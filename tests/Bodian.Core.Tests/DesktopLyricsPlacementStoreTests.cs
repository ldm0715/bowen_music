using Bodian.Core.Models;
using Bodian.Core.Services;
using Bodian.Core.Services.Implementations;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 桌面歌词条的位置记忆。
/// </summary>
/// <remarks>
/// 实现是复用主窗口那套 <c>JsonWindowPlacementStore</c>，所以这里只验两件本项目特有的事：
/// 用的文件与主窗口不是同一个，以及按这个接口注册的那份能正常往返。
/// </remarks>
public sealed class DesktopLyricsPlacementStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "bodian-desktop-lyrics-window-tests", Guid.NewGuid().ToString("N"));

    private string Path_ => Path.Combine(_directory, "desktop-lyrics-window.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void DesktopLyricsWindowFile_IsNotTheMainWindowFile()
    {
        // 共用路径会让两份记录互相覆盖，而且只会在「关一次桌面歌词条、再关一次主窗口」时暴露。
        Assert.NotEqual(AppPaths.WindowFile, AppPaths.DesktopLyricsWindowFile);
        Assert.NotEqual(AppPaths.SettingsFile, AppPaths.DesktopLyricsSettingsFile);
    }

    [Fact]
    public void RoundTripsPositionAndWidth()
    {
        var store = new JsonWindowPlacementStore(Path_);

        store.Save(new WindowPlacement(120, 900, 1100, 212));

        var loaded = new JsonWindowPlacementStore(Path_).Load();

        Assert.NotNull(loaded);
        Assert.Equal(120, loaded!.X);
        Assert.Equal(900, loaded.Y);
        Assert.Equal(1100, loaded.Width);
    }

    [Fact]
    public void MissingFile_ReturnsNull()
    {
        Assert.Null(new JsonWindowPlacementStore(Path_).Load());
    }
}
