using Bowen.Core.Audio;
using Xunit;

namespace Bowen.Core.Tests;

public sealed class AudioEffectCatalogTests
{
    [Fact]
    public void PresetIds_AreUnique()
    {
        var ids = AudioEffectCatalog.AllPresets.Select(preset => preset.Id).ToArray();

        Assert.Equal(ids.Length, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void AmbientIds_AreUnique()
    {
        var ids = AudioEffectCatalog.AmbientSounds.Select(sound => sound.Id).ToArray();

        Assert.Equal(ids.Length, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void EveryPreset_HasANonEmptyFilterChain()
    {
        Assert.All(AudioEffectCatalog.AllPresets, preset =>
        {
            Assert.False(string.IsNullOrWhiteSpace(preset.FilterChain));
            Assert.DoesNotContain("lavfi=", preset.FilterChain);
        });
    }

    [Fact]
    public void Catalog_HasTheMobileTabSizes()
    {
        Assert.Equal(7, AudioEffectCatalog.ViperPresets.Count);
        Assert.Equal(8, AudioEffectCatalog.GalaxyPresets.Count);
        Assert.Equal(12, AudioEffectCatalog.AmbientSounds.Count);
    }

    [Fact]
    public void EqChain_SkipsFlatBandsAndKeepsThePreamp()
    {
        var chain = AudioEffectCatalog.BuildEqChain([0, 3, 0, 0, 0, 0, 0, 0, 0, -2], preampDb: -3);

        Assert.Equal("volume=-3dB,equalizer=f=62:t=q:w=1.2:g=3,equalizer=f=16000:t=q:w=1.2:g=-2", chain);
    }

    [Fact]
    public void EqChain_AllFlat_IsEmpty()
    {
        Assert.Equal("", AudioEffectCatalog.BuildEqChain(new double[10]));
    }
}
