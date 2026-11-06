using Bowen.Core.Models;
using Bowen.Core.Services.Implementations;
using Xunit;

namespace Bowen.Core.Tests;

/// <summary>
/// 统一配置文件存储层。**零真实用户目录** —— 每个测试用自己那份临时目录。
/// </summary>
public sealed class JsonAppSettingsStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "bowen-config-tests", Guid.NewGuid().ToString("N"));

    private string Path_ => Path.Combine(_directory, "config.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private JsonAppSettingsStore NewStore(TimeSpan? saveDelay = null) =>
        new(AppSettings.Default, Path_, saveDelay: saveDelay ?? TimeSpan.FromMilliseconds(20));

    private void WriteConfig(string json)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path_, json);
    }

    // ── 读取 ────────────────────────────────────────────────────────────────

    [Fact]
    public void NoFile_ReturnsDefaults()
    {
        var loaded = JsonAppSettingsStore.Load(Path_);

        Assert.Equal(AppTheme.System, loaded.Appearance.Theme);
        Assert.Equal(VolumeSettings.DefaultLevel, loaded.Volume.Level);
        Assert.Equal(AudioQuality.Lossless, loaded.AudioQuality.Preferred);
    }

    [Fact]
    public void MalformedJson_ReturnsDefaults()
    {
        WriteConfig("{ this is not json");

        var loaded = JsonAppSettingsStore.Load(Path_);

        Assert.Equal(AppTheme.System, loaded.Appearance.Theme);
    }

    [Fact]
    public void MissingSection_UsesInitializers()
    {
        WriteConfig("""{ "appearance": { "theme": "Dark" } }""");

        var loaded = JsonAppSettingsStore.Load(Path_);

        Assert.Equal(AppTheme.Dark, loaded.Appearance.Theme);
        Assert.Equal(LyricsSettings.DefaultFontSize, loaded.Lyrics.FontSize);
        Assert.True(loaded.Playback.RestoreQueue);
    }

    [Fact]
    public void UnknownField_IsIgnored()
    {
        WriteConfig("""{ "future": 1, "appearance": { "theme": "Dark", "future": true } }""");

        Assert.Equal(AppTheme.Dark, JsonAppSettingsStore.Load(Path_).Appearance.Theme);
    }

    [Fact]
    public void ReadsLegacyPascalCase()
    {
        WriteConfig("""{ "Appearance": { "Theme": "Dark" }, "Volume": { "Level": 42 } }""");

        var loaded = JsonAppSettingsStore.Load(Path_);

        Assert.Equal(AppTheme.Dark, loaded.Appearance.Theme);
        Assert.Equal(42d, loaded.Volume.Level);
    }

    [Fact]
    public void BrokenSection_OnlyThatSectionResets()
    {
        WriteConfig("""
            {
              "appearance": { "theme": "Dark" },
              "lyrics": "not-an-object",
              "volume": { "level": 42 }
            }
            """);

        var loaded = JsonAppSettingsStore.Load(Path_);

        Assert.Equal(AppTheme.Dark, loaded.Appearance.Theme);
        Assert.Equal(42d, loaded.Volume.Level);
        Assert.Equal(LyricsSettings.DefaultFontSize, loaded.Lyrics.FontSize);
    }

    [Fact]
    public void NullSection_UsesDefaults()
    {
        WriteConfig("""{ "appearance": null, "lyrics": null }""");

        var loaded = JsonAppSettingsStore.Load(Path_);

        Assert.Equal(AppTheme.System, loaded.Appearance.Theme);
        Assert.Equal(LyricsSettings.DefaultFontSize, loaded.Lyrics.FontSize);
    }

    [Fact]
    public void UnknownEnumString_FallsBackToRegisteredDefault()
    {
        WriteConfig("""
            {
              "appearance": { "theme": "Nope", "startupPage": "Nope" },
              "playback": { "mode": "Nope" },
              "audioQuality": { "preferred": "Nope" }
            }
            """);

        var loaded = JsonAppSettingsStore.Load(Path_);

        Assert.Equal(AppTheme.System, loaded.Appearance.Theme);
        Assert.Equal(StartupSection.Favorites, loaded.Appearance.StartupPage);
        Assert.Equal(PlayMode.Sequential, loaded.Playback.Mode);
        Assert.Equal(AudioQuality.Lossless, loaded.AudioQuality.Preferred);
    }

    [Fact]
    public void NumericEnum_FallsBackToDefault()
    {
        WriteConfig("""{ "appearance": { "theme": 99 } }""");

        Assert.Equal(AppTheme.System, JsonAppSettingsStore.Load(Path_).Appearance.Theme);
    }

    [Fact]
    public void NumericEnum_IsReadForLegacyFiles()
    {
        // 早期的 playback.json 存数字；迁移时要能读出来。
        WriteConfig("""{ "playback": { "mode": 2 }, "audioQuality": { "preferred": 1 } }""");

        var loaded = JsonAppSettingsStore.Load(Path_);

        Assert.Equal(PlayMode.Shuffle, loaded.Playback.Mode);
        Assert.Equal(AudioQuality.High, loaded.AudioQuality.Preferred);
    }

    [Fact]
    public void ShortcutFlags_RoundTrip()
    {
        var initial = AppSettings.Default with
        {
            Shortcuts = new ShortcutSettings
            {
                Bindings =
                [
                    new(
                        ShortcutAction.NextTrack,
                        ShortcutKey.N,
                        ShortcutModifiers.Control | ShortcutModifiers.Shift),
                ],
            },
        };

        using var store = new JsonAppSettingsStore(initial, Path_);
        store.UpdateAndFlush(_ => { });

        var binding = JsonAppSettingsStore.Load(Path_).Shortcuts.For(ShortcutAction.NextTrack);

        Assert.NotNull(binding);
        Assert.Equal(ShortcutKey.N, binding.Key);
        Assert.Equal(ShortcutModifiers.Control | ShortcutModifiers.Shift, binding.Modifiers);
    }

    [Fact]
    public void CorruptPrimary_FallsBackToBackup()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path_ + ".bak", """{ "volume": { "level": 33 } }""");
        File.WriteAllText(Path_, "{ broken");

        Assert.Equal(33d, JsonAppSettingsStore.Load(Path_).Volume.Level);
    }

    [Fact]
    public void CorruptPrimaryAndBackup_ReturnsDefaults()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path_ + ".bak", "{ broken too");
        File.WriteAllText(Path_, "{ broken");

        Assert.Equal(VolumeSettings.DefaultLevel, JsonAppSettingsStore.Load(Path_).Volume.Level);
    }

    // ── 写入 ────────────────────────────────────────────────────────────────

    [Fact]
    public void UpdateAndFlush_WritesImmediately()
    {
        using var store = NewStore();

        Assert.True(store.UpdateAndFlush(settings => settings.Volume.Level = 42));

        Assert.True(File.Exists(Path_));
        Assert.Equal(42d, JsonAppSettingsStore.Load(Path_).Volume.Level);
        Assert.Equal(1, store.WriteCount);
    }

    [Fact]
    public async Task Update_IsDebounced()
    {
        using var store = NewStore(TimeSpan.FromMilliseconds(60));

        for (var value = 1; value <= 10; value++)
        {
            store.Update(settings => settings.Volume.Level = value);
        }

        await WaitUntilAsync(() => store.WriteCount == 1);
        await Task.Delay(120, TestContext.Current.CancellationToken);

        Assert.Equal(1, store.WriteCount);
        Assert.Equal(10d, JsonAppSettingsStore.Load(Path_).Volume.Level);
    }

    [Fact]
    public void Flush_WritesPendingChangeImmediately()
    {
        using var store = NewStore(TimeSpan.FromSeconds(30));
        store.Update(settings => settings.Appearance.Theme = AppTheme.Dark);

        Assert.True(store.Flush());

        Assert.Equal(AppTheme.Dark, JsonAppSettingsStore.Load(Path_).Appearance.Theme);
    }

    [Fact]
    public void SecondWrite_CreatesBackupOfPreviousContent()
    {
        using var store = NewStore();

        store.UpdateAndFlush(settings => settings.Volume.Level = 10);
        store.UpdateAndFlush(settings => settings.Volume.Level = 20);

        Assert.True(File.Exists(Path_ + ".bak"));
        Assert.Equal(10d, JsonAppSettingsStore.Load(Path_ + ".bak").Volume.Level);
        Assert.Equal(20d, JsonAppSettingsStore.Load(Path_).Volume.Level);
    }

    [Fact]
    public void WritesCamelCase()
    {
        using var store = NewStore();
        store.UpdateAndFlush(settings => settings.Volume.Level = 10);

        var json = File.ReadAllText(Path_);

        Assert.Contains("\"schemaVersion\"", json, StringComparison.Ordinal);
        Assert.Contains("\"appearance\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"SchemaVersion\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Save_LeavesNoTemporaryFile()
    {
        using var store = NewStore();
        store.UpdateAndFlush(_ => { });

        Assert.False(File.Exists(Path_ + ".tmp"));
    }

    [Fact]
    public void ConcurrentUpdates_KeepEverySection()
    {
        using var store = NewStore();

        Parallel.Invoke(
            () => store.Update(settings => settings.Volume.Level = 42),
            () => store.Update(settings => settings.Appearance.Theme = AppTheme.Dark),
            () => store.Update(settings => settings.Fonts.FontFamily = "Test Font"));

        Assert.True(store.Flush());

        var loaded = JsonAppSettingsStore.Load(Path_);
        Assert.Equal(42d, loaded.Volume.Level);
        Assert.Equal(AppTheme.Dark, loaded.Appearance.Theme);
        Assert.Equal("Test Font", loaded.Fonts.FontFamily);
    }

    [Fact]
    public void Dispose_FlushesPendingChange()
    {
        var store = NewStore(TimeSpan.FromSeconds(30));
        store.Update(settings => settings.Appearance.Theme = AppTheme.Dark);

        store.Dispose();

        Assert.Equal(AppTheme.Dark, JsonAppSettingsStore.Load(Path_).Appearance.Theme);
    }

    [Fact]
    public void UpdateAndFlush_ReturnsFalse_WhenPathIsDirectory()
    {
        var directoryPath = Path.Combine(_directory, "config-as-directory");
        Directory.CreateDirectory(directoryPath);
        using var store = new JsonAppSettingsStore(AppSettings.Default, directoryPath);

        Assert.False(store.UpdateAndFlush(settings => settings.Volume.Level = 42));
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMilliseconds = 2000)
    {
        var deadline = Environment.TickCount64 + timeoutMilliseconds;
        while (!condition())
        {
            if (Environment.TickCount64 >= deadline)
            {
                throw new TimeoutException("等待条件超时。");
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}
