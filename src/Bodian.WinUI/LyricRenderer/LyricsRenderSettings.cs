namespace Bodian.WinUI.LyricRenderer;

/// <summary>全屏歌词的原生渲染参数。每个视图持有自己的实例。</summary>
public sealed record LyricsRenderSettings
{
    public static LyricsRenderSettings Default => new();
    public double BaseFontSize { get; set; } = 40;
    public double LineHeight { get; init; } = 1.35;
    public double LineGap { get; init; } = 24;
    public string FontFamily { get; init; } = "Segoe UI Variable Display";
    public double PlayingLineTopOffsetFactor { get; init; } = 0.42;
    public double FarBlurAmount { get; init; } = 5;
    public double CurrentLineScale { get; init; } = 1;
    public double InactiveLineScale { get; init; } = 0.92;
    public double InactiveLineOpacity { get; init; } = 0.38;
    public double ViewportMarginLines { get; init; } = 3;
    public double SweepFeatherRatio { get; init; } = 0.5;
    public TimeSpan LongSyllableThreshold { get; init; } = TimeSpan.FromMilliseconds(1000);
    public double LongSyllableScale { get; init; } = 1.08;
    public double LongSyllableGlowRatio { get; init; } = 0.14;
    public double FloatRatio { get; init; } = 0.055;
}
