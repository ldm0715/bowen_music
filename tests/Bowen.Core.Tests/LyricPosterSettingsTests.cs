using Bowen.Core.Media;
using Bowen.Core.Models;
using Xunit;

namespace Bowen.Core.Tests;

public sealed class LyricPosterSettingsTests
{
    [Fact]
    public void DefaultUsesAutoBackgroundAndAutoText()
    {
        var settings = LyricPosterSettings.Default;

        Assert.Equal(string.Empty, settings.FontFamily);
        Assert.Equal(LyricPosterBackgroundSource.Auto, settings.BackgroundSource);
        Assert.Equal(LyricPosterTextColorMode.Auto, settings.TextColorMode);
        Assert.Equal(0xFFu, settings.BackgroundColorArgb >> 24);
        Assert.Equal(0xFFu, settings.TextColorArgb >> 24);
    }

    [Fact]
    public void NormalizedTrimsFontAndClampsInvalidEnums()
    {
        var settings = (LyricPosterSettings.Default with
        {
            FontFamily = "  宋体  ",
            BackgroundSource = (LyricPosterBackgroundSource)99,
            TextColorMode = (LyricPosterTextColorMode)99,
        }).Normalized();

        Assert.Equal("宋体", settings.FontFamily);
        Assert.Equal(LyricPosterBackgroundSource.Auto, settings.BackgroundSource);
        Assert.Equal(LyricPosterTextColorMode.Auto, settings.TextColorMode);
    }

    [Fact]
    public void NormalizedForcesOpaqueAndFallsBackForZeroColors()
    {
        var settings = (LyricPosterSettings.Default with
        {
            FontFamily = null!,
            BackgroundColorArgb = 0,
            BackgroundCustomColorArgb = 0x00123456,
            TextColorArgb = 0,
            TextCustomColorArgb = 0x00ABCDEF,
        }).Normalized();

        Assert.Equal(string.Empty, settings.FontFamily);
        Assert.Equal(0xFF24324Au, settings.BackgroundColorArgb);
        Assert.Equal(0xFF123456u, settings.BackgroundCustomColorArgb);
        Assert.Equal(0xFFFFFFFFu, settings.TextColorArgb);
        Assert.Equal(0xFFABCDEFu, settings.TextCustomColorArgb);
    }

    [Fact]
    public void BackgroundPaletteKeepsDarkPresetsAndAddsLightOptions()
    {
        foreach (var preset in LyricsSettings.BackgroundPalette)
        {
            Assert.Contains(preset, LyricPosterSettings.BackgroundPalette);
        }

        var hasLight = LyricPosterSettings.BackgroundPalette
            .Select(entry => new RgbColor((byte)(entry.Argb >> 16), (byte)(entry.Argb >> 8), (byte)entry.Argb))
            .Any(color => LyricPosterColorPolicy.IsLight(color));

        Assert.True(hasLight);
    }

    [Fact]
    public void TextColorPaletteExcludesWhite()
    {
        Assert.DoesNotContain(LyricPosterSettings.TextColorPalette, entry => entry.Argb == 0xFFFFFFFF);
        Assert.Equal(LyricsSettings.HighlightPalette.Length - 1, LyricPosterSettings.TextColorPalette.Length);
    }
}
