using Bodian.Core.Models;
using Bodian.Core.Services.Implementations;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 快捷键键位的本地存储。**零网络、零真实用户目录** —— 每个测试用自己那份临时文件。
/// </summary>
/// <remarks>
/// 与 <c>ViewModeSettingsStoreTests</c> 同一套形态：重点是坏文件不能让整套快捷键失灵，
/// 以及写不进去时不能把异常扔回调用方。
/// </remarks>
public sealed class ShortcutSettingsStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "bodian-shortcut-tests", Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_directory, "shortcuts.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private JsonShortcutSettingsStore NewStore() => new(SettingsPath);

    [Fact]
    public void NoFile_FallsBackToTheReferenceKeys()
    {
        Assert.Equal(ShortcutSettings.Default.Bindings, NewStore().Load().Bindings);
    }

    [Fact]
    public void SavedSettings_RoundTrip()
    {
        var settings = new ShortcutSettings
        {
            Bindings =
            [
                new(ShortcutAction.NextTrack, ShortcutKey.N, ShortcutModifiers.Control | ShortcutModifiers.Shift),
                new(ShortcutAction.TogglePlayPause, ShortcutKey.Space, ShortcutModifiers.None),
            ],
        };

        NewStore().Save(settings);

        // 读回来会过一遍 Normalized：缺的补上、顺序按 ShortcutActions.All 排。
        var loaded = NewStore().Load();

        var next = loaded.For(ShortcutAction.NextTrack);
        Assert.NotNull(next);
        Assert.Equal(ShortcutKey.N, next.Key);
        Assert.Equal(ShortcutModifiers.Control | ShortcutModifiers.Shift, next.Modifiers);
    }

    [Fact]
    public void Enums_AreWrittenAsNamesNotNumbers()
    {
        // 写成数字的话，以后往枚举中间插一个成员就会让旧文件静默读成别的动作。
        NewStore().Save(ShortcutSettings.Default);

        var json = File.ReadAllText(SettingsPath);

        Assert.Contains("NextTrack", json, StringComparison.Ordinal);
        Assert.Contains("Right", json, StringComparison.Ordinal);
        Assert.Contains("Control", json, StringComparison.Ordinal);
    }

    [Fact]
    public void BrokenFile_FallsBackToDefaultAndKeepsTheFile()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, "{ this is not json");

        Assert.Equal(ShortcutSettings.Default.Bindings, NewStore().Load().Bindings);

        // 坏文件要留着，不然没得查。
        Assert.True(File.Exists(SettingsPath));
    }

    [Fact]
    public void HandEditedBadBinding_IsRepairedInsteadOfDiscardingTheWholeTable()
    {
        Directory.CreateDirectory(_directory);

        // 裸字母键 + 一个枚举外的键：两项都该被换回默认，而不是整份作废。
        File.WriteAllText(SettingsPath, """
            {
              "Bindings": [
                { "Action": "NextTrack", "Key": "N", "Modifiers": "None" },
                { "Action": "PreviousTrack", "Key": "27", "Modifiers": "Control" },
                { "Action": "ToggleFavorite", "Key": "F5", "Modifiers": "None" }
              ]
            }
            """);

        var loaded = NewStore().Load();

        var next = loaded.For(ShortcutAction.NextTrack);
        Assert.NotNull(next);
        Assert.Equal(ShortcutKey.Right, next.Key);

        var previous = loaded.For(ShortcutAction.PreviousTrack);
        Assert.NotNull(previous);
        Assert.Equal(ShortcutKey.Left, previous.Key);

        // 合法的那一项要保住。
        var favorite = loaded.For(ShortcutAction.ToggleFavorite);
        Assert.NotNull(favorite);
        Assert.Equal(ShortcutKey.F5, favorite.Key);
    }

    [Fact]
    public void Save_LeavesNoTemporaryFileBehind()
    {
        NewStore().Save(ShortcutSettings.Default);

        Assert.True(File.Exists(SettingsPath));
        Assert.False(File.Exists(SettingsPath + ".tmp"));
    }

    [Fact]
    public void Save_CreatesTheDirectory()
    {
        Assert.False(Directory.Exists(_directory));

        NewStore().Save(ShortcutSettings.Default);

        Assert.True(File.Exists(SettingsPath));
    }
}
