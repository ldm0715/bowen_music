using Bowen.Core.Models;
using Bowen.Core.Services.Implementations;
using Xunit;

namespace Bowen.Core.Tests;

public sealed class AudioEffectSettingsStoreTests
{
    private static string NewPath() => Path.Combine(
        Path.GetTempPath(),
        "bowen-audio-effect-tests",
        Guid.NewGuid().ToString("N"),
        "audio-effects.json");

    [Fact]
    public void MissingFile_IsTheDefault_WithoutCreatingAFile()
    {
        var path = NewPath();
        var loaded = new JsonAudioEffectSettingsStore(path).Load();

        Assert.Equal(AudioEffectSettings.Default, loaded);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Settings_RoundTrip()
    {
        var path = NewPath();
        var store = new JsonAudioEffectSettingsStore(path);
        var settings = new AudioEffectSettings(
            Enabled: true,
            PresetId: "viper.bass",
            AmbientId: "ambient.waves",
            AmbientVolume: 42,
            EqBands: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);

        Assert.True(store.Save(settings));
        var loaded = store.Load();

        Assert.True(loaded.Enabled);
        Assert.Equal("viper.bass", loaded.PresetId);
        Assert.Equal("ambient.waves", loaded.AmbientId);
        Assert.Equal(42, loaded.AmbientVolume);
        Assert.Equal(settings.EqBands, loaded.EqBands);
    }

    [Fact]
    public void UnknownIds_AreDroppedOnLoad()
    {
        var path = NewPath();
        var store = new JsonAudioEffectSettingsStore(path);
        store.Save(new AudioEffectSettings(
            Enabled: true,
            PresetId: "viper.gone",
            AmbientId: "ambient.gone"));

        var loaded = store.Load();

        Assert.Equal("", loaded.PresetId);
        Assert.Equal("", loaded.AmbientId);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    public void InvalidContent_FallsBackToTheDefault_AndPreservesFile(string json)
    {
        var path = NewPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);

        Assert.Equal(AudioEffectSettings.Default, new JsonAudioEffectSettingsStore(path).Load());
        Assert.Equal(json, File.ReadAllText(path));
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(150, 100)]
    [InlineData(37.5, 37.5)]
    public void AmbientVolume_IsClamped(double saved, double expected)
    {
        var path = NewPath();
        var store = new JsonAudioEffectSettingsStore(path);
        store.Save(new AudioEffectSettings(AmbientVolume: saved));

        Assert.Equal(expected, store.Load().AmbientVolume);
    }

    [Fact]
    public void EqBands_AreAlwaysTenAndClamped()
    {
        var path = NewPath();
        var store = new JsonAudioEffectSettingsStore(path);
        store.Save(new AudioEffectSettings(EqBands: [99, -99, 1]));

        var bands = store.Load().EqBands;

        Assert.NotNull(bands);
        Assert.Equal(10, bands!.Length);
        Assert.Equal(12, bands[0]);
        Assert.Equal(-12, bands[1]);
        Assert.Equal(1, bands[2]);
        Assert.Equal(0, bands[3]);
    }

    [Fact]
    public void SaveFailure_IsReported()
    {
        var path = NewPath();
        Directory.CreateDirectory(path);

        Assert.False(new JsonAudioEffectSettingsStore(path).Save(AudioEffectSettings.Default));
    }
}
