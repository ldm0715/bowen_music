using Bowen.Core.Models;
using Bowen.Core.Services;
using Bowen.Core.Services.Implementations;
using Xunit;

namespace Bowen.Core.Tests;

/// <summary>
/// 启动加载与旧设置迁移。每个测试用独立临时数据目录。
/// </summary>
public sealed class SettingsBootstrapTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "bowen-bootstrap-tests", Guid.NewGuid().ToString("N"));

    private string ConfigPath => Path.Combine(_root, "config.json");
    private string ArchiveRoot => Path.Combine(_root, "archive");
    private string ArchiveDirectory => Path.Combine(ArchiveRoot, "legacy-settings");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string LegacyPath(string fileName) => Path.Combine(_root, fileName);

    private void SeedLegacyTheme(AppTheme theme = AppTheme.Dark) =>
        new JsonThemeSettingsStore(LegacyPath("settings.json"))
            .Save(new ThemeSettings(theme, AcrylicEffects: false, StartupPage: StartupSection.Recent));

    private void SeedLegacyVolume(double level) =>
        new JsonVolumeSettingsStore(LegacyPath("volume.json")).Save(new VolumeSettings(level));

    // ── 空目录 ──────────────────────────────────────────────────────────────

    [Fact]
    public void EmptyDirectory_WritesDefaultsAndDoesNotCreateArchive()
    {
        var result = SettingsBootstrap.Load(_root);

        Assert.True(result.Migrated);
        Assert.Equal(AppTheme.System, result.Settings.Appearance.Theme);
        Assert.True(File.Exists(ConfigPath));
        Assert.False(Directory.Exists(ArchiveDirectory));
    }

    // ── 迁移 ────────────────────────────────────────────────────────────────

    [Fact]
    public void LegacyFiles_AreMigratedAndArchived()
    {
        SeedLegacyTheme();
        SeedLegacyVolume(37);
        new JsonLyricsSettingsStore(LegacyPath("lyrics.json"))
            .Save(new LyricsSettings { FontSize = 60 });
        new JsonWindowPlacementStore(LegacyPath("window.json"))
            .Save(new WindowPlacement(10, 20, 800, 600));

        var result = SettingsBootstrap.Load(_root);

        Assert.True(result.Migrated);
        Assert.Equal(AppTheme.Dark, result.Settings.Appearance.Theme);
        Assert.Equal(37d, result.Settings.Volume.Level);
        Assert.Equal(60d, result.Settings.Lyrics.FontSize);
        Assert.Equal(800, result.Settings.Windows.Main?.Width);

        Assert.True(File.Exists(ConfigPath));
        Assert.False(File.Exists(LegacyPath("settings.json")));
        Assert.False(File.Exists(LegacyPath("volume.json")));

        Assert.True(File.Exists(Path.Combine(ArchiveDirectory, "settings.json")));
        Assert.True(File.Exists(Path.Combine(ArchiveDirectory, "volume.json")));
        Assert.True(File.Exists(Path.Combine(ArchiveDirectory, "README.txt")));
        Assert.Contains("config.json", File.ReadAllText(Path.Combine(ArchiveDirectory, "README.txt")));

        var reloaded = JsonAppSettingsStore.Load(ConfigPath);
        Assert.Equal(AppTheme.Dark, reloaded.Appearance.Theme);
        Assert.Equal(37d, reloaded.Volume.Level);
    }

    [Fact]
    public void MissingLegacyFiles_UseDefaults()
    {
        SeedLegacyTheme();

        var result = SettingsBootstrap.Load(_root);

        Assert.Equal(AppTheme.Dark, result.Settings.Appearance.Theme);
        Assert.Equal(VolumeSettings.DefaultLevel, result.Settings.Volume.Level);
        Assert.True(result.Settings.Playback.RestoreQueue);
    }

    [Fact]
    public void LegacyPlaybackBareEnum_IsMigrated()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(LegacyPath("playback.json"), "2");

        var result = SettingsBootstrap.Load(_root);

        Assert.Equal(PlayMode.Shuffle, result.Settings.Playback.Mode);
        Assert.True(result.Settings.Playback.RestoreQueue);
    }

    [Fact]
    public void ExistingConfig_IsNotReplacedByLegacyFiles()
    {
        SeedLegacyTheme(AppTheme.Dark);
        Assert.True(SettingsBootstrap.Load(_root).Migrated);

        // 模拟旧版本在迁移之后又写了一份旧文件。
        SeedLegacyTheme(AppTheme.Light);

        var result = SettingsBootstrap.Load(_root);

        Assert.False(result.Migrated);
        Assert.Equal(AppTheme.Dark, result.Settings.Appearance.Theme);
        Assert.False(File.Exists(LegacyPath("settings.json")));
    }

    [Fact]
    public void ArchiveCollision_UsesTimestampedDirectory()
    {
        Directory.CreateDirectory(ArchiveDirectory);
        File.WriteAllText(Path.Combine(ArchiveDirectory, "settings.json"), "old archive");
        SeedLegacyVolume(37);

        SettingsBootstrap.Load(_root);

        Assert.True(File.Exists(Path.Combine(ArchiveDirectory, "settings.json")));
        Assert.Equal("old archive", File.ReadAllText(Path.Combine(ArchiveDirectory, "settings.json")));

        var timestamped = Directory.GetDirectories(ArchiveRoot, "legacy-settings-*");
        Assert.Single(timestamped);
        Assert.True(File.Exists(Path.Combine(timestamped[0], "volume.json")));
    }

    [Fact]
    public void Archive_DoesNotTouchNonSettingsFiles()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(LegacyPath("devid.txt"), "device");
        File.WriteAllText(LegacyPath("session.dat"), "credentials");
        Directory.CreateDirectory(Path.Combine(_root, "accounts", "anonymous"));
        File.WriteAllText(Path.Combine(_root, "accounts", "anonymous", "queue.json"), "[]");
        SeedLegacyTheme();

        SettingsBootstrap.Load(_root);

        Assert.True(File.Exists(LegacyPath("devid.txt")));
        Assert.True(File.Exists(LegacyPath("session.dat")));
        Assert.True(File.Exists(Path.Combine(_root, "accounts", "anonymous", "queue.json")));
    }

    // ── 主文件 / 备份 / 保留 ────────────────────────────────────────────────

    [Fact]
    public void CorruptPrimary_UsesBackupAndRestoresIt()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(ConfigPath + ".bak", """{ "schemaVersion": 1, "volume": { "level": 33 } }""");
        File.WriteAllText(ConfigPath, "{ broken");

        var result = SettingsBootstrap.Load(_root);

        Assert.Equal(33d, result.Settings.Volume.Level);
        Assert.True(File.Exists(ConfigPath));
        Assert.Equal(33d, JsonAppSettingsStore.Load(ConfigPath).Volume.Level);
        Assert.Single(Directory.GetFiles(_root, "config.json.corrupt-*"));
    }

    [Fact]
    public void CorruptPrimaryAndBackup_UseDefaultsAndPreservePrimary()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(ConfigPath + ".bak", "{ broken too");
        File.WriteAllText(ConfigPath, "{ broken");

        var result = SettingsBootstrap.Load(_root);

        Assert.Equal(VolumeSettings.DefaultLevel, result.Settings.Volume.Level);
        Assert.False(File.Exists(ConfigPath));
        Assert.Single(Directory.GetFiles(_root, "config.json.corrupt-*"));
    }

    [Fact]
    public void UnrecognizedConfig_IsPreservedAndLegacyWins()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(ConfigPath, """{ "volume": { "level": 80 } }""");
        SeedLegacyVolume(37);

        var result = SettingsBootstrap.Load(_root);

        Assert.Equal(37d, result.Settings.Volume.Level);
        Assert.Single(Directory.GetFiles(_root, "config.json.unrecognized-*"));
        Assert.Equal(1, JsonAppSettingsStore.Load(ConfigPath).SchemaVersion);
    }

    [Fact]
    public void FutureSchemaVersion_IsPreserved()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(ConfigPath, """{ "schemaVersion": 999, "volume": { "level": 80 } }""");

        var result = SettingsBootstrap.Load(_root);

        Assert.Equal(VolumeSettings.DefaultLevel, result.Settings.Volume.Level);
        Assert.Single(Directory.GetFiles(_root, "config.json.unrecognized-*"));
        Assert.Equal(AppSettings.CurrentSchemaVersion, JsonAppSettingsStore.Load(ConfigPath).SchemaVersion);
    }

    [Fact]
    public void WriteFailure_LeavesLegacyFilesInPlace()
    {
        Directory.CreateDirectory(Path.Combine(_root, "config.json"));
        SeedLegacyTheme();

        var result = SettingsBootstrap.Load(_root);

        Assert.True(result.Migrated);
        Assert.Equal(AppTheme.Dark, result.Settings.Appearance.Theme);
        Assert.True(File.Exists(LegacyPath("settings.json")));
        Assert.False(Directory.Exists(ArchiveDirectory));
    }
}
