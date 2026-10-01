namespace Bodian.Core.Media;

/// <summary>
/// 从封面主色派生鲜明的氛围色；无色相时使用默认网格配色。
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
    /// <summary>无封面或灰阶封面使用的青绿、亮蓝与紫色网格渐变。</summary>
    public static IReadOnlyList<RgbColor> DefaultAmbient { get; } = Array.AsReadOnly<RgbColor>(
    [
        new(24, 224, 182),
        new(55, 144, 255),
        new(161, 88, 255),
    ]);

    /// <summary>主色饱和度低于这个值就认为「它其实是灰的」，使用默认网格配色。</summary>
    private const double DesaturatedThreshold = 0.15;

    /// <summary>派生的三团色相的偏移量（度）。</summary>
    private static readonly double[] HueOffsets = [-40, 0, 40];

    /// <summary>三团的明度倍率。中间那团最亮，两侧略暗，叠起来才有层次。</summary>
    private static readonly double[] ValueScales = [0.88, 1.0, 0.94];

    /// <summary>
    /// 派生三团氛围色。
    /// </summary>
    /// <param name="seed">主色，来自 <see cref="ColorQuantizer.Dominant"/>。</param>
    /// <returns>彩色封面返回主色的三个同族色；灰阶封面返回默认青绿、亮蓝与紫色。</returns>
    public static IReadOnlyList<RgbColor> Ambient(RgbColor seed)
    {
        var (hue, saturation, value) = ToHsv(seed);

        // 灰阶（或接近灰）的封面没有可用的色相 —— 硬把饱和度拉起来会得到一个凭空的颜色。
        // 使用默认网格配色，不强行给中灰补饱和度，否则背景容易变成灰青色。
        if (saturation < DesaturatedThreshold)
        {
            return DefaultAmbient;
        }

        // 光晕会再叠透明度；原始色保持鲜明，避免暗封面或低饱和封面退化成灰色底。
        saturation = Math.Clamp(saturation, 0.60, 0.88);
        value = Math.Clamp(value, 0.78, 0.94);

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
