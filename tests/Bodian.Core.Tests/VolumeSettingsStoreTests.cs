using Bodian.Core.Models;
using Bodian.Core.Services.Implementations;
using Xunit;

namespace Bodian.Core.Tests;

public sealed class VolumeSettingsStoreTests
{
    private static string NewPath() => Path.Combine(Path.GetTempPath(), "bodian-volume-settings-tests",
        Guid.NewGuid().ToString("N"), "volume.json");

    [Fact]
    public void MissingFile_IsTheDefaultVolume_WithoutCreatingAFile()
    {
        var path = NewPath();
        Assert.Equal(VolumeSettings.DefaultLevel, new JsonVolumeSettingsStore(path).Load().Level);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Volume_RoundTrips()
    {
        var path = NewPath();
        var store = new JsonVolumeSettingsStore(path);
        Assert.True(store.Save(new VolumeSettings(40)));
        Assert.Equal(40, store.Load().Level);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(150, 100)]
    [InlineData(37.5, 37.5)]
    [InlineData(1e308, 100)]
    public void OutOfRangeLevel_IsClamped(double saved, double expected)
    {
        var path = NewPath();
        var store = new JsonVolumeSettingsStore(path);
        store.Save(new VolumeSettings(saved));
        Assert.Equal(expected, store.Load().Level);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("{\"Level\":\"NaN\"}")]
    public void InvalidContent_FallsBackToTheDefault_AndPreservesFile(string json)
    {
        var path = NewPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
        Assert.Equal(VolumeSettings.DefaultLevel, new JsonVolumeSettingsStore(path).Load().Level);
        Assert.Equal(json, File.ReadAllText(path));
    }

    [Fact]
    public void EmptyObject_UsesTheConstructorDefault()
    {
        // 位置 record 的默认参数在缺键时生效 —— 与队列快照那套「缺键即零值」不同，
        // 所以音量这份不需要额外的补齐。
        var path = NewPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{}");
        Assert.Equal(VolumeSettings.DefaultLevel, new JsonVolumeSettingsStore(path).Load().Level);
    }

    [Fact]
    public void SaveFailure_IsReported()
    {
        var path = NewPath();
        Directory.CreateDirectory(path);
        Assert.False(new JsonVolumeSettingsStore(path).Save(new VolumeSettings(50)));
    }
}
