using System.Numerics;
using Bodian.Core.Models.Lyrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Windows.Foundation;

namespace Bodian.WinUI.LyricRenderer;

/// <summary>
/// 一行在这一帧里的视觉参数。每帧复用同一个数组，不重新分配。
/// </summary>
internal struct LyricLineVisual
{
    /// <summary>行中心在<b>视口空间</b>里的 Y。</summary>
    public double CenterY;

    public double Scale;

    public double Opacity;

    public double BlurAmount;

    public bool IsCurrent;

    /// <summary>是否落在视口（含预留边距）内。落在外面就不用画。</summary>
    public bool Visible;

    /// <summary>该行在文档空间里的 Y，绘制用。</summary>
    public float DocumentTop;
}

/// <summary>
/// 歌词渲染的编排：帧状态、行定位、绘制循环。
/// </summary>
/// <remarks>
/// <para>
/// <b>渲染线程独占。</b> <see cref="Draw"/> 只读 <see cref="Update"/> 算好的帧状态，
/// 两边共用同一个 <see cref="LyricLineVisual"/> 数组。
/// </para>
/// <para>
/// <b>已完成</b>：排版、当前行放大、远景模糊、边缘羽化、逐字扫光、滚动到当前行（瞬移）。
/// <b>还没有</b>：波浪式滚动（S5）—— 入口就是 <see cref="Update"/> 里算 <c>_scrollOffset</c> 那几行。
/// </para>
/// <para>
/// 参数的来源见 <see cref="LyricsRenderSettings"/>：<c>distanceFactor</c> 一律<b>按像素距离</b>算，
/// 不是按行下标 —— 当前行会被放大，行高与别的行不同，按下标算会和视觉对不上。
/// </para>
/// </remarks>
internal sealed class LyricsRenderer(
    LyricsRenderSettings settings,
    ILogger<LyricsRenderer>? logger = null)
{
    private readonly ILogger<LyricsRenderer> _logger =
        logger ?? NullLogger<LyricsRenderer>.Instance;

    /// <summary>
    /// 已唱 / 未唱的颜色。**由外层按主题给**，不是常量。
    /// </summary>
    /// <remarks>
    /// 浅色主题下写死白色就是白底白字 —— 字还在，但一个也看不见，表现是「歌词没了」。
    /// 颜色必须跟着主题走：已唱用主前景色，未唱用更淡的那一档。
    /// </remarks>
    private Windows.UI.Color _playedColor = Windows.UI.Color.FromArgb(255, 32, 32, 32);

    private Windows.UI.Color _unplayedColor = Windows.UI.Color.FromArgb(255, 128, 128, 128);

    private ICanvasResourceCreator? _creator;
    private LyricsDeviceResources? _device;

    private LyricDocument _document = LyricDocument.Empty;
    private bool _layoutDirty = true;

    private double _viewportWidth;
    private double _viewportHeight;

    private LyricLineVisual[] _visuals = new LyricLineVisual[0];
    private Rect[] _hitRects = new Rect[0];
    private double _scrollOffset;

    /// <summary>本帧的播放位置。<see cref="Update"/> 写入，<see cref="Draw"/> 读 —— 两者同线程同帧。</summary>
    private TimeSpan _position;

    /// <summary>波浪式滚动。</summary>
    private readonly LyricsScrollAnimator _scroll = new();

    /// <summary>当前行本帧的滚动偏移。既是最新一轮动画的起点，也发布给命中测试。</summary>
    private double _currentLineOffset;

    /// <summary>上一次看到的硬跳计数。变了就说明发生了 seek/切歌，滚动直接归位不做波浪。</summary>
    private long _lastJumpCount = -1;

    /// <summary>当前视口里正在唱的行。给命中测试与滚动用。</summary>
    public int CurrentIndex { get; private set; } = -1;

    /// <summary>
    /// 上一帧被逐字上色的那一行（<c>-1</c> 表示没有）。
    /// </summary>
    /// <remarks>
    /// 逐字上色是<b>改 layout 上的逐字颜色</b>，会留在 layout 上。切行时要把上一行改回未唱色，
    /// 否则它会保留着上一帧的高亮。
    /// </remarks>
    private int _sweepLine = -1;

    /// <summary>
    /// 把视口坐标的 Y 换成行下标。
    /// </summary>
    /// <remarks>
    /// 供 UI 线程（指针事件）调用。只读两个字段：<see cref="_hitRects"/> 在换歌时才整体替换，
    /// <see cref="_scrollOffset"/> 每帧变 —— 两者之间差一帧对「点哪句跳哪句」没有影响。
    /// </remarks>
    public int LineIndexAt(double y)
    {
        var rects = Volatile.Read(ref _hitRects);
        var documentY = y + Volatile.Read(ref _scrollOffset);

        for (var i = 0; i < rects.Length; i++)
        {
            if (documentY >= rects[i].Y && documentY <= rects[i].Y + rects[i].Height)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// 设备相关资源整体重建。只在 <c>CreateResources</c> 里调。
    /// </summary>
    /// <param name="device">
    /// <b>传控件的 <c>Device</c>（<see cref="CanvasDevice"/>），不要传控件本身。</b>
    /// 排版是在 <c>Update</c> 里懒建的 —— 用控件当资源创建者时 Win2D 只允许在
    /// <c>CreateResources</c> 期间建资源，在其之外建会失败。设备对象没有这个限制。
    /// </param>
    public void RebuildDeviceResources(CanvasDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        _device?.Dispose();

        _creator = device;
        _device = new LyricsDeviceResources(device, settings);

        // 旧的排版快照挂在旧设备上，已经失效 —— 一起丢掉，等下一帧重建。
        _layoutDirty = true;
    }

    /// <summary>设置已唱 / 未唱的颜色。渲染线程调用（由外层按主题取色后投递进来）。</summary>
    public void SetColors(Windows.UI.Color played, Windows.UI.Color unplayed)
    {
        _playedColor = played;
        _unplayedColor = unplayed;
    }

    /// <summary>换歌。渲染线程调用。</summary>
    public void SetDocument(LyricDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (ReferenceEquals(document, _document))
        {
            return;
        }

        _document = document;
        _layoutDirty = true;
    }

    /// <summary>视口尺寸变化。宽度变了要重排，高度变了只要重算滚动位置。</summary>
    public void SetViewport(double width, double height)
    {
        if (Math.Abs(width - _viewportWidth) > 0.5)
        {
            _layoutDirty = true;
        }

        _viewportWidth = width;
        _viewportHeight = height;
    }

    /// <summary>
    /// 每帧一次：推进滚动动画，并算出每行的视觉参数。
    /// </summary>
    /// <param name="position">本帧的播放位置。</param>
    /// <param name="now">
    /// 单调时钟。<b>滚动动画用它而不是播放位置</b> —— 暂停时位置不动，但已经起头的滚动还得走完。
    /// </param>
    /// <param name="jumpCount">时钟的硬跳计数；变大说明发生了 seek 或切歌。</param>
    public void Update(TimeSpan position, TimeSpan now, long jumpCount)
    {
        _position = position;

        EnsureLayout();

        if (_device?.Snapshot is not { Count: > 0 } snapshot)
        {
            CurrentIndex = -1;
            _scroll.JumpTo(0);
            Volatile.Write(ref _scrollOffset, 0);
            return;
        }

        // 曲头空白段没有「当前行」，但滚动的波源总得有 —— 以第一行为它。
        var current = snapshot.Document.IndexOfLineAt(position);
        if (current < 0)
        {
            current = 0;
        }

        var lineChanged = current != CurrentIndex;
        CurrentIndex = current;

        var currentCenterAtTarget = snapshot.Tops[current] + (snapshot.Lines[current].Height / 2);
        var target = currentCenterAtTarget - (_viewportHeight * settings.PlayingLineTopOffsetFactor);

        if (jumpCount != _lastJumpCount)
        {
            // seek / 切歌：直接归位。这时做波浪看起来像卡顿。
            _lastJumpCount = jumpCount;
            _scroll.JumpTo(target);
        }
        else if (lineChanged)
        {
            _scroll.ScrollTo(_currentLineOffset, target, now);
        }

        // 可见行数：用平均行高估一个，供错峰曲线的归一化使用。
        var averageHeight = Math.Max(1.0, snapshot.TotalHeight / Math.Max(1, snapshot.Count));
        var visibleCount = Math.Max(1, (int)(_viewportHeight / averageHeight));

        var maxDuration = Math.Max(settings.ScrollTopDuration, settings.ScrollBottomDuration);
        var maxDelay = LyricEffectMath.StaggerDelay(
            visibleCount,
            maxDuration,
            settings.ScrollStaggerBudgetRatio,
            settings.ScrollStaggerBudgetMax,
            settings.ScrollStaggerPerLine);

        var settled = _scroll.IsSettled(now, maxDuration, maxDelay);

        _currentLineOffset = LineOffset(current, visibleCount, now, settled);
        var playingCenter = currentCenterAtTarget - _currentLineOffset;

        // 发布给命中测试。逐行偏移之下这是个近似值（用的是当前行的偏移），
        // 对「点哪句跳哪句」足够。
        Volatile.Write(ref _scrollOffset, _currentLineOffset);

        var margin = settings.BaseFontSize * settings.LineHeight * settings.ViewportMarginLines;
        var edgeBand = _viewportHeight * settings.EdgeFadeRatio;
        var maxScaleDelta = settings.CurrentLineScale - settings.InactiveLineScale;
        var maxOpacityDelta = 1 - settings.InactiveLineOpacity;

        for (var i = 0; i < snapshot.Count; i++)
        {
            var height = snapshot.Lines[i].Height;
            var offset = i == current ? _currentLineOffset : LineOffset(i, visibleCount, now, settled);
            var top = snapshot.Tops[i] - (float)offset;
            var center = top + (height / 2);

            var distance = LyricEffectMath.DistanceFactor(center, playingCenter, spaceBefore: _viewportHeight * settings.PlayingLineTopOffsetFactor, spaceAfter: _viewportHeight * (1 - settings.PlayingLineTopOffsetFactor));
            var closeness = 1 - distance;

            // 边缘羽化：越靠近上下边缘越淡。当前行停在固定比例处，正常不会吃到这个衰减。
            var edge = edgeBand <= 0
                ? 1
                : Math.Min(
                    Math.Clamp(center / edgeBand, 0, 1),
                    Math.Clamp((_viewportHeight - center) / edgeBand, 0, 1));

            _visuals[i] = new LyricLineVisual
            {
                CenterY = center,
                DocumentTop = top,
                Scale = settings.InactiveLineScale + (maxScaleDelta * closeness),
                Opacity = (settings.InactiveLineOpacity + (maxOpacityDelta * closeness)) * edge,
                BlurAmount = settings.FarBlurAmount * distance,
                IsCurrent = i == current,
                Visible = center > -margin && center < _viewportHeight + margin,
            };
        }
    }

    /// <summary>
    /// 某一行的滚动偏移：<b>每行各自的时长与延迟</b>，这就是波浪的来源。
    /// </summary>
    /// <remarks>
    /// 波源放在<b>当前行</b>（延迟为 0），而不是「首个可见行」—— 上游用的是后者。
    /// 这里取当前行是因为让正在唱的那一行先动、上下依次跟上，看起来更贴着唱词走。
    /// </remarks>
    private double LineOffset(int index, int visibleCount, TimeSpan now, bool settled)
    {
        if (settled)
        {
            return _scroll.Target;
        }

        var distance = Math.Abs(index - CurrentIndex);
        var normalized = Math.Clamp(distance / (double)visibleCount, 0, 1);

        var duration = LyricEffectMath.ScrollDuration(
            settings.ScrollBaseDuration,
            normalized,
            distance,
            visibleCount,
            settings.ScrollTopDuration,
            settings.ScrollBottomDuration);

        var delay = LyricEffectMath.StaggerDelay(
            distance,
            duration,
            settings.ScrollStaggerBudgetRatio,
            settings.ScrollStaggerBudgetMax,
            settings.ScrollStaggerPerLine);

        return _scroll.OffsetAt(duration, delay, now);
    }

    /// <summary>每帧一次：把 <see cref="Update"/> 算好的帧状态画出来。</summary>
    public void Draw(CanvasDrawingSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (_device?.Snapshot is not { Count: > 0 } snapshot)
        {
            return;
        }

        var sweep = CurrentIndex;

        if (sweep >= snapshot.Count || (sweep >= 0 && snapshot.Document.Lines[sweep].Syllables.Count == 0))
        {
            sweep = -1;
        }

        // 切行了：把上一行改回未唱色，否则它留着上一帧的高亮。
        if (_sweepLine >= 0 && _sweepLine != sweep && _sweepLine < snapshot.Count)
        {
            ClearSweep(snapshot, _sweepLine);
            _sweepLine = -1;
        }

        for (var i = 0; i < snapshot.Count && i < _visuals.Length; i++)
        {
            var visual = _visuals[i];

            if (!visual.Visible || visual.Opacity <= 0.01)
            {
                continue;
            }

            if (i == sweep)
            {
                ApplySweep(snapshot, i, _playedColor);
                _sweepLine = i;
            }

            DrawLine(session, snapshot, i, visual);
        }
    }

    public void Dispose()
    {
        _device?.Dispose();
        _device = null;
        _creator = null;
        Volatile.Write(ref _hitRects, []);
    }

    private void DrawLine(
        CanvasDrawingSession session,
        LyricsLayoutSnapshot snapshot,
        int index,
        in LyricLineVisual visual)
    {
        var creator = _creator;
        var device = _device;

        if (creator is null || device is null)
        {
            return;
        }

        var rendered = snapshot.Lines[index];
        var text = snapshot.Document.Lines[index];
        var unplayed = WithOpacity(_unplayedColor, visual.Opacity);

        // 变换：行局部坐标 → 文档坐标（平移），再绕行中心缩放。
        // Matrix3x2 的 A * B 是「先 A 后 B」，所以平移写在前面。
        var center = new Vector2((float)(_viewportWidth / 2), (float)visual.CenterY);
        var previous = session.Transform;
        session.Transform = Matrix3x2.CreateTranslation(0f, visual.DocumentTop)
                            * Matrix3x2.CreateScale((float)visual.Scale, center)
                            * previous;

        if (visual.IsCurrent)
        {
            var longSyllables = text.Syllables.Count > 0
                                && snapshot.Document.Kind == LyricKind.WordByWord
                                && rendered.LongSyllables.Length > 0;

            if (longSyllables)
            {
                DrawLongSyllableGlow(session, creator, device, rendered, text, visual);
            }

            // 逐字颜色已经设在 layout 上（见 ApplySweep），这里一次画完即可。
            // 传进去的基础色只对「没有被逐字覆盖」的字生效，也就是行尾空白。
            session.DrawTextLayout(rendered.Layout, 0f, 0f, unplayed);

            if (longSyllables)
            {
                DrawLongSyllables(session, rendered, text, visual);
            }
        }
        else
        {
            DrawPlainLine(session, device, creator, rendered.Layout, unplayed, visual.BlurAmount);
        }

        session.Transform = previous;
    }

    /// <summary>
    /// 整行一个颜色。非当前行走这里，没有逐字时间轴的当前行也走这里。
    /// </summary>
    private static void DrawPlainLine(
        CanvasDrawingSession session,
        LyricsDeviceResources device,
        ICanvasResourceCreator creator,
        CanvasTextLayout layout,
        Windows.UI.Color color,
        double blurAmount)
    {
        if (blurAmount <= 0.05)
        {
            // 不模糊就直接画，连离屏图都不用建。
            session.DrawTextLayout(layout, 0f, 0f, color);
            return;
        }

        // 模糊需要一个图像源。command list 用过一次就不能再录制，所以这张是每帧新建、
        // 用完即弃的 —— 不能像效果对象那样复用一个字段。
        using var layer = new CanvasCommandList(creator);

        using (var buffer = layer.CreateDrawingSession())
        {
            buffer.DrawTextLayout(layout, 0f, 0f, color);
        }

        device.Blur.Source = layer;
        device.Blur.BlurAmount = (float)blurAmount;

        // 无偏移：内容本来就录在行局部坐标上，位置由上面的变换负责。
        session.DrawImage(device.Blur);
    }

    /// <summary>
    /// 给当前行的字上色：整行先设未唱色，再把已唱的那几个字设成已唱色。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>用 <see cref="CanvasTextLayout.SetColor"/> 按字上色，不做任何裁剪。</b>
    /// 之前用矩形裁剪实现，连撞两次：Win2D 唯一的裁剪 API 是 <c>CreateLayer</c>，而它的矩形
    /// 坐标语义与绘制变换的关系没有可靠依据 —— 带变换传进去，当前行整行落在图层外，一个字都看不见。
    /// 按字上色只用到颜色值：没有图层、没有离屏图、没有画刷对象，没有语义可猜。
    /// </para>
    /// <para>
    /// <b>粒度是「字」。</b> 中文歌词一个字基本就是一个音节，所以逐字推进与逐音节推进等价；
    /// 代价是丢掉了「一个字唱到一半时高亮停在字中间」的像素级过渡。
    /// 要那个效果得上渐变画刷（<c>SetBrush</c>），作为后续打磨项，不再拿它冒险。
    /// </para>
    /// </remarks>
    private void ApplySweep(LyricsLayoutSnapshot snapshot, int index, Windows.UI.Color played)
    {
        var rendered = snapshot.Lines[index];
        var text = snapshot.Document.Lines[index];
        var span = rendered.CharBounds.Length;

        if (span == 0)
        {
            return;
        }

        var unplayed = _unplayedColor;

        // 整行先归到未唱色，再把已唱的覆盖上去 —— 顺带把上一次残留的高亮清干净。
        rendered.Layout.SetColor(0, span, unplayed);

        var count = PlayedCharCount(rendered, text, _position);

        if (count > 0)
        {
            rendered.Layout.SetColor(0, count, played);
        }

        // 长音音节要从主行里排除出去 —— 它们下面会被单独画（缩放/上浮/发光）。
        // 不排除就会画两遍：一份原尺寸垫在底下，一份放大后压在上面。
        if (snapshot.Document.Kind == LyricKind.WordByWord)
        {
            foreach (var syllable in rendered.LongSyllables)
            {
                var (start, length) = CharRangeOf(rendered, syllable.SyllableIndex);

                if (length > 0)
                {
                    rendered.Layout.SetColor(start, length, Windows.UI.Color.FromArgb(0, 0, 0, 0));
                }
            }
        }
    }

    /// <summary>某个音节覆盖的字符区间。</summary>
    private static (int Start, int Count) CharRangeOf(LyricsLineLayout rendered, int syllableIndex)
    {
        var start = -1;
        var end = 0;

        for (var i = 0; i < rendered.CharSyllable.Length; i++)
        {
            if (rendered.CharSyllable[i] != syllableIndex)
            {
                continue;
            }

            if (start < 0)
            {
                start = i;
            }

            end = i + 1;
        }

        return start < 0 ? (0, 0) : (start, end - start);
    }

    /// <summary>
    /// 长音效果的脉冲：<c>sin(π × 进度)</c> —— 开口时渐入、唱到中间最强、收尾渐出。
    /// </summary>
    /// <remarks>
    /// 上游用的是「时长 + 延迟」的过渡对象来驱动这几个量；这里改用进度驱动的正弦，
    /// 少一层状态，也天然解决「放大之后怎么缩回去」。曲线形状是等价的一进一出。
    /// </remarks>
    private static double Pulse(double progress) => Math.Sin(Math.PI * Math.Clamp(progress, 0, 1));

    /// <summary>把某一行改回全部未唱色。</summary>
    private void ClearSweep(LyricsLayoutSnapshot snapshot, int index)
    {
        var rendered = snapshot.Lines[index];
        var span = rendered.CharBounds.Length;

        if (span > 0)
        {
            rendered.Layout.SetColor(0, span, _unplayedColor);
        }
    }

    /// <summary>
    /// 已经开口唱的字数。
    /// </summary>
    /// <remarks>
    /// 一个字只要<b>开始</b>唱就点亮（而不是等它唱完）—— 逐字推进时这样才跟手，
    /// 否则高亮会慢一个字。
    /// </remarks>
    private static int PlayedCharCount(LyricsLineLayout rendered, LyricLine text, TimeSpan position)
    {
        var count = 0;

        for (var i = 0; i < rendered.CharSyllable.Length; i++)
        {
            var syllableIndex = rendered.CharSyllable[i];

            if (syllableIndex < 0 || syllableIndex >= text.Syllables.Count)
            {
                break;
            }

            if (text.Syllables[syllableIndex].ProgressAt(position) <= 0)
            {
                break;
            }

            count = i + 1;
        }

        return count;
    }

    /// <summary>
    /// 长音拖尾发光：把「已经开口」的长音字画进一张离屏图、整张高斯模糊，垫在文字下面。
    /// </summary>
    /// <remarks>
    /// 上游是<b>逐字</b>挂 <c>CropEffect</c> + <c>GaussianBlurEffect</c>；这里把同一行里所有
    /// 已开口的长音字合到一张图上一起模糊。一行通常只有一两个长音字，两者的观感一致，
    /// 但少了逐字的裁剪层。离屏图每帧新建、用完即弃（command list 用过就不能再录）。
    /// </remarks>
    private void DrawLongSyllableGlow(
        CanvasDrawingSession session,
        ICanvasResourceCreator creator,
        LyricsDeviceResources device,
        LyricsLineLayout rendered,
        LyricLine text,
        in LyricLineVisual visual)
    {
        var layer = new CanvasCommandList(creator);
        var any = false;

        using (var buffer = layer.CreateDrawingSession())
        {
            foreach (var syllable in rendered.LongSyllables)
            {
                var progress = text.Syllables[syllable.SyllableIndex].ProgressAt(_position);
                var pulse = Pulse(progress);

                if (pulse <= 0.01)
                {
                    continue;
                }

                var color = WithOpacity(_playedColor, visual.Opacity * 0.7 * pulse);
                var bounds = syllable.Bounds;
                var origin = syllable.Layout.LayoutBounds;

                buffer.DrawTextLayout(
                    syllable.Layout,
                    (float)(bounds.X - origin.X),
                    (float)(bounds.Y - origin.Y),
                    color);

                any = true;
            }
        }

        if (any)
        {
            device.Blur.Source = layer;
            device.Blur.BlurAmount = (float)(settings.BaseFontSize * settings.LineHeight * settings.LongSyllableGlowRatio);
            session.DrawImage(device.Blur);
        }

        layer.Dispose();
    }

    /// <summary>
    /// 逐个画长音音节：绕自身中心放大 + 上浮。
    /// </summary>
    /// <remarks>
    /// 这些音节已经在 <see cref="ApplySweep"/> 里从主行排除（设成透明），所以这里是它们唯一的绘制点。
    /// <b>没开口的也要画</b> —— 否则它们会整个消失。
    /// </remarks>
    private void DrawLongSyllables(
        CanvasDrawingSession session,
        LyricsLineLayout rendered,
        LyricLine text,
        in LyricLineVisual visual)
    {
        foreach (var syllable in rendered.LongSyllables)
        {
            var progress = text.Syllables[syllable.SyllableIndex].ProgressAt(_position);
            var pulse = Pulse(progress);

            var color = WithOpacity(progress > 0 ? _playedColor : _unplayedColor, visual.Opacity);
            var bounds = syllable.Bounds;
            var origin = syllable.Layout.LayoutBounds;

            // 颜色直接设在它自己的那份小排版上（字数很少，重新格式化的代价可忽略）。
            syllable.Layout.SetColor(0, syllable.CharCount, color);

            var scale = 1 + ((settings.LongSyllableScale - 1) * pulse);
            var floatY = -(settings.BaseFontSize * settings.LineHeight * settings.FloatRatio * pulse);

            var center = new Vector2(
                (float)(bounds.X + (bounds.Width / 2)),
                (float)(bounds.Y + (bounds.Height / 2)));

            var previous = session.Transform;
            session.Transform = Matrix3x2.CreateScale((float)scale, center)
                                * Matrix3x2.CreateTranslation(0f, (float)floatY)
                                * previous;

            session.DrawTextLayout(
                syllable.Layout,
                (float)(bounds.X - origin.X),
                (float)(bounds.Y - origin.Y),
                color);

            session.Transform = previous;
        }
    }

    private void EnsureLayout()
    {
        if (!_layoutDirty || _creator is null || _device is null || _viewportWidth <= 0)
        {
            return;
        }

        _layoutDirty = false;

        _device.Snapshot?.Dispose();
        _device.Snapshot = _device.LayoutEngine.Build(_creator, _document, _viewportWidth);

        // 旧 layout 已经释放，跟着它走的逐字颜色也一并消失。
        _sweepLine = -1;

        var count = _device.Snapshot.Count;

        // 长音音节数直接决定缩放/发光/浮动会不会出现，出问题时第一眼看它。
        var longSyllables = 0;

        for (var i = 0; i < count; i++)
        {
            longSyllables += _device.Snapshot.Lines[i].LongSyllables.Length;
        }

        _logger.LogInformation(
            "歌词排版完成：{Lines} 行，其中长音音节 {Long} 个，视口 {Width:F0}×{Height:F0}",
            count,
            longSyllables,
            _viewportWidth,
            _viewportHeight);

        if (_visuals.Length < count)
        {
            _visuals = new LyricLineVisual[count];
        }

        // 命中测试的矩形只在换歌/换宽度时变，所以在这里建一次，UI 线程可以长期持有。
        var rects = new Rect[count];

        for (var i = 0; i < count; i++)
        {
            rects[i] = new Rect(0, _device.Snapshot.Tops[i], _viewportWidth, _device.Snapshot.Lines[i].Height);
        }

        Volatile.Write(ref _hitRects, rects);
    }

    private static Windows.UI.Color WithOpacity(Windows.UI.Color color, double opacity)
        => Windows.UI.Color.FromArgb(
            (byte)Math.Clamp(opacity * 255, 0, 255),
            color.R,
            color.G,
            color.B);
}
