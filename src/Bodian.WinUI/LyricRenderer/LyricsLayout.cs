using Bodian.Core.Models.Lyrics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Windows.Foundation;

namespace Bodian.WinUI.LyricRenderer;

/// <summary>
/// 一个长音音节：它在行内的位置，加一份<b>独立的小排版</b>。
/// </summary>
/// <remarks>
/// 为什么长音音节要单独排一份：缩放、上浮、发光都是<b>单字级别</b>的变换，
/// 而 Win2D 只有「整行一次画完」与「逐字单独画」两种选择 —— 整行那次调用里没有单字变换的余地。
/// 所以长音音节从主行里排除出去（把那些字设成透明），再用这份小排版逐个画。
/// 文字内容不会变，所以每首歌只需要建一次。
/// </remarks>
internal sealed class LyricsLongSyllable(int syllableIndex, CanvasTextLayout layout, Rect bounds, int charCount) : IDisposable
{
    /// <summary>它在行内的第几个音节。</summary>
    public int SyllableIndex { get; } = syllableIndex;

    /// <summary>这个音节有几个字。给逐字上色用。</summary>
    public int CharCount { get; } = charCount;

    /// <summary>独立的小排版，<c>NoWrap</c> 且左对齐。</summary>
    public CanvasTextLayout Layout { get; } = layout;

    /// <summary>该音节在<b>行局部坐标</b>里的矩形。</summary>
    public Rect Bounds { get; } = bounds;

    public void Dispose() => Layout.Dispose();
}

/// <summary>
/// 一行的排版结果。
/// </summary>
/// <remarks>
/// <b>设备相关</b>（归属某一个 <see cref="CanvasDevice"/>），只能在渲染线程建、用、释放。
/// </remarks>
internal sealed class LyricsLineLayout : IDisposable
{
    public LyricsLineLayout(
        CanvasTextLayout layout,
        Rect[] charBounds,
        int[] charSyllable,
        LyricsLongSyllable[] longSyllables)
    {
        Layout = layout;
        CharBounds = charBounds;
        CharSyllable = charSyllable;
        LongSyllables = longSyllables;

        Width = (float)layout.LayoutBounds.Width;
        Height = (float)layout.LayoutBounds.Height;
    }

    public CanvasTextLayout Layout { get; }

    /// <summary>
    /// 每个 UTF-16 下标的字符矩形（行局部坐标）。
    /// </summary>
    /// <remarks>
    /// 代理对（emoji）占两个下标、组合字符可能给出零宽矩形，所以逐下标的矩形是
    /// 「该下标的全部区域的并集」，可能有零宽项 —— 累加宽度时自然贡献 0。
    /// </remarks>
    public Rect[] CharBounds { get; }

    /// <summary>
    /// 每个 UTF-16 下标的字属于第几个音节。
    /// </summary>
    /// <remarks>
    /// 扫光是按<b>字</b>累加宽度、按<b>音节</b>取进度的（一个字可能只是某个音节的一部分），
    /// 所以需要这层映射。预先摊平成一维数组，绘制时零查找。
    /// </remarks>
    public int[] CharSyllable { get; }

    /// <summary>达到长音阈值的音节，各带一份独立排版。多数行是空的。</summary>
    public LyricsLongSyllable[] LongSyllables { get; }

    public float Width { get; }

    public float Height { get; }

    public void Dispose()
    {
        foreach (var syllable in LongSyllables)
        {
            syllable.Dispose();
        }

        Layout.Dispose();
    }
}

/// <summary>
/// 一整份歌词的排版结果。换歌、换视口宽度、换设备时<b>整体重建</b>，不做增量失效。
/// </summary>
/// <remarks>
/// 缓存粒度按「一首歌」封顶，不跨曲目 —— 一首 63 行的歌词全建也只是一次换歌时几十毫秒的代价，
/// 换来的是每帧零查找。逐行做 LRU 在这里是纯粹的多余复杂度。
/// </remarks>
internal sealed class LyricsLayoutSnapshot : IDisposable
{
    public LyricsLayoutSnapshot(
        LyricDocument document,
        LyricsLineLayout[] lines,
        float[] tops,
        float totalHeight,
        double viewportWidth)
    {
        Document = document;
        Lines = lines;
        Tops = tops;
        TotalHeight = totalHeight;
        ViewportWidth = viewportWidth;
    }

    public LyricDocument Document { get; }

    public LyricsLineLayout[] Lines { get; }

    /// <summary>每一行在<b>文档空间</b>里的 Y（与视口无关，滚动偏移另外减）。</summary>
    public float[] Tops { get; }

    public float TotalHeight { get; }

    public double ViewportWidth { get; }

    public int Count => Lines.Length;

    public void Dispose()
    {
        foreach (var line in Lines)
        {
            line.Dispose();
        }
    }
}

/// <summary>
/// 建排版。只被渲染器使用，跟着设备一起换代。
/// </summary>
/// <remarks>
/// <para>
/// 折行交给 DWrite：<c>WordWrapping.Wrap</c> 对中文按字断行，正是歌词要的。
/// 用 <c>WrapWholeWords</c> 会把整句中文当成一个「词」，长句直接溢出。
/// </para>
/// <para>
/// <b>建的是「行局部坐标」</b>：每行的内容都从 (0,0) 起，落到文档的哪个 Y 由 <see cref="LyricsLayoutSnapshot.Tops"/>
/// 决定，绘制时用变换平移过去。这样同一行内容可以在不同位置复用，也不受滚动影响。
/// </para>
/// </remarks>
internal sealed class LyricsLayoutEngine(LyricsRenderSettings settings) : IDisposable
{
    private CanvasTextFormat? _format;

    /// <summary>
    /// 建整份排版。
    /// </summary>
    /// <param name="resourceCreator">设备相关的资源创建者。</param>
    /// <param name="document">歌词。</param>
    /// <param name="viewportWidth">视口宽度（DIP），决定折行宽度。</param>
    public LyricsLayoutSnapshot Build(
        ICanvasResourceCreator resourceCreator,
        LyricDocument document,
        double viewportWidth)
    {
        ArgumentNullException.ThrowIfNull(resourceCreator);
        ArgumentNullException.ThrowIfNull(document);

        var format = GetFormat();
        var width = (float)Math.Max(1, viewportWidth);
        var lines = new LyricsLineLayout[document.Lines.Count];
        var tops = new float[document.Lines.Count];
        var y = 0f;

        for (var i = 0; i < document.Lines.Count; i++)
        {
            var line = document.Lines[i];
            var item = BuildLine(resourceCreator, format, line, width);

            lines[i] = item;
            tops[i] = y;
            y += item.Height + (float)settings.LineGap;
        }

        var total = document.Lines.Count > 0 ? y - (float)settings.LineGap : 0f;

        return new LyricsLayoutSnapshot(document, lines, tops, total, viewportWidth);
    }

    public void Dispose()
    {
        _format?.Dispose();
        _format = null;
    }

    private LyricsLineLayout BuildLine(
        ICanvasResourceCreator resourceCreator,
        CanvasTextFormat format,
        LyricLine line,
        float width)
    {
        // 高度给一个有限上限而不是 0：0 在 Win2D 里是「无约束」，无约束 + 折行
        // 在不同版本上行为不一致。按「最多 20 个视觉行」封顶，足够且确定。
        var maxHeight = (float)(settings.BaseFontSize * settings.LineHeight * 20);

        // 空行（理论上游解析器不会产出，但手工构造的文档可能）也要占一行高度，
        // 否则后面的行会挤在一起。
        var text = string.IsNullOrEmpty(line.Text) ? " " : line.Text;
        var layout = new CanvasTextLayout(resourceCreator, text, format, width, maxHeight);
        var span = text.Length;

        var charBounds = new Rect[span];
        for (var i = 0; i < span; i++)
        {
            charBounds[i] = UnionOf(layout.GetCharacterRegions(i, 1));
        }

        var charSyllable = BuildSyllableMap(line, span);
        var longSyllables = BuildLongSyllables(resourceCreator, line, charSyllable, charBounds);

        return new LyricsLineLayout(layout, charBounds, charSyllable, longSyllables);
    }

    /// <summary>
    /// 给达到长音阈值的音节各建一份独立排版。
    /// </summary>
    /// <remarks>
    /// 长音的门槛只看<b>时长</b>，不看版式 —— 逐行版整行就是一个音节，时长通常远超阈值，
    /// 会把每一行都当成「长音」而整行放大。<b>所以调用方还必须用 <c>LyricKind</c> 把关</b>，
    /// 见 <c>LyricsRenderer</c> 里的判断。这里不做那个判断，是因为版式属于文档而不是行。
    /// </remarks>
    private LyricsLongSyllable[] BuildLongSyllables(
        ICanvasResourceCreator resourceCreator,
        LyricLine line,
        int[] charSyllable,
        Rect[] charBounds)
    {
        var threshold = settings.LongSyllableThreshold;

        if (threshold <= TimeSpan.Zero || line.Syllables.Count == 0)
        {
            return [];
        }

        var found = new List<LyricsLongSyllable>();

        for (var s = 0; s < line.Syllables.Count; s++)
        {
            var syllable = line.Syllables[s];

            if (syllable.Duration < threshold || syllable.Text.Length == 0)
            {
                continue;
            }

            var bounds = SyllableBounds(charSyllable, charBounds, s);

            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                continue;
            }

            // NoWrap + 左对齐：这一份只画一个音节，不需要折行，也不要居中。
            var subFormat = new CanvasTextFormat
            {
                FontFamily = settings.FontFamily,
                FontSize = (float)settings.BaseFontSize,
                FontWeight = Microsoft.UI.Text.FontWeights.Normal,
                WordWrapping = CanvasWordWrapping.NoWrap,
                HorizontalAlignment = CanvasHorizontalAlignment.Left,
                VerticalAlignment = CanvasVerticalAlignment.Top,
            };

            var subLayout = new CanvasTextLayout(resourceCreator, syllable.Text, subFormat, 0, 0);
            subFormat.Dispose();

            found.Add(new LyricsLongSyllable(s, subLayout, bounds, syllable.Text.Length));
        }

        return [.. found];
    }

    /// <summary>某个音节的字矩形并集。</summary>
    private static Rect SyllableBounds(int[] charSyllable, Rect[] charBounds, int syllableIndex)
    {
        var result = default(Rect);
        var started = false;

        for (var i = 0; i < charSyllable.Length; i++)
        {
            if (charSyllable[i] != syllableIndex)
            {
                continue;
            }

            if (!started)
            {
                result = charBounds[i];
                started = true;
            }
            else
            {
                result.Union(charBounds[i]);
            }
        }

        return result;
    }

    /// <summary>
    /// 字下标 → 音节下标。解析器保证把所有音节的文本拼起来就是整行文本。
    /// </summary>
    /// <remarks>
    /// 音节总长与整行文本对不上时（不该发生，但手工构造的文档可能），把多出来的尾部挂到
    /// 最后一个音节上 —— 宁可进度略有偏差，也不要下标越界。
    /// </remarks>
    private static int[] BuildSyllableMap(LyricLine line, int span)
    {
        var map = new int[span];
        var cursor = 0;

        for (var i = 0; i < line.Syllables.Count; i++)
        {
            var count = line.Syllables[i].Text.Length;

            for (var c = cursor; c < cursor + count && c < span; c++)
            {
                map[c] = i;
            }

            cursor += count;
        }

        var last = line.Syllables.Count - 1;

        for (var c = Math.Clamp(cursor, 0, span); c < span; c++)
        {
            map[c] = last;
        }

        return map;
    }

    /// <summary>把若干区域合成一个外接矩形。空数组返回空矩形。</summary>
    private static Rect UnionOf(CanvasTextLayoutRegion[] parts)
    {
        if (parts.Length == 0)
        {
            return default;
        }

        var bounds = parts[0].LayoutBounds;

        for (var i = 1; i < parts.Length; i++)
        {
            bounds.Union(parts[i].LayoutBounds);
        }

        return bounds;
    }

    private CanvasTextFormat GetFormat() => _format ??= new CanvasTextFormat
    {
        FontFamily = settings.FontFamily,
        FontSize = (float)settings.BaseFontSize,
        FontWeight = Microsoft.UI.Text.FontWeights.Normal,

        // 中文按字断行；WrapWholeWords 会把整句中文当成一个词，长句溢出。
        WordWrapping = CanvasWordWrapping.Wrap,

        // 居中对齐：配合「requestedWidth 给满视口宽」的排版，画的时候从 x=0 开始就是居中的，
        // 下游不必再算每行的横向偏移。
        HorizontalAlignment = CanvasHorizontalAlignment.Center,
        VerticalAlignment = CanvasVerticalAlignment.Top,

        LineSpacingMode = CanvasLineSpacingMode.Uniform,
        LineSpacing = (float)(settings.BaseFontSize * settings.LineHeight),
    };
}
