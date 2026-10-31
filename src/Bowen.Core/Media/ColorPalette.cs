namespace Bowen.Core.Media;

/// <summary>
/// 从封面提取的颜色派生氛围色；没有封面时使用默认网格配色。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么不再由一个主色派生三个色相</b>：色相 ±40° 得到的三团仍然只是一家人，
/// 整页看起来像一套固定渐变。现在由 <see cref="ColorQuantizer.Palette"/> 提供 1–3 个
/// 封面里真实存在的颜色，这里只负责把它们调整到适合发光、且适合当前主题的区间。
/// </para>
/// <para>
/// <b>为什么按主题分别映射</b>：同一个颜色在浅色底上是柔和的粉彩，在深色底上却会发灰。
/// 明度与彩度必须按主题分别取值，否则要么深色发浑，要么浅色刺眼。
/// 映射在 OKLCH 空间里做：色相与彩度解耦，压低明度不会顺带把颜色抽干。
/// </para>
/// <para>
/// <b>灰阶封面不再换成默认配色。</b> 用封面自身的灰调生成低饱和光晕，
/// 青绿、亮蓝、紫只留给没有封面或解码失败的情况。
/// </para>
/// </remarks>
public static class ColorPalette
{
    /// <summary>氛围背景需要的光晕数量。</summary>
    public const int ColorCount = 3;

    /// <summary>无封面、解码失败或封面全透明时使用的青绿、亮蓝与紫色网格渐变。</summary>
    public static IReadOnlyList<RgbColor> DefaultAmbient { get; } = Array.AsReadOnly<RgbColor>(
    [
        new(24, 224, 182),
        new(55, 144, 255),
        new(161, 88, 255),
    ]);

    /// <summary>OKLCH 彩度低于这个值就按中性色处理，不再放大它的色相。</summary>
    /// <remarks>
    /// `#888886` 的彩度约 0.002，暖灰约 0.020，浅粉 `#F7DCE3` 约 0.031。
    /// 门槛取 0.025：暖灰保持中性，浅粉仍按彩色映射，避免白底封面上的彩色条被抹掉。
    /// </remarks>
    private const double NeutralChromaThreshold = 0.025;

    /// <summary>中性色光晕的彩度上限；保留一点点色温，但不至于变成彩色。</summary>
    private const double NeutralChromaCap = 0.015;

    /// <summary>
    /// 三团的明度偏移。中间那团略亮，两侧略暗，叠起来才有层次。
    /// </summary>
    private static readonly double[] ValueOffsets = [-0.03, 0.02, -0.01];

    // 深色主题：把明度抬到仍能读出色相的位置，同时保住彩度。
    private const double DarkLightnessBase = 0.60;
    private const double DarkLightnessRange = 0.14;
    private const double DarkChromaScale = 1.15;
    private const double DarkChromaOffset = 0.02;
    private const double DarkMinimumChroma = 0.070;
    private const double DarkMaximumChroma = 0.150;

    // 浅色主题：高明度、低彩度，作为浅底上的柔和色晕。
    private const double LightLightnessBase = 0.78;
    private const double LightLightnessRange = 0.12;
    private const double LightChromaScale = 0.85;
    private const double LightMinimumChroma = 0.040;
    private const double LightMaximumChroma = 0.100;

    /// <summary>
    /// 派生三团氛围色（单色入口）。
    /// </summary>
    /// <param name="seed">封面主色，来自 <see cref="ColorQuantizer.Dominant"/>。</param>
    /// <param name="theme">当前主题，决定目标明度与彩度。</param>
    public static IReadOnlyList<RgbColor> Ambient(RgbColor seed, AmbientTheme theme)
        => Ambient([seed], theme);

    /// <summary>
    /// 由 1–3 个封面颜色派生三团氛围色。
    /// </summary>
    /// <param name="seeds">
    /// 封面里真实存在的颜色，来自 <see cref="ColorQuantizer.Palette"/>。
    /// 少于三色时重复使用最后一个颜色，由 <see cref="ValueOffsets"/> 拉开明暗。
    /// </param>
    /// <param name="theme">当前主题，决定目标明度与彩度。</param>
    /// <returns>三团色彩；<paramref name="seeds"/> 为空时返回默认网格配色。</returns>
    public static IReadOnlyList<RgbColor> Ambient(IReadOnlyList<RgbColor> seeds, AmbientTheme theme)
    {
        ArgumentNullException.ThrowIfNull(seeds);

        if (seeds.Count == 0)
        {
            return DefaultAmbient;
        }

        var colors = new RgbColor[ColorCount];

        for (var i = 0; i < colors.Length; i++)
        {
            colors[i] = AmbientFrom(seeds[Math.Min(i, seeds.Count - 1)], i, theme);
        }

        return colors;
    }

    private static RgbColor AmbientFrom(RgbColor seed, int index, AmbientTheme theme)
    {
        var (lightness, chroma, hue) = ColorMath.ToOklch(seed);

        var targetLightness = theme == AmbientTheme.Dark
            ? DarkLightnessBase + (DarkLightnessRange * lightness)
            : LightLightnessBase + (LightLightnessRange * lightness);

        targetLightness += ValueOffsets[index];
        targetLightness = Math.Clamp(targetLightness, 0.30, 0.94);

        var targetChroma = chroma < NeutralChromaThreshold
            ? Math.Min(chroma, NeutralChromaCap)
            : theme == AmbientTheme.Dark
                ? Math.Clamp((chroma * DarkChromaScale) + DarkChromaOffset, DarkMinimumChroma, DarkMaximumChroma)
                : Math.Clamp(chroma * LightChromaScale, LightMinimumChroma, LightMaximumChroma);

        return ColorMath.FromOklch(targetLightness, targetChroma, hue);
    }

    /// <summary>
    /// 由封面种子派生单个纯色表面，按主题取不同的明度与彩度。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>纯色背景铺满整窗，必须和当前主题的文字、卡片对比。</b> 深色主题用低明度色，
    /// 浅色主题用高明度色；色相来自封面第一个种子。取第一个种子是因为它已经是
    /// <see cref="ColorQuantizer.Palette"/> 按得分排在最前的颜色。
    /// </para>
    /// <para>
    /// <b>歌词页也走这里，但固定传 <see cref="AmbientTheme.Dark"/>。</b>
    /// 歌词页永远是白字深底，不需要浅色变体；外壳则有深色与浅色两种主题。
    /// </para>
    /// </remarks>
    public static RgbColor SolidBackground(IReadOnlyList<RgbColor> seeds, AmbientTheme theme)
    {
        ArgumentNullException.ThrowIfNull(seeds);

        if (seeds.Count == 0)
        {
            return theme == AmbientTheme.Dark
                ? new RgbColor(24, 32, 48)
                : new RgbColor(244, 244, 244);
        }

        var (lightness, chroma, hue) = ColorMath.ToOklch(seeds[0]);
        var neutral = chroma < NeutralChromaThreshold;

        if (theme == AmbientTheme.Dark)
        {
            // 深色底：约 #121212–#1C1C1C 的明度，留一点点封面色相。
            var targetLightness = Math.Clamp(0.20 + (0.10 * lightness), 0.20, 0.30);
            var targetChroma = neutral
                ? Math.Min(chroma, 0.008)
                : Math.Clamp((chroma * 0.55) + 0.005, 0.012, 0.070);

            return ColorMath.FromOklch(targetLightness, targetChroma, hue);
        }

        // 浅色底：比卡片底色略深一点，保证卡片边界仍可见。
        var lightLightness = Math.Clamp(0.94 - (0.04 * lightness), 0.90, 0.96);
        var lightChroma = neutral
            ? Math.Min(chroma, 0.006)
            : Math.Clamp(chroma * 0.30, 0.006, 0.035);

        return ColorMath.FromOklch(lightLightness, lightChroma, hue);
    }

    /// <summary>
    /// 在两个调色板之间线性插值，供切换封面时的颜色过渡使用。
    /// </summary>
    /// <param name="from">起始调色板。</param>
    /// <param name="to">目标调色板，长度必须与 <paramref name="from"/> 相同。</param>
    /// <param name="amount">0 返回起点，1 返回终点，中间值按通道线性混合。</param>
    public static IReadOnlyList<RgbColor> Blend(
        IReadOnlyList<RgbColor> from,
        IReadOnlyList<RgbColor> to,
        double amount)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        if (from.Count != to.Count)
        {
            throw new ArgumentException("Palettes must have the same length.", nameof(to));
        }

        var t = Math.Clamp(amount, 0, 1);
        var result = new RgbColor[from.Count];

        for (var i = 0; i < result.Length; i++)
        {
            result[i] = new RgbColor(
                Mix(from[i].R, to[i].R, t),
                Mix(from[i].G, to[i].G, t),
                Mix(from[i].B, to[i].B, t));
        }

        return result;
    }

    private static byte Mix(byte from, byte to, double amount)
        => (byte)Math.Round(from + ((to - from) * amount));
}
