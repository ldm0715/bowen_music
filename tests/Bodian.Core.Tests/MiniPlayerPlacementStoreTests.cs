using Bodian.Core.Models;
using Bodian.Core.Services;
using Bodian.Core.Services.Abstractions;
using Bodian.Core.Services.Implementations;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 小窗的位置记忆。
/// </summary>
/// <remarks>
/// 实现是复用主窗口那套 <c>JsonWindowPlacementStore</c>，所以这里只验两件本项目特有的事：
/// 用的文件与另外两份窗口几何都不是同一个，以及按这个接口注册的那份能正常往返。
/// </remarks>
public sealed class MiniPlayerPlacementStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "bodian-mini-player-window-tests", Guid.NewGuid().ToString("N"));

    private string File_ => Path.Combine(_directory, "mini-player-window.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void MiniPlayerWindowFile_IsNotSharedWithTheOtherWindows()
    {
        // 共用路径会让几份记录互相覆盖，而且只会在「关一个窗、再关另一个」时暴露。
        Assert.NotEqual(AppPaths.WindowFile, AppPaths.MiniPlayerWindowFile);
        Assert.NotEqual(AppPaths.DesktopLyricsWindowFile, AppPaths.MiniPlayerWindowFile);
    }

    [Fact]
    public void RoundTripsPosition()
    {
        IMiniPlayerPlacementStore store = new MiniPlayerPlacementStore(File_);

        store.Save(new WindowPlacement(1180, 760, 420, 80));

        var loaded = new MiniPlayerPlacementStore(File_).Load();

        Assert.NotNull(loaded);
        Assert.Equal(1180, loaded!.X);
        Assert.Equal(760, loaded.Y);
    }

    [Fact]
    public void MissingFile_ReturnsNull()
    {
        Assert.Null(new MiniPlayerPlacementStore(File_).Load());
    }

    [Fact]
    public void BrokenFile_ReturnsNullAndKeepsTheFile()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(File_, "{ not json");

        Assert.Null(new MiniPlayerPlacementStore(File_).Load());

        // 坏文件不删 —— 让用户下次还能从中捞回自己摆的位置。
        Assert.True(File.Exists(File_));
    }
}
