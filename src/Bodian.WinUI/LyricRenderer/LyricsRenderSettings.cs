namespace Bodian.WinUI.LyricRenderer;

/// <summary>
/// 歌词渲染的全部可调参数。
/// </summary>
/// <remarks>
/// <para>
/// <b>默认值照抄 <c>jayfunc/BetterLyrics</c>（GPL-3.0）</b>，来源是它的
/// <c>LyricsEffectSettings</c> 与 <c>LyricsAnimator</c>。不要「重新设计」这套参数 ——
/// 它的默认值本身就是调好的结果，改了就得重新调。本项目是 GPL-3.0，参数与机制可直接沿用。
/// </para>
/// <para>
/// 每一项都标了来源：<c>[源]</c> 是从上游源码里读出来的值；<c>[选]</c> 是上游没有明确给出、
/// 由本项目取的值。**改动 <c>[选]</c> 的项不需要重新对齐上游，改动 <c>[源]</c> 的项需要。**
/// </para>
/// </remarks>
public sealed record LyricsRenderSettings
{
    public static LyricsRenderSettings Default { get; } = new();

    // ── 排版 ────────────────────────────────────────────────────────────────

    /// <summary>字号（DIP）。<b>这是当前行的字号</b>，非当前行按 <see cref="InactiveLineScale"/> 缩小。</summary>
    /// <remarks>[选] 上游的字号来自用户的样式设置，默认值没提取到。</remarks>
    public double BaseFontSize { get; init; } = 36;

    /// <summary>行高倍数，乘以字号得到行距。</summary>
    /// <remarks>[选] 上游用 <c>LineSpacingMode.Uniform</c> + 样式里的行距。</remarks>
    public double LineHeight { get; init; } = 1.4;

    /// <summary>歌词行之间的额外间距（DIP）。</summary>
    /// <remarks>[选]</remarks>
    public double LineGap { get; init; } = 10;

    /// <summary>字体族。</summary>
    /// <remarks>
    /// [选] <b>本轮单字体族。</b> 上游是 CJK 与西文两个独立字体族（<c>fontFamilyCJK</c> /
    /// <c>fontFamilyWestern</c>）各排一套、再对齐基线。雅黑自带完整的拉丁字形，所以单字体族
    /// <b>正确性上是安全的</b>（不会缺字），只是西文观感不如 Segoe UI。
    /// </remarks>
    public string FontFamily { get; init; } = "Microsoft YaHei UI";

    /// <summary>
    /// 当前行停在视口高度的百分之几处。
    /// </summary>
    /// <remarks>
    /// [选] 上游有这个参数（<c>playingLineTopOffsetFactor</c>）但默认值没提取到。
    /// 它同时决定 <c>distanceFactor</c> 的分母：当前行<b>之上</b>用
    /// <c>视口高 × 本值</c>，<b>之下</b>用 <c>视口高 × (1 − 本值)</c>。
    /// </remarks>
    public double PlayingLineTopOffsetFactor { get; init; } = 0.4;

    // ── 远景效果 ────────────────────────────────────────────────────────────

    /// <summary>最远的行的高斯模糊量。上游是硬编码的 <c>5 * distanceFactor</c>。</summary>
    /// <remarks>[源]</remarks>
    public double FarBlurAmount { get; init; } = 5;

    /// <summary>当前行的缩放。<c>[源]</c> 源为 <c>_highlightedScale</c>。</summary>
    public double CurrentLineScale { get; init; } = 1.0;

    /// <summary>最远的行的缩放。<c>[源]</c> 来源为 <c>_defaultScale</c>。</summary>
    public double InactiveLineScale { get; init; } = 0.75;

    /// <summary>最远的行的不透明度。</summary>
    /// <remarks>[选] 上游这个值来自样式设置（<c>UnplayedOriginalLyricsOpacity</c>），默认值没提取到。</remarks>
    public double InactiveLineOpacity { get; init; } = 0.45;

    /// <summary>上下边缘的羽化带占视口的比例。落在带内的行按距离渐隐。</summary>
    /// <remarks>[选] 上游的 <c>IsLyricsEdgeFeatheringEffectEnabled</c> 默认开，强度没提取到。</remarks>
    public double EdgeFadeRatio { get; init; } = 0.28;

    /// <summary>视口外还预留多少行高去布局 —— 免得滚动时行突然出现。</summary>
    /// <remarks>[选]</remarks>
    public double ViewportMarginLines { get; init; } = 2;

    // ── 逐字扫光（S4） ──────────────────────────────────────────────────────

    /// <summary>
    /// 扫光边缘的羽化宽度，占单个字宽的比例。<b>当前未使用。</b>
    /// </summary>
    /// <remarks>
    /// [源] 上游用「渐变填充 + 文字形状遮罩」实现羽化，渲染路径要经过离屏图与效果链。
    /// 本项目改用更稳的做法：整段先按未唱色画一遍，再用矩形裁剪把已唱那一段覆上已唱色 ——
    /// <b>边界是硬的，没有羽化</b>。留这个字段是为了将来要羽化时不必改设置模型。
    /// </remarks>
    public double SweepFeatherRatio { get; init; } = 0.5;

    // ── 滚动动画（S5） ──────────────────────────────────────────────────────

    /// <summary>滚动时长的基准值（秒）。每行的实际时长在这个基准上加两个距离加权增量。</summary>
    /// <remarks>[选] 上游是容器滚动过渡自己的时长（<c>canvasScrollTransition</c>），取值没提取到。</remarks>
    public double ScrollBaseDuration { get; init; } = 0.3;

    /// <summary>当前行之上的那一段时长（秒）。</summary>
    /// <remarks>[源] <c>LyricsScrollTopDuration = 500</c>。</remarks>
    public double ScrollTopDuration { get; init; } = 0.5;

    /// <summary>当前行之下的那一段时长（秒）。</summary>
    /// <remarks>[源] <c>LyricsScrollBottomDuration = 500</c>。</remarks>
    public double ScrollBottomDuration { get; init; } = 0.5;

    /// <summary>错峰预算占滚动时长的比例。</summary>
    /// <remarks>[源] <c>budget = min(0.4, scrollDuration × 0.75)</c>。</remarks>
    public double ScrollStaggerBudgetRatio { get; init; } = 0.75;

    /// <summary>错峰预算的上限（秒）。</summary>
    /// <remarks>[源] 同上，<c>min(0.4, …)</c>。</remarks>
    public double ScrollStaggerBudgetMax { get; init; } = 0.4;

    /// <summary>
    /// 每行的错峰系数（秒）。
    /// </summary>
    /// <remarks>
    /// [选] <b>上游这个值是 0 —— 也就是默认关闭错峰。</b> 本项目<b>默认开启</b>（观感更好，
    /// 而实际用户不会去改这个开关），取值由本项目定，不是上游的默认值。
    /// </remarks>
    public double ScrollStaggerPerLine { get; init; } = 0.4;

    // ── 长音效果（下一轮，本轮不读） ───────────────────────────────────────

    /// <summary>触发长音效果的最短音节时长。</summary>
    /// <remarks>[源] <c>LyricsGlowEffectLongSyllableDuration = 700</c>（ms）。</remarks>
    public TimeSpan LongSyllableThreshold { get; init; } = TimeSpan.FromMilliseconds(700);

    /// <summary>长音音节放大到的倍率。</summary>
    /// <remarks>[源] 上游自动调节与手动值都是 <c>1.15</c>。</remarks>
    public double LongSyllableScale { get; init; } = 1.15;

    /// <summary>
    /// 长音发光量，按行高的比例算。
    /// </summary>
    /// <remarks>
    /// [源] 上游的自动调节（默认开）是 <c>行高 × 0.2</c>；手动值是绝对值 8。
    /// 这里用自动调节那条 —— 它跟着字号走，换字号不用重调。
    /// </remarks>
    public double LongSyllableGlowRatio { get; init; } = 0.2;

    /// <summary>长音音节的浮动幅度，按行高的比例算。</summary>
    /// <remarks>[源] 上游的自动调节（默认开）是 <c>行高 × 0.1</c>；手动值是绝对值 8。</remarks>
    public double FloatRatio { get; init; } = 0.1;

    // ── 颜色 ────────────────────────────────────────────────────────────────
    //
    // 颜色**刻意不在这里**。它是运行期的主题取色，不是可调常量：
    // 写死浅色会在浅色主题下变成白底白字（字都在，一个也看不见），
    // 写死深色会在深色主题下反过来。所以由 LyricsCanvasView 从主题画刷取色后
    // 通过 LyricsRenderer.SetColors 注入。
}
