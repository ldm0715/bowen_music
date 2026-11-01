using Bowen.Core.Audio;
using Bowen.Core.Models;
using Xunit;

namespace Bowen.Core.Tests;

public sealed class AudioEffectGraphBuilderTests
{
    [Fact]
    public void Disabled_ProducesNoFilter()
    {
        Assert.Null(AudioEffectGraphBuilder.Build(
            new AudioEffectSettings(Enabled: false, PresetId: "viper.3d"), null));
    }

    [Fact]
    public void PresetOnly_WrapsTheChainInALabelledLavfi()
    {
        var value = AudioEffectGraphBuilder.Build(
            new AudioEffectSettings(Enabled: true, PresetId: "viper.3d"), null);

        Assert.NotNull(value);
        Assert.StartsWith("@bowenFx:lavfi=[", value);
        Assert.Contains("stereotools=slev=1.6", value);
        Assert.DoesNotContain("amovie", value);
    }

    [Fact]
    public void UnknownPreset_ProducesNoFilter()
    {
        Assert.Null(AudioEffectGraphBuilder.Build(
            new AudioEffectSettings(Enabled: true, PresetId: "viper.missing"), null));
    }

    [Fact]
    public void AmbientOnly_MixesTheLoopWithTheMainSignal()
    {
        var value = AudioEffectGraphBuilder.Build(
            new AudioEffectSettings(Enabled: true, AmbientId: "ambient.waves", AmbientVolume: 30),
            @"C:\Users\a b\ambient.wav");

        Assert.NotNull(value);
        Assert.Contains("anull[main]", value);
        Assert.Contains("amovie=C\\\\:/Users/a b/ambient.wav:loop=0", value);
        Assert.Contains("asetpts=N/SR/TB[amb]", value);
        Assert.Contains("[main][amb]amix=inputs=2:weights=1 0.3:normalize=0", value);
    }

    [Fact]
    public void PresetAndAmbient_ChainThePresetBeforeTheMixer()
    {
        var value = AudioEffectGraphBuilder.Build(
            new AudioEffectSettings(
                Enabled: true,
                PresetId: "viper.bass",
                AmbientId: "ambient.fireplace",
                AmbientVolume: 50),
            @"C:\ambient.wav");

        Assert.NotNull(value);
        Assert.Contains("anull[main];[main]virtualbass=", value);
        Assert.Contains("[fx];amovie=", value);
        Assert.Contains("[fx][amb]amix=inputs=2:weights=1 0.5:normalize=0", value);
    }

    [Theory]
    [InlineData(0, "1 0")]
    [InlineData(30, "1 0.3")]
    [InlineData(100, "1 1")]
    [InlineData(150, "1 1")]
    [InlineData(-20, "1 0")]
    [InlineData(double.NaN, "1 0.3")]
    public void AmbientWeights_IsClamped(double volume, string expected)
    {
        Assert.Equal(expected, AudioEffectGraphBuilder.AmbientWeights(volume));
    }

    [Fact]
    public void FilterPath_EscapesDriveColonAndSeparators()
    {
        Assert.Equal(
            @"C\\:/a b/ambient\[1\].wav",
            AudioEffectGraphBuilder.EscapeFilterPath(@"C:\a b\ambient[1].wav"));
    }
}
