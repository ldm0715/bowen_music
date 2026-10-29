using System.Numerics;
using Bowen.Core.Models;
using Bowen.Core.Models.Lyrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using Windows.Foundation;
using Windows.UI;

namespace Bowen.WinUI.LyricRenderer;

/// <summary>全屏歌词：稳定焦点、逐句弹簧滚动、连续字形渐变和长音发光。</summary>
internal sealed class LyricsRenderer(LyricsRenderSettings settings, ILogger<LyricsRenderer>? logger = null)
{
    private readonly ILogger<LyricsRenderer> _logger = logger ?? NullLogger<LyricsRenderer>.Instance;
    private readonly LyricsScrollAnimator _scroll = new();
    private LyricsDeviceResources? _device;
    private CanvasDevice? _creator;
    private LyricDocument _document = LyricDocument.Empty;
    private bool _layoutDirty = true;
    private readonly LinkedList<CachedLayout> _layouts = new();
    private bool _maskDirty = true;
    private double _width, _height;
    private float _dpi = 96;
    private double? _browseTarget;
    private TimeSpan _browseUntil;
    private double? _pointerY;
    private int _focusIndex = -1;
    private TimeSpan _position, _lastFrame;
    private long _lastJump = -1;
    private double[] _scales = [];
    private double[] _scaleVelocities = [];
    private readonly object _hitRectGate = new();
    private Rect[] _hitRects = [];
    private Rect[] _nextHitRects = [];
    private bool _scaleAnimating;
    private double[] _centers = [];
    private CanvasLinearGradientBrush? _edgeBrush;
    private bool _blurredCreated;
    private Color _played = Color.FromArgb(242, 255, 255, 255);
    private Color _unplayed = Color.FromArgb(80, 255, 255, 255);

    // 未播放行固定用白色。高亮色只作用于当前行，不能让整首歌词跟着染色。
    private static readonly Color InactiveTextColor = Color.FromArgb(242, 255, 255, 255);

    // 选中底与侧栏一致：SelectionPillBrush 深色值 #1AFFFFFF + ControlCornerRadius 6。
    // 歌词页固定 RequestedTheme="Dark"，所以只用深色那一档。
    private static readonly Color SelectionPillColor = Color.FromArgb(0x1A, 255, 255, 255);
    private const float SelectionPillCornerRadius = 6;
    public int CurrentIndex { get; private set; } = -1;
    public bool IsBrowsing => _browseTarget.HasValue;

    public void SetDpi(float dpi)
    {
        if (Math.Abs(_dpi - dpi) < 0.1) return;
        _dpi = dpi;
        _maskDirty = true;
    }

    public void ScrollBy(double delta, TimeSpan now)
    {
        if (_device?.Snapshot is not { Count: > 0 } snapshot || _scroll.Count != snapshot.Count) return;
        var anchor = Math.Clamp(CurrentIndex, 0, snapshot.Count - 1);
        var focusY = _height * settings.PlayingLineTopOffsetFactor;
        var verticalOffset = VerticalOffsetPixels;
        var minimum = snapshot.UnitCenters[0] - focusY - verticalOffset;
        var maximum = snapshot.UnitCenters[^1] - focusY - verticalOffset;
        _browseTarget = Math.Clamp((_browseTarget ?? _scroll.OffsetAt(anchor)) + delta, minimum, Math.Max(minimum, maximum));
        _browseUntil = now + TimeSpan.FromSeconds(4);
        _scroll.Retarget(_browseTarget.Value, anchor, stagger: false);
    }

    public void ResumeFollowing() => _browseUntil = TimeSpan.Zero;

    public void SetPointerY(double? pointerY, TimeSpan now)
    {
        _pointerY = pointerY;
        if (IsBrowsing && pointerY.HasValue) _browseUntil = now + TimeSpan.FromSeconds(4);
    }

    public int LineIndexAt(double y)
    {
        lock (_hitRectGate)
        {
            for (var i = 0; i < _hitRects.Length; i++)
                if (_hitRects[i].Height > 0 && y >= _hitRects[i].Top && y <= _hitRects[i].Bottom) return i;
            return -1;
        }
    }

    public void RebuildDeviceResources(CanvasDevice device)
    {
        Dispose();
        _creator = device;
        _device = new LyricsDeviceResources(device, settings);
        _layoutDirty = _maskDirty = true;
    }

    public void SetColors(Color played, Color unplayed)
    {
        if (_played == played && _unplayed == unplayed) return;
        _played = played;
        _unplayed = unplayed;

        // 颜色只影响当前行的活动层与长音发光，未播放行固定白色。
        // 不重排整份文档，只释放活动层缓存，下一帧按新颜色重建。
        if (_device?.Snapshot is { } snapshot)
        {
            foreach (var line in snapshot.Lines) line.ReleaseActiveResources();
        }
    }

    /// <summary>改已唱部分的高亮色。未唱部分保持白色半透明。</summary>
    public void SetPlayedColor(Color played) => SetColors(played, _unplayed);

    /// <summary>改行距。0 合法：文本自身行高仍由 LineHeight 提供，不会重叠。</summary>
    public void SetLineGap(double lineGap)
    {
        lineGap = double.IsFinite(lineGap)
            ? Math.Clamp(lineGap, LyricsSettings.MinimumLineGap, LyricsSettings.MaximumLineGap)
            : LyricsSettings.DefaultLineGap;
        if (Math.Abs(settings.LineGap - lineGap) < 0.01) return;
        settings.LineGap = lineGap;
        _layoutDirty = true;
    }

    /// <summary>改横向对齐。文字排版与绘制原点都要跟着变，整份文档重排。</summary>
    public void SetAlignment(LyricsAlignment alignment)
    {
        if (!Enum.IsDefined(alignment)) alignment = LyricsAlignment.Left;
        if (settings.Alignment == alignment) return;
        settings.Alignment = alignment;
        _layoutDirty = true;
    }

    /// <summary>改水平偏移，占歌词区域宽度的百分比。只改绘制原点，不重排版。</summary>
    public void SetHorizontalOffsetPercent(double percent)
    {
        percent = double.IsFinite(percent)
            ? Math.Clamp(percent, LyricsSettings.MinimumHorizontalOffsetPercent, LyricsSettings.MaximumHorizontalOffsetPercent)
            : LyricsSettings.DefaultOffsetPercent;
        if (Math.Abs(settings.HorizontalOffsetPercent - percent) < 0.01) return;
        settings.HorizontalOffsetPercent = percent;
    }

    /// <summary>改垂直偏移，占歌词区域高度的百分比。下一帧重新对齐滚动目标。</summary>
    public void SetVerticalOffsetPercent(double percent)
    {
        percent = double.IsFinite(percent)
            ? Math.Clamp(percent, LyricsSettings.MinimumVerticalOffsetPercent, LyricsSettings.MaximumVerticalOffsetPercent)
            : LyricsSettings.DefaultOffsetPercent;
        if (Math.Abs(settings.VerticalOffsetPercent - percent) < 0.01) return;
        settings.VerticalOffsetPercent = percent;

        // 调整版式时回到跟随状态，避免浏览位置压住新的偏移。
        if (IsBrowsing)
        {
            _browseTarget = null;
            _browseUntil = TimeSpan.Zero;
        }
    }

    public void SetDocument(LyricDocument document)
    {
        if (ReferenceEquals(_document, document)) return;
        _document = document;
        _layoutDirty = true;
        _lastJump = -1;
    }

    public void SetFontSize(double fontSize)
    {
        fontSize = double.IsFinite(fontSize)
            ? Math.Clamp(fontSize, LyricsSettings.MinimumFontSize, LyricsSettings.MaximumFontSize)
            : LyricsSettings.DefaultFontSize;
        if (Math.Abs(settings.BaseFontSize - fontSize) < 0.5) return;
        settings.BaseFontSize = fontSize;
        _layoutDirty = true;
    }

    /// <summary>切换字体：改设置并标记整份文档重排。渲染线程内调用。</summary>
    public void SetFontFamily(string fontFamily)
    {
        fontFamily = string.IsNullOrWhiteSpace(fontFamily) ? LyricsRenderSettings.Default.FontFamily : fontFamily.Trim();
        if (string.Equals(settings.FontFamily, fontFamily, StringComparison.Ordinal)) return;
        settings.FontFamily = fontFamily;
        _layoutDirty = true;
    }

    public void SetViewport(double width, double height)
    {
        if (Math.Abs(width - _width) > 0.5) _layoutDirty = true;
        if (Math.Abs(width - _width) > 0.5 || Math.Abs(height - _height) > 0.5) _maskDirty = true;
        _width = width;
        _height = height;
    }

    public bool IsMotionAnimating => _scroll.IsAnimating || _scaleAnimating;

    public bool NeedsContinuousFrames(bool playing)
    {
        if (IsMotionAnimating) return true;
        if (!playing || IsBrowsing || _document.Kind != LyricKind.WordByWord || CurrentIndex < 0 || CurrentIndex >= _document.Lines.Count) return false;
        foreach (var syllable in _document.Lines[CurrentIndex].Syllables)
            if (syllable.Duration > TimeSpan.Zero && _position >= syllable.Start && _position < syllable.End) return true;
        return false;
    }

    public bool Update(TimeSpan position, TimeSpan now, long jumpCount)
    {
        var previousPosition = _position;
        var previousIndex = CurrentIndex;
        var wasBrowsing = IsBrowsing;
        var changed = _layoutDirty || _maskDirty;
        _position = LyricMotionMath.AdvanceHighlight(_position, position, _lastJump != jumpCount);
        var rebuilt = EnsureLayout();
        if (_device?.Snapshot is not { Count: > 0 } snapshot || _height <= 0)
        {
            CurrentIndex = -1;
            _scaleAnimating = false;
            if (_scroll.Count > 0) _scroll.Reset([], [], [], 0);
            EnsureMask();
            return changed;
        }
        var current = Math.Max(0, snapshot.Document.IndexOfLineAt(_position));
        // 锚点对齐整个滚动单元的中心：双行歌词绕焦点线居中，单行时与行中心相同。
        // 垂直偏移按歌词区域高度换算成 DIP，叠加在焦点线上。
        var focusY = _height * settings.PlayingLineTopOffsetFactor;
        var verticalOffset = VerticalOffsetPixels;
        var unit = snapshot.UnitOfLine[current];
        var target = snapshot.UnitCenters[unit] - focusY - verticalOffset;
        var seconds = _lastFrame == default ? 1.0 / 60 : Math.Clamp((now - _lastFrame).TotalSeconds, 0, 1);
        _lastFrame = now;
        if (rebuilt || _lastJump != jumpCount || _scroll.Count != snapshot.Count)
        {
            _browseTarget = null;
            _scroll.Reset(snapshot.UnitOfLine, snapshot.UnitTops, snapshot.UnitHeights, target);
            _lastJump = jumpCount;
        }
        else if (_browseTarget.HasValue && now >= _browseUntil)
        {
            _browseTarget = null;
            _scroll.Retarget(target, current);
        }
        else if (!_browseTarget.HasValue && (current != CurrentIndex || Math.Abs(target - _scroll.Target) > 0.5))
        {
            _scroll.Retarget(target, current);
        }
        CurrentIndex = current;
        _scroll.Update(seconds);
        var centers = _centers;
        for (var index = 0; index < snapshot.Count; index++)
            centers[index] = snapshot.Tops[index] + snapshot.Lines[index].Height / 2 - _scroll.OffsetAt(index);
        _focusIndex = IsBrowsing
            ? LyricMotionMath.NearestLine(centers, focusY + verticalOffset)
            : current;
        var rects = _nextHitRects;
        _scaleAnimating = false;
        for (var i = 0; i < snapshot.Count; i++)
        {
            // 浏览时不放大任何行：焦点只归鼠标，行本身不再承担强调。
            var active = !IsBrowsing && i == current && _position >= snapshot.Document.Lines[i].Start;
            var scaleTarget = active ? settings.CurrentLineScale : settings.InactiveLineScale;
            var previousScale = _scales[i];
            (_scales[i], _scaleVelocities[i]) = LyricMotionMath.AdvanceSpring(
                _scales[i], _scaleVelocities[i], scaleTarget, seconds, 16, 1);
            if (Math.Abs(_scales[i] - scaleTarget) < 0.0001 && Math.Abs(_scaleVelocities[i]) < 0.0001)
                (_scales[i], _scaleVelocities[i]) = (scaleTarget, 0);
            else _scaleAnimating = true;
            changed |= previousScale != _scales[i];
            var height = snapshot.Lines[i].Height;
            var top = snapshot.Tops[i] - _scroll.OffsetAt(i);
            rects[i] = new Rect(0, top + height * (1 - _scales[i]) / 2 - settings.LineGap / 2,
                _width, height * _scales[i] + settings.LineGap);
        }
        // 点击使用这一帧各行的实际位置。
        lock (_hitRectGate) Array.Copy(rects, _hitRects, rects.Length);
        // 包含音节结束的最后一帧；跨过短音节或停在间隙也要提交正确的颜色。
        if (!IsBrowsing && _document.Kind == LyricKind.WordByWord && previousPosition != _position)
            foreach (var syllable in _document.Lines[current].Syllables)
                if (syllable.ProgressAt(previousPosition) != syllable.ProgressAt(_position)) { changed = true; break; }
        return changed || rebuilt || _scroll.HasMoved || current != previousIndex || wasBrowsing != IsBrowsing;
    }

    public void Draw(CanvasDrawingSession session)
    {
        if (_creator is null || _device?.Snapshot is not { Count: > 0 } snapshot || _height <= 0) return;
        EnsureMask();
        var hovered = _pointerY.HasValue ? LineIndexAt(_pointerY.Value) : -1;
        // 选中底按「句」铺：鼠标划过原文或译文，整句（原文 + 译文）一起被包住。
        // 非浏览时播放中的那一句本身已有逐字扫色，不再叠底。
        var selectedUnit = -1;
        if (hovered >= 0)
        {
            selectedUnit = snapshot.UnitOfLine[hovered];
            var activeUnit = !IsBrowsing && CurrentIndex >= 0 && CurrentIndex < snapshot.Count
                && _position >= snapshot.Document.Lines[CurrentIndex].Start
                ? snapshot.UnitOfLine[CurrentIndex]
                : -1;
            if (!IsBrowsing && selectedUnit == activeUnit) selectedUnit = -1;
        }
        var pillDrawn = false;
        var previousTransform = session.Transform;
        var originX = TextOriginX;
        using (session.CreateLayer(_edgeBrush!))
        {
            for (var index = 0; index < snapshot.Count; index++)
            {
                var line = snapshot.Lines[index];
                var top = snapshot.Tops[index] - _scroll.OffsetAt(index);
                // 只保留视口附近的纹理；已经播放过的行不会累积整首歌的 GPU 位图和长音缓存。
                if (top + line.Height < -settings.BaseFontSize || top > _height + settings.BaseFontSize)
                {
                    var margin = settings.BaseFontSize * settings.ViewportMarginLines;
                    if (top + line.Height < -margin || top > _height + margin) line.ReleaseDrawingResources();
                    else line.ReleaseActiveResources();
                    continue;
                }
                if (index != CurrentIndex || IsBrowsing) line.ReleaseActiveResources();
                EnsurePlainImage(line);
                var active = index == CurrentIndex && _position >= snapshot.Document.Lines[index].Start;
                var center = new Vector2(0, (float)(top + line.Height / 2));
                session.Transform = Matrix3x2.CreateTranslation(originX, (float)top)
                    * Matrix3x2.CreateScale((float)_scales[index], center) * previousTransform;
                // 每句只铺一次底，覆盖单元内所有行；即使单元第一行被视口裁掉也照样铺。
                if (!pillDrawn && selectedUnit >= 0 && snapshot.UnitOfLine[index] == selectedUnit)
                {
                    DrawSelectionPill(session, snapshot, index);
                    pillDrawn = true;
                }
                if (active && !IsBrowsing)
                {
                    DrawActiveLine(session, snapshot, index);
                }
                else
                {
                    var distance = Math.Abs(index - _focusIndex);
                    var blurAmount = (float)Math.Min(settings.FarBlurAmount, Math.Max(0, distance - 1) * 1.25);
                    if (blurAmount < 0.01)
                        session.DrawImage(line.PlainImage!, (float)line.GlyphMask!.Bounds.X, (float)line.GlyphMask.Bounds.Y);
                    else
                    {
                        EnsureBlurredImage(line, blurAmount);
                        var bounds = line.GlyphMask!.Bounds;
                        session.DrawImage(line.BlurredImage,
                            new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height));
                    }
                }
            }
            session.Transform = previousTransform;
        }
    }

    /// <summary>
    /// 取走“本帧生成过模糊中间表面”的标记，由绘制循环在结束绘制会话后调用 Trim。
    /// </summary>
    public bool TakeTrimRequest()
    {
        var requested = _blurredCreated;
        _blurredCreated = false;
        return requested;
    }

    /// <summary>
    /// 对齐原点。排版宽度是视口宽度减 28，原点按对齐方式补偿：
    /// 居左 8（左边距 8）、居中 14（文字中心落在视口中线）、居右 20（右边距 8）。
    /// </summary>
    private float BaseTextOriginX => settings.Alignment switch
    {
        LyricsAlignment.Center => 14f,
        LyricsAlignment.Right => 20f,
        _ => 8f,
    };

    /// <summary>实际绘制原点：对齐原点之后再加水平偏移。</summary>
    private float TextOriginX => BaseTextOriginX + (float)HorizontalOffsetPixels;

    private double HorizontalOffsetPixels => _width * settings.HorizontalOffsetPercent / 100.0;

    private double VerticalOffsetPixels => _height * settings.VerticalOffsetPercent / 100.0;

    /// <summary>整句铺底：横向占满歌词列，纵向覆盖整个滚动单元加半个行距。</summary>
    private void DrawSelectionPill(CanvasDrawingSession session, LyricsLayoutSnapshot snapshot, int lineIndex)
    {
        // 底块始终铺满歌词列，不跟随文字的水平偏移；局部 x 抵消对齐原点的平移。
        var unit = snapshot.UnitOfLine[lineIndex];
        var unitTop = snapshot.UnitTops[unit] - snapshot.Tops[lineIndex];
        session.FillRoundedRectangle(
            new Rect(-BaseTextOriginX, unitTop - settings.LineGap / 2, (float)_width,
                snapshot.UnitHeights[unit] + settings.LineGap),
            SelectionPillCornerRadius, SelectionPillCornerRadius, SelectionPillColor);
    }

    private void EnsurePlainImage(LyricsLineLayout line)
    {
        if (line.PlainImage is not null && line.GlyphMask?.Dpi == _dpi) return;
        line.ReleaseDrawingResources();
        line.GlyphMask = new LyricsLineGlyphMask(_creator!, line, _dpi);
        line.PlainImage = line.GlyphMask.Paint(WithOpacity(InactiveTextColor, settings.InactiveLineOpacity));
    }

    /// <summary>
    /// 生成远景模糊的半分辨率位图。
    /// </summary>
    /// <remarks>
    /// 原来每条可见行各挂一个 <see cref="GaussianBlurEffect"/> + <c>CacheOutput</c>，
    /// D2D 会为每行留一张 512×512 或 1024×1024 的缓存纹理（首次打开实测 17 张、17 MB）。
    /// 现在只在模糊级变化时把结果画进一张半分辨率位图，效果对象用完即弃，绘制时零效果。
    /// </remarks>
    private void EnsureBlurredImage(LyricsLineLayout line, float blurAmount)
    {
        if (line.BlurredImage is not null && Math.Abs(line.BlurredAmount - blurAmount) < 0.01) return;
        if (line.PlainImage is not { } plain || line.GlyphMask is not { } mask) return;
        line.BlurredImage?.Dispose();
        var bounds = mask.Bounds;
        const float scale = 0.5f;
        var target = new CanvasRenderTarget(_creator!, (float)bounds.Width, (float)bounds.Height, _dpi * scale);
        using var effect = new GaussianBlurEffect
        {
            Source = plain,
            BlurAmount = blurAmount,
            BorderMode = EffectBorderMode.Soft,
        };
        using (var session = target.CreateDrawingSession())
        {
            session.Clear(Color.FromArgb(0, 0, 0, 0));
            session.DrawImage(effect);
        }
        line.BlurredImage = target;
        line.BlurredAmount = blurAmount;
        _blurredCreated = true;
    }

    private void DrawActiveLine(CanvasDrawingSession session, LyricsLayoutSnapshot snapshot, int index)
    {
        var rendered = snapshot.Lines[index];
        var text = snapshot.Document.Lines[index];
        var wordByWord = snapshot.Document.Kind == LyricKind.WordByWord;
        if (!rendered.ActivePrepared)
        {
            if (wordByWord) _device!.LayoutEngine.PrepareGlyphs(_creator!, text, rendered);
            rendered.ActivePrepared = true;
        }
        if (rendered.ActiveImage is null || rendered.ActiveImage.Dpi != _dpi)
        {
            rendered.ActiveImage?.Dispose();
            rendered.ActiveImage = new LyricsActiveLineImage(_creator!, rendered, settings,
                _played, _unplayed, _dpi, wordByWord);
        }
        rendered.ActiveImage.Draw(session, text, rendered, _position, wordByWord);
    }

    private bool EnsureLayout()
    {
        if (!_layoutDirty || _creator is null || _device is null || _width <= 0) return false;
        var key = new LayoutKey(_document, Math.Max(1, _width - 28), settings.BaseFontSize,
            settings.FontFamily, settings.Alignment, settings.LineGap);
        if (_device.Snapshot is { } previous)
            foreach (var line in previous.Lines) line.ReleaseDrawingResources();
        _device.Snapshot = null;
        for (var node = _layouts.First; node is not null;)
        {
            var next = node.Next;
            if (!ReferenceEquals(node.Value.Key.Document, _document))
            {
                _layouts.Remove(node);
                node.Value.Snapshot.Dispose();
            }
            node = next;
        }
        var cached = _layouts.First;
        while (cached is not null && cached.Value.Key != key) cached = cached.Next;
        if (cached is not null)
        {
            _layouts.Remove(cached);
            _layouts.AddFirst(cached);
            _device.Snapshot = cached.Value.Snapshot;
        }
        else
        {
            var snapshot = _device.LayoutEngine.Build(_creator, _document, key.Width);
            _layouts.AddFirst(new CachedLayout(key, snapshot));
            _device.Snapshot = snapshot;
            // 保留普通窗口、全屏和一个最近尺寸，反复切换无需重新排版整首歌。
            if (_layouts.Count > 3 && _layouts.Last is { } oldest)
            {
                _layouts.RemoveLast();
                oldest.Value.Snapshot.Dispose();
            }
        }
        _scales = new double[_device.Snapshot.Count];
        Array.Fill(_scales, settings.InactiveLineScale);
        _scaleVelocities = new double[_scales.Length];
        _centers = new double[_scales.Length];
        _nextHitRects = new Rect[_scales.Length];
        lock (_hitRectGate) _hitRects = new Rect[_scales.Length];
        _layoutDirty = false;
        _logger.LogInformation("全屏歌词排版：{Count} 行，字号 {FontSize:F1}，视口 {Width:F0}×{Height:F0}",
            _scales.Length, settings.BaseFontSize, _width, _height);
        return true;
    }

    private void EnsureMask()
    {
        if (!_maskDirty || _creator is null) return;
        _edgeBrush?.Dispose();
        _edgeBrush = new CanvasLinearGradientBrush(_creator, new CanvasGradientStop[]
        {
            new() { Position = 0, Color = Color.FromArgb(0, 255, 255, 255) },
            new() { Position = 0.08f, Color = Color.FromArgb(61, 255, 255, 255) },
            new() { Position = 0.12f, Color = Color.FromArgb(255, 255, 255, 255) },
            new() { Position = 0.88f, Color = Color.FromArgb(255, 255, 255, 255) },
            new() { Position = 0.92f, Color = Color.FromArgb(61, 255, 255, 255) },
            new() { Position = 1, Color = Color.FromArgb(0, 255, 255, 255) },
        }) { StartPoint = Vector2.Zero, EndPoint = new Vector2(0, (float)_height) };
        _maskDirty = false;
    }

    public void Dispose()
    {
        _edgeBrush?.Dispose();
        _edgeBrush = null;
        if (_device is not null) _device.Snapshot = null;
        foreach (var cached in _layouts) cached.Snapshot.Dispose();
        _layouts.Clear();
        _device?.Dispose();
        _device = null;
        _creator = null;
        lock (_hitRectGate) _hitRects = [];
        _nextHitRects = [];
        _lastFrame = default;
        _browseTarget = null;
        _pointerY = null;
        _focusIndex = -1;
        CurrentIndex = -1;
    }

    private sealed record LayoutKey(LyricDocument Document, double Width, double FontSize,
        string FontFamily, LyricsAlignment Alignment, double LineGap);
    private sealed record CachedLayout(LayoutKey Key, LyricsLayoutSnapshot Snapshot);

    private static Color WithOpacity(Color color, double opacity)
        => Color.FromArgb((byte)Math.Clamp(color.A * opacity, 0, 255), color.R, color.G, color.B);
}
