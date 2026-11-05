namespace Bowen.Core.Media;

/// <summary>
/// 海报背景与文字颜色的对比规则。
/// </summary>
/// <remarks>
/// <b>对外公开</b>：<see cref="ColorMath"/> 是 Core internal（只对测试暴露），
/// WinUI 的渲染器与对话框需要同一套阈值，所以在这里包一层。
/// 阈值沿用歌词页现有的 0.45 约定；只做明暗二选一，不承诺 WCAG。
/// </remarks>
public static class LyricPosterColorPolicy
{
    /// <summary>超过这个亮度视为浅色背景。</summary>
    public const double LuminanceThreshold = 0.45;

    public static readonly RgbColor WhiteText = new(0xFF, 0xFF, 0xFF);

    /// <summary>近黑色。纯黑在大面积海报上偏硬，沿用歌词页的 <c>#1A1A1A</c>。</summary>
    public static readonly RgbColor BlackText = new(0x1A, 0x1A, 0x1A);

    public static double Luminance(RgbColor color) => ColorMath.Luminance(color);

    public static bool IsLight(RgbColor background) => Luminance(background) > LuminanceThreshold;

    /// <summary>自动文字色：浅底取近黑，深底取白。</summary>
    public static RgbColor AutoText(RgbColor background) => IsLight(background) ? BlackText : WhiteText;

    /// <summary>深色背景用浅色 logo，浅色背景用深色 logo。</summary>
    public static bool UseLightLogo(RgbColor background) => !IsLight(background);
}
