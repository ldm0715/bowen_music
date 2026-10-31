using Bowen.Core.Models;

namespace Bowen.WinUI.LyricRenderer;

/// <summary>全屏歌词的原生渲染参数。每个视图持有自己的实例。</summary>
public sealed record LyricsRenderSettings
{
    public static LyricsRenderSettings Default => new();
    public double BaseFontSize { get; set; } = 40;
    public double LineHeight { get; init; } = 1.35;

    /// <summary>
    /// 行与行之间的额外间距。可变：外观面板改行距后由渲染线程更新并标记重排。
    /// </summary>
    public double LineGap { get; set; } = 24;

    /// <summary>
    /// 横向对齐方式。可变：外观面板改对齐后由渲染线程更新并标记重排。
    /// </summary>
    public LyricsAlignment Alignment { get; set; } = LyricsAlignment.Left;

    /// <summary>
    /// 在对齐结果之上的水平偏移，占歌词区域宽度的百分比。
    /// </summary>
    public double HorizontalOffsetPercent { get; set; }

    /// <summary>
    /// 垂直偏移，占歌词区域高度的百分比；正数向下。
    /// </summary>
    public double VerticalOffsetPercent { get; set; }

    /// <summary>
    /// 字体族名。<b>可变</b>：设置页改歌词字体后由渲染线程调
    /// <see cref="LyricsRenderer.SetFontFamily"/> 更新并标记重排。
    /// </summary>
    public string FontFamily { get; set; } = "Segoe UI Variable Display";

    /// <summary>
    /// 译文行相对歌词字号的缩放。译文比原文小一档，避免喧宾夺主。
    /// 可变：外观面板改译文大小后由渲染线程更新并标记重排。
    /// </summary>
    public double TranslationFontScale { get; set; } = LyricsSettings.DefaultTranslationFontScale;

    /// <summary>
    /// 原文与译文之间的间距，DIP。收紧成一组，避免译文被当成下一句。
    /// 可变：外观面板改译文间距后由渲染线程更新并标记重排。
    /// </summary>
    public double TranslationLineGap { get; set; } = LyricsSettings.DefaultTranslationLineGap;

    /// <summary>
    /// 译文是否跟随原文同步扫色。关掉后译文保持未播放行的暗色。
    /// </summary>
    public bool SyncTranslationHighlight { get; set; } = true;
    public double PlayingLineTopOffsetFactor { get; init; } = 0.42;
    public double FarBlurAmount { get; init; } = 5;
    public double CurrentLineScale { get; init; } = 1;
    // 活动/未活动状态只改变亮度与模糊，不通过整行缩放移动文字基线。
    public double InactiveLineScale { get; init; } = 1;
    public double InactiveLineOpacity { get; init; } = 0.38;
    public double ViewportMarginLines { get; init; } = 3;
    public double SweepFeatherRatio { get; init; } = 0.5;
    public TimeSpan LongSyllableThreshold { get; init; } = TimeSpan.FromMilliseconds(1000);
    // 长音字随进度放大并上浮，唱完落回原位；只作用于长音，普通字不动。
    public double LongSyllableScale { get; init; } = 1.08;
    public double LongSyllableGlowRatio { get; init; } = 0.14;
    public double FloatRatio { get; init; } = 0.055;
}
