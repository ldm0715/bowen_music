using Bowen.Core.Media;
using Xunit;

namespace Bowen.Core.Tests;

public sealed class LyricPosterColorPolicyTests
{
    [Fact]
    public void WhiteBackgroundUsesBlackTextAndDarkLogo()
    {
        var background = new RgbColor(0xFF, 0xFF, 0xFF);

        Assert.True(LyricPosterColorPolicy.IsLight(background));
        Assert.Equal(LyricPosterColorPolicy.BlackText, LyricPosterColorPolicy.AutoText(background));
        Assert.False(LyricPosterColorPolicy.UseLightLogo(background));
    }

    [Fact]
    public void DarkBackgroundUsesWhiteTextAndLightLogo()
    {
        var background = new RgbColor(24, 32, 48);

        Assert.False(LyricPosterColorPolicy.IsLight(background));
        Assert.Equal(LyricPosterColorPolicy.WhiteText, LyricPosterColorPolicy.AutoText(background));
        Assert.True(LyricPosterColorPolicy.UseLightLogo(background));
    }

    [Theory]
    [InlineData(114, false)]
    [InlineData(116, true)]
    public void LuminanceThresholdSplitsMidTones(byte channel, bool expectedLight)
    {
        var background = new RgbColor(channel, channel, channel);

        Assert.Equal(expectedLight, LyricPosterColorPolicy.IsLight(background));
        Assert.Equal(expectedLight ? LyricPosterColorPolicy.BlackText : LyricPosterColorPolicy.WhiteText,
            LyricPosterColorPolicy.AutoText(background));
    }
}
