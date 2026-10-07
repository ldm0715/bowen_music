using Bodian.Core.Media;
using Xunit;

namespace Bodian.Core.Tests;

public sealed class AmbientBackgroundTests
{
    [Fact]
    public void OutputIsPremultipliedBgraWithFiniteAlpha()
    {
        var pixels = AmbientBackground.Render([new(255, 0, 0), new(0, 255, 0), new(0, 0, 255)], 64, 48);
        Assert.Equal(64 * 48 * 4, pixels.Length);
        for (var pixel = 0; pixel < pixels.Length; pixel += 4)
        {
            Assert.InRange(pixels[pixel], 0, pixels[pixel + 3]);
            Assert.InRange(pixels[pixel + 1], 0, pixels[pixel + 3]);
            Assert.InRange(pixels[pixel + 2], 0, pixels[pixel + 3]);
        }
    }

    [Fact]
    public void BlackPaletteKeepsAlphaWithoutIntroducingColor()
    {
        var pixels = AmbientBackground.Render([new(0, 0, 0), new(0, 0, 0), new(0, 0, 0)], 16, 16);
        for (var pixel = 0; pixel < pixels.Length; pixel += 4)
        {
            Assert.Equal(0, pixels[pixel]);
            Assert.Equal(0, pixels[pixel + 1]);
            Assert.Equal(0, pixels[pixel + 2]);
        }
        Assert.Contains(pixels.Where((_, index) => index % 4 == 3), alpha => alpha > 0);
    }

    [Fact]
    public void RepeatedRenderingHasStableColors()
        => Assert.Equal(AmbientBackground.Render(ColorPalette.DefaultAmbient, 32, 32),
            AmbientBackground.Render(ColorPalette.DefaultAmbient, 32, 32));
}
