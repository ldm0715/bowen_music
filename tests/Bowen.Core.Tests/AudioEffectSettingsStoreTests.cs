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
            CustomPresets:
            [
                new CustomEqPreset
                {
                    Id = "custom.a",
                    Name = "我的曲线",
                    Description = "给通勤用",
                    Bands = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10],
                },
            ]);

        Assert.True(store.Save(settings));
        var loaded = store.Load();

        Assert.True(loaded.Enabled);
        Assert.Equal("viper.bass", loaded.PresetId);
        Assert.Equal("ambient.waves", loaded.AmbientId);
        Assert.Equal(42, loaded.AmbientVolume);
        Assert.Single(loaded.CustomPresets);
        Assert.Equal("我的曲线", loaded.CustomPresets[0].Name);
        Assert.Equal("给通勤用", loaded.CustomPresets[0].Description);
        Assert.Equal(settings.CustomPresets[0].Bands, loaded.CustomPresets[0].Bands);
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
    public void CustomPresetBands_AreAlwaysTenAndClamped()
    {
        var path = NewPath();
        var store = new JsonAudioEffectSettingsStore(path);
        store.Save(new AudioEffectSettings(
            CustomPresets:
            [
                new CustomEqPreset { Id = "custom.a", Name = "  低音  ", Bands = [99, -99, 1] },
            ]));

        var preset = Assert.Single(store.Load().CustomPresets);

        Assert.Equal("低音", preset.Name);
        Assert.Equal(10, preset.Bands.Length);
        Assert.Equal(12, preset.Bands[0]);
        Assert.Equal(-12, preset.Bands[1]);
        Assert.Equal(1, preset.Bands[2]);
        Assert.Equal(0, preset.Bands[3]);
    }

    [Fact]
    public void EmptyDescription_FallsBackToTheDefault()
    {
        var path = NewPath();
        var store = new JsonAudioEffectSettingsStore(path);
        store.Save(new AudioEffectSettings(
            CustomPresets:
            [
                new CustomEqPreset { Id = "custom.a", Name = "无说明", Description = "  ", Bands = new double[10] },
            ]));

        Assert.Equal(
            CustomEqPreset.DefaultDescription,
            Assert.Single(store.Load().CustomPresets).Description);
    }

    [Fact]
    public void CustomPresets_DropBadIdsAndDuplicates()
    {
        var path = NewPath();
        var store = new JsonAudioEffectSettingsStore(path);
        store.Save(new AudioEffectSettings(
            CustomPresets:
            [
                new CustomEqPreset { Id = "custom.a", Name = "保留", Bands = [1] },
                new CustomEqPreset { Id = "viper.bass", Name = "前缀不对", Bands = [1] },
                new CustomEqPreset { Id = "custom.a", Name = "重复", Bands = [1] },
                new CustomEqPreset { Id = "", Name = "空 id", Bands = [1] },
            ]));

        var preset = Assert.Single(store.Load().CustomPresets);

        Assert.Equal("custom.a", preset.Id);
        Assert.Equal("保留", preset.Name);
    }

    [Fact]
    public void CustomPresetId_SurvivesTheRoundTrip()
    {
        var path = NewPath();
        var store = new JsonAudioEffectSettingsStore(path);
        store.Save(new AudioEffectSettings(
            Enabled: true,
            PresetId: "custom.a",
            CustomPresets:
            [
                new CustomEqPreset { Id = "custom.a", Name = "我的", Bands = new double[10] },
            ]));

        Assert.Equal("custom.a", store.Load().PresetId);
    }

    [Fact]
    public void UnknownCustomPresetId_IsDropped()
    {
        var path = NewPath();
        var store = new JsonAudioEffectSettingsStore(path);
        store.Save(new AudioEffectSettings(Enabled: true, PresetId: "custom.gone"));

        Assert.Equal("", store.Load().PresetId);
    }

    [Fact]
    public void SaveFailure_IsReported()
    {
        var path = NewPath();
        Directory.CreateDirectory(path);

        Assert.False(new JsonAudioEffectSettingsStore(path).Save(AudioEffectSettings.Default));
    }
}
