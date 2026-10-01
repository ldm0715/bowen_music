using Bodian.Core.Models;
using Bodian.Core.Services.Implementations;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 窗口位置记录。**零真实用户目录** —— 每个测试用自己那份临时文件。
/// </summary>
public sealed class WindowPlacementStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "bodian-window-tests", Guid.NewGuid().ToString("N"));

    private string Path_ => Path.Combine(_directory, "window.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private JsonWindowPlacementStore NewStore() => new(Path_);

    [Fact]
    public void NoFile_ReturnsNull()
    {
        Assert.Null(NewStore().Load());
    }

    [Fact]
    public void Save_ThenLoad_RoundTrips()
    {
        NewStore().Save(new WindowPlacement(120, 80, 1300, 1000));

        var loaded = NewStore().Load();

        Assert.NotNull(loaded);
        Assert.Equal(120, loaded.X);
        Assert.Equal(80, loaded.Y);
        Assert.Equal(1300, loaded.Width);
        Assert.Equal(1000, loaded.Height);
    }

    /// <summary>
    /// 负坐标是**合法**的：副屏摆在主屏左边或上边时就是这样。
    /// </summary>
    /// <remarks>
    /// 这条单独写出来，是因为「坐标是负的所以要当非法丢掉」是个很自然的误判 ——
    /// 真那样做的话，副屏用户每次启动窗口都会跳回主屏。
    /// </remarks>
    [Fact]
    public void NegativeCoordinates_AreValid()
    {
        NewStore().Save(new WindowPlacement(-1800, -200, 1300, 1000));

        var loaded = NewStore().Load();

        Assert.NotNull(loaded);
        Assert.Equal(-1800, loaded.X);
        Assert.Equal(-200, loaded.Y);
    }

    [Theory]
    [InlineData(0, 1000)]
    [InlineData(1300, 0)]
    [InlineData(-100, 1000)]
    public void NonPositiveSize_IsRejected(int width, int height)
    {
        NewStore().Save(new WindowPlacement(0, 0, width, height));

        Assert.Null(NewStore().Load());
    }

    [Fact]
    public void Save_CreatesMissingDirectory()
    {
        Assert.False(Directory.Exists(_directory));

        NewStore().Save(new WindowPlacement(0, 0, 800, 600));

        Assert.True(File.Exists(Path_));
    }

    [Fact]
    public void CorruptJson_ReturnsNull_ButFileIsKept()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path_, "{ not json at all");

        Assert.Null(NewStore().Load());

        // 坏文件**不删** —— 留着还能人工看一眼出了什么事。
        Assert.True(File.Exists(Path_));
    }

    [Fact]
    public void NullJson_ReturnsNull()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path_, "null");

        Assert.Null(NewStore().Load());
    }

    [Fact]
    public void Save_ToUnwritablePath_DoesNotThrow()
    {
        // 目标「文件」那一层被占成目录，写必然失败。窗口记录写不进去只该记日志。
        Directory.CreateDirectory(Path_);

        NewStore().Save(new WindowPlacement(0, 0, 800, 600));
    }
}
