namespace Bodian.Core.Media;

/// <summary>
/// 从一个主色派生出一组**和谐**的氛围色。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么是「派生」而不是「从封面里挑三个色」</b>：挑出来的三个色可能彼此毫不相干
/// （封面上一块青、一块橙、一块灰），三团半透明色叠在一起就会往灰走 ——
/// 表现就是背景「脏」。按色相派生则保证三个色同族，重叠区不会浑。
/// </para>
/// <para>
/// 色相偏移取 ±40°：够远，三团能看出区别；又够近，仍是一家人。
/// 再加一点明度差，避免三团完全一样、叠起来变成一片死平。
/// </para>
/// </remarks>
public static class ColorPalette
{
    /// <summary>品牌薄荷绿 <c>#00F3B0</c> 的色相。主色太灰时退回它。</summary>
    private const double BrandHue = 163.5;

    /// <summary>主色饱和度低于这个值就认为「它其实是灰的」，改用品牌色相。</summary>
    private const double DesaturatedThreshold = 0.15;

    /// <summary>派生的三团色相的偏移量（度）。</summary>
    private static readonly double[] HueOffsets = [-40, 0, 40];

    /// <summary>三团的明度倍率。中间那团最亮，两侧略暗，叠起来才有层次。</summary>
    private static readonly double[] ValueScales = [0.88, 1.0, 0.94];

    /// <summary>
    /// 派生三团氛围色。
    /// </summary>
    /// <param name="seed">主色，来自 <see cref="ColorQuantizer.Dominant"/>。</param>
    /// <returns>顺序固定为「偏冷 → 原色 → 偏暖」，调用方按位取用。</returns>
    public static IReadOnlyList<RgbColor> Ambient(RgbColor seed)
    {
        var (hue, saturation, value) = ToHsv(seed);

        // 灰阶（或接近灰）的封面没有可用的色相 —— 硬把饱和度拉起来会得到一个凭空的颜色。
        // 退回品牌色相，至少观感是「有意的」而不是「随机冒出一个红」。
        if (saturation < DesaturatedThreshold)
        {
            hue = BrandHue;
        }

        // 派生的颜色是要当背景光晕用的，太淡看不见、太浓会压过内容。
        saturation = Math.Clamp(Math.Max(saturation, 0.34), 0.34, 0.82);

        var result = new RgbColor[HueOffsets.Length];

        for (var i = 0; i < HueOffsets.Length; i++)
        {
            var h = (hue + HueOffsets[i] + 360) % 360;
            var v = Math.Clamp(value * ValueScales[i], 0.28, 0.94);

            result[i] = FromHsv(h, saturation, v);
        }

        return result;
    }

    /// <summary>RGB → HSV。<c>H</c> 单位是度（0..360），<c>S</c>/<c>V</c> 是 0..1。</summary>
    private static (double Hue, double Saturation, double Value) ToHsv(RgbColor color)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;

        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        var hue = 0.0;

        if (delta > 0)
        {
            if (max == r)
            {
                hue = 60 * (((g - b) / delta) % 6);
            }
            else if (max == g)
            {
                hue = 60 * (((b - r) / delta) + 2);
            }
            else
            {
                hue = 60 * (((r - g) / delta) + 4);
            }
        }

        if (hue < 0)
        {
            hue += 360;
        }

        var saturation = max <= 0 ? 0.0 : delta / max;

        return (hue, saturation, max);
    }

    /// <summary>HSV → RGB。</summary>
    private static RgbColor FromHsv(double hue, double saturation, double value)
    {
        var c = value * saturation;
        var x = c * (1 - Math.Abs(((hue / 60) % 2) - 1));
        var m = value - c;

        var (r, g, b) = (int)(hue / 60) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };

        return new RgbColor(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }
}
