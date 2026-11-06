using Bowen.Core.Models;
using Bowen.Core.Services.Implementations;
using Xunit;

namespace Bowen.Core.Tests;

/// <summary>
/// 旧设置接口到统一存储的适配器。重点验「读写落到同一份配置」与「Load 返回副本」。
/// </summary>
public sealed class SettingsStoreAdaptersTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "bowen-adapter-tests", Guid.NewGuid().ToString("N"));

    private string Path_ => Path.Combine(_directory, "config.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private JsonAppSettingsStore NewStore() =>
        new(AppSettings.Default, Path_, saveDelay: TimeSpan.FromSeconds(30));

    [Fact]
    public void ThemeAdapter_RoundTripsAndLoadReturnsCopy()
    {
        using var store = NewStore();
        var adapter = new UnifiedThemeSettingsStore(store);

        adapter.Save(new ThemeSettings
        {
            Theme = AppTheme.Dark,
            AcrylicEffects = false,
            StartupPage = StartupSection.Recent,
        });

        Assert.Equal(AppTheme.Dark, store.Current.Appearance.Theme);

        var copy = adapter.Load();
        copy.Theme = AppTheme.Light;

        Assert.Equal(AppTheme.Dark, store.Current.Appearance.Theme);
        Assert.Equal(AppTheme.Dark, JsonAppSettingsStore.Load(Path_).Appearance.Theme);
    }

    [Fact]
    public void VolumeAdapter_SaveReturnsResultAndPersists()
    {
        using var store = NewStore();
        var adapter = new UnifiedVolumeSettingsStore(store);

        Assert.True(adapter.Save(new VolumeSettings { Level = 42 }));

        Assert.Equal(42d, adapter.Load().Level);
        Assert.Equal(42d, JsonAppSettingsStore.Load(Path_).Volume.Level);
    }

    [Fact]
    public void ShortcutAdapter_LoadCopiesTheBindingsArray()
    {
        using var store = NewStore();
        var adapter = new UnifiedShortcutSettingsStore(store);
        adapter.Save(new ShortcutSettings
        {
            Bindings =
            [
                new(ShortcutAction.NextTrack, ShortcutKey.N, ShortcutModifiers.Control),
            ],
        });

        var loaded = adapter.Load();
        var index = Array.FindIndex(loaded.Bindings, b => b.Action == ShortcutAction.NextTrack);
        loaded.Bindings[index] = new(ShortcutAction.NextTrack, ShortcutKey.M, ShortcutModifiers.Control);

        Assert.Equal(ShortcutKey.N, adapter.Load().For(ShortcutAction.NextTrack)!.Key);
    }

    [Fact]
    public void WindowAdapters_KeepTheThreePlacementsIndependent()
    {
        using var store = NewStore();

        new UnifiedWindowPlacementStore(store).Save(new WindowPlacement(1, 2, 3, 4));
        new UnifiedDesktopLyricsPlacementStore(store).Save(new WindowPlacement(5, 6, 7, 8));
        new UnifiedMiniPlayerPlacementStore(store).Save(new WindowPlacement(9, 10, 11, 12));

        Assert.Equal(1, new UnifiedWindowPlacementStore(store).Load()!.X);
        Assert.Equal(5, new UnifiedDesktopLyricsPlacementStore(store).Load()!.X);
        Assert.Equal(9, new UnifiedMiniPlayerPlacementStore(store).Load()!.X);
    }

    [Fact]
    public void ProxyAdapter_NormalizesUnknownMode()
    {
        using var store = NewStore();
        var adapter = new UnifiedProxySettingsStore(store);

        Assert.True(adapter.Save(new ProxySettings { Mode = (ProxyMode)99, Address = "x" }));

        Assert.Equal(ProxyMode.Direct, adapter.Load().Mode);
        Assert.Equal("x", adapter.Load().Address);
    }

    [Fact]
    public void FontAdapter_PersistsImmediately()
    {
        using var store = NewStore();
        var adapter = new UnifiedFontSettingsStore(store);

        Assert.True(adapter.Save(new FontSettings { FontFamily = "  Test Font  " }));

        Assert.Equal("Test Font", JsonAppSettingsStore.Load(Path_).Fonts.FontFamily);
    }

    [Fact]
    public void InputMethodAdapter_SaveReturnsResult()
    {
        using var store = NewStore();
        var adapter = new UnifiedInputMethodSettingsStore(store);

        Assert.True(adapter.Save(new InputMethodSettings(true)));
        Assert.True(adapter.Load().CompatibilityEnabled);
    }

    [Fact]
    public void AudioQualityAdapter_RejectsUndefinedValue()
    {
        using var store = NewStore();
        var adapter = new UnifiedAudioQualitySettingsStore(store);

        Assert.Throws<ArgumentOutOfRangeException>(() => adapter.Save((AudioQuality)99));
    }
}
