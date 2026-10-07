namespace Bodian.Core.Media;

/// <summary>将柔和光晕预绘为一张小尺寸的预乘 BGRA 图片，窗口缩放只缩放已有像素。</summary>
public static class AmbientBackground
{
    private static readonly (double X, double Y, double RadiusX, double RadiusY)[] Blobs =
    [
        (0.12, 0.10, 0.66, 0.86),
        (0.92, 0.32, 0.68, 0.90),
        (0.48, 0.84, 0.72, 0.78),
    ];
    private static readonly double[] Stops = [0, 0.25, 0.55, 0.82, 1];
    private static readonly double[] Alphas = [208.0 / 255, 172.0 / 255, 100.0 / 255, 36.0 / 255, 0];

    public static byte[] Render(IReadOnlyList<RgbColor> palette, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(palette);
        if (palette.Count < Blobs.Length) throw new ArgumentException("Three ambient colors are required.", nameof(palette));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var pixels = new byte[checked(width * height * 4)];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var red = 0.0;
            var green = 0.0;
            var blue = 0.0;
            var alpha = 0.0;
            for (var blob = 0; blob < Blobs.Length; blob++)
            {
                var geometry = Blobs[blob];
                var dx = ((x + 0.5) / width - geometry.X) / geometry.RadiusX;
                var dy = ((y + 0.5) / height - geometry.Y) / geometry.RadiusY;
                var radius = Math.Sqrt(dx * dx + dy * dy);
                if (radius >= 1) continue;
                var stop = 0;
                while (stop < Stops.Length - 2 && radius > Stops[stop + 1]) stop++;
                var fraction = (radius - Stops[stop]) / (Stops[stop + 1] - Stops[stop]);
                var opacity = Alphas[stop] + (Alphas[stop + 1] - Alphas[stop]) * fraction;
                var remaining = 1 - opacity;
                red = palette[blob].R * opacity + red * remaining;
                green = palette[blob].G * opacity + green * remaining;
                blue = palette[blob].B * opacity + blue * remaining;
                alpha = opacity + alpha * remaining;
            }
            var offset = (y * width + x) * 4;
            pixels[offset] = (byte)Math.Clamp(Math.Round(blue), 0, 255);
            pixels[offset + 1] = (byte)Math.Clamp(Math.Round(green), 0, 255);
            pixels[offset + 2] = (byte)Math.Clamp(Math.Round(red), 0, 255);
            pixels[offset + 3] = (byte)Math.Clamp(Math.Round(alpha * 255), 0, 255);
        }
        return pixels;
    }
}
