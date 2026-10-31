namespace Bowen.Core.Media;

/// <summary>RGB / HSV / OKLCH 转换与颜色距离。取色与配色共用，避免两处各写一份。</summary>
internal static class ColorMath
{
    /// <summary>RGB → HSV。<c>H</c> 单位是度（0..360），<c>S</c>/<c>V</c> 是 0..1。</summary>
    public static (double Hue, double Saturation, double Value) ToHsv(RgbColor color)
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
    public static RgbColor FromHsv(double hue, double saturation, double value)
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

    /// <summary>相对亮度，0..1。用于排序与明暗筛选，不要求严格的光度学精度。</summary>
    public static double Luminance(RgbColor color)
        => ((0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B)) / 255.0;

    /// <summary>两个色相的环形距离，0..180 度。</summary>
    public static double HueDistance(double first, double second)
    {
        var distance = Math.Abs(first - second);
        return Math.Min(distance, 360 - distance);
    }

    /// <summary>两个颜色的最大通道差，0..255。用来挡住几乎相同的颜色。</summary>
    public static int RgbDistance(RgbColor first, RgbColor second)
        => Math.Max(
            Math.Abs(first.R - second.R),
            Math.Max(Math.Abs(first.G - second.G), Math.Abs(first.B - second.B)));

    /// <summary>OKLCH 颜色。<c>Lightness</c> 0..1，<c>Chroma</c> 0..约 0.4，<c>Hue</c> 0..360。</summary>
    public readonly record struct Oklch(double Lightness, double Chroma, double Hue);

    /// <summary>
    /// RGB → OKLCH。
    /// </summary>
    /// <remarks>
    /// <b>为什么不用 HSV 做主题映射</b>：HSV 的明度与彩度是绑在一起的，压低 <c>V</c> 会把
    /// 绝对彩度一起压掉，暗色模式下就变成发灰的颜色。OKLCH 的明度与彩度解耦，
    /// 可以在保持色相和彩度的同时单独调整明度。
    /// </remarks>
    public static Oklch ToOklch(RgbColor color)
    {
        var r = ToLinear(color.R / 255.0);
        var g = ToLinear(color.G / 255.0);
        var b = ToLinear(color.B / 255.0);

        var l = Math.Cbrt((0.4122214708 * r) + (0.5363325363 * g) + (0.0514459929 * b));
        var m = Math.Cbrt((0.2119034982 * r) + (0.6806995451 * g) + (0.1073969566 * b));
        var s = Math.Cbrt((0.0883024619 * r) + (0.2817188376 * g) + (0.6299787005 * b));

        var lightness = (0.2104542553 * l) + (0.7936177850 * m) - (0.0040720468 * s);
        var a = (1.9779984951 * l) - (2.4285922050 * m) + (0.4505937099 * s);
        var bb = (0.0259040371 * l) + (0.7827717662 * m) - (0.8086757660 * s);

        var chroma = Math.Sqrt((a * a) + (bb * bb));
        var hue = Math.Atan2(bb, a) * (180 / Math.PI);

        if (hue < 0)
        {
            hue += 360;
        }

        return new Oklch(lightness, chroma, hue);
    }

    /// <summary>
    /// OKLCH → RGB。目标彩度超出 sRGB 色域时降低彩度，保持明度与色相。
    /// </summary>
    public static RgbColor FromOklch(double lightness, double chroma, double hue)
    {
        lightness = Math.Clamp(lightness, 0, 1);
        chroma = Math.Max(0, chroma);

        var (r, g, b) = OklchToLinearRgb(lightness, chroma, hue);

        if (!InGamut(r, g, b))
        {
            var low = 0.0;
            var high = chroma;

            for (var i = 0; i < 24; i++)
            {
                var middle = (low + high) / 2;
                var (middleR, middleG, middleB) = OklchToLinearRgb(lightness, middle, hue);

                if (InGamut(middleR, middleG, middleB))
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }

            (r, g, b) = OklchToLinearRgb(lightness, low, hue);
        }

        return new RgbColor(Encode(r), Encode(g), Encode(b));
    }

    private static (double R, double G, double B) OklchToLinearRgb(double lightness, double chroma, double hue)
    {
        var radians = hue * (Math.PI / 180);
        var a = chroma * Math.Cos(radians);
        var b = chroma * Math.Sin(radians);

        var l = lightness + (0.3963377774 * a) + (0.2158037573 * b);
        var m = lightness - (0.1055613458 * a) - (0.0638541728 * b);
        var s = lightness - (0.0894841775 * a) - (1.2914855480 * b);

        var l3 = l * l * l;
        var m3 = m * m * m;
        var s3 = s * s * s;

        return (
            (4.0767416621 * l3) - (3.3077115913 * m3) + (0.2309699292 * s3),
            (-1.2684380046 * l3) + (2.6097574011 * m3) - (0.3413193965 * s3),
            (-0.0041960863 * l3) - (0.7034186147 * m3) + (1.7076147010 * s3));
    }

    private static bool InGamut(double r, double g, double b)
        => r >= -0.000001 && r <= 1.000001
            && g >= -0.000001 && g <= 1.000001
            && b >= -0.000001 && b <= 1.000001;

    private static double ToLinear(double component)
        => component <= 0.04045
            ? component / 12.92
            : Math.Pow((component + 0.055) / 1.055, 2.4);

    private static byte Encode(double linear)
    {
        var clamped = Math.Clamp(linear, 0, 1);
        var encoded = clamped <= 0.0031308
            ? clamped * 12.92
            : (1.055 * Math.Pow(clamped, 1 / 2.4)) - 0.055;

        return (byte)Math.Round(Math.Clamp(encoded, 0, 1) * 255);
    }
}
