using Bodian.Core.Models;
using Bodian.Core.Services.Implementations;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// <c>playback.json</c>：新格式（对象）的读写，以及老格式（裸枚举）的兼容回落。
/// </summary>
public sealed class PlaybackSettingsStoreTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "bodian-playback-settings-tests", Guid.NewGuid().ToString("N"));

    private string Path_ => Path.Combine(_root, "playback.json");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private JsonPlaybackSettingsStore NewStore() => new(Path_);

    private void Write(string json)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path_, json);
    }

    [Fact]
    public void MissingFile_IsTheDefault()
    {
        Assert.Equal(PlaybackSettings.Default, NewStore().Load());
    }

    [Fact]
    public void RoundTrips()
    {
        NewStore().Save(new PlaybackSettings(PlayMode.Shuffle, RestoreQueue: false));

        var loaded = NewStore().Load();

        Assert.Equal(PlayMode.Shuffle, loaded.Mode);
        Assert.False(loaded.RestoreQueue);
    }

    [Fact]
    public void LegacyBareEnum_KeepsTheModeAndDefaultsTheSwitchToOn()
    {
        // 加「记住播放列表」之前，这份文件里只有一个裸枚举（枚举按仓库约定存数字，不是名字）。
        // 升级上来的用户不能因此丢播放模式。
        Write("1");

        var loaded = NewStore().Load();

        Assert.Equal(PlayMode.ListLoop, loaded.Mode);
        Assert.True(loaded.RestoreQueue);
    }

    [Fact]
    public void ObjectWithoutTheSwitch_DefaultsItToOn()
    {
        // 手改过的文件：缺这一项时补「开」。补「关」会静默地把用户的播放列表记不住。
        Write("{\"Mode\":2}");

        var loaded = NewStore().Load();

        Assert.Equal(PlayMode.Shuffle, loaded.Mode);
        Assert.True(loaded.RestoreQueue);
    }

    [Fact]
    public void ExplicitOff_SurvivesAReload()
    {
        Write("{\"Mode\":0,\"RestoreQueue\":false}");

        Assert.False(NewStore().Load().RestoreQueue);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("99")]
    [InlineData("{\"Mode\":99}")]
    public void BrokenContent_FallsBackToDefault_WithoutThrowing(string json)
    {
        Write(json);

        Assert.Equal(PlaybackSettings.Default, NewStore().Load());
    }
}
