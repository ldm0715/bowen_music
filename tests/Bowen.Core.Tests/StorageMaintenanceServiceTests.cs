using Bowen.Core.Models;
using Bowen.Core.Services;
using Bowen.Core.Services.Abstractions;
using Bowen.Core.Services.Implementations;
using Bowen.Core.Tests.Support;
using Xunit;

namespace Bowen.Core.Tests;

/// <summary>
/// 「存储」那几项的统计与清理。**零真实用户目录** —— 根目录指向临时目录，
/// 否则「清理播放记录」会把跑测试这台机器上的真实历史删掉。
/// </summary>
public sealed class StorageMaintenanceServiceTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "bowen-storage-tests", Guid.NewGuid().ToString("N"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// 未给账号时服务按匿名桶算，所以测试的数据也摆在那儿。
    /// 目录名同样从 <see cref="AppPaths"/> 取，不在这里写第二遍。
    /// </summary>
    private string AccountDirectory => Path.Combine(
        _root, Path.GetFileName(AppPaths.AccountsDirectory), AppPaths.AnonymousScope);

    private string PlayHistoryFile => Path.Combine(AccountDirectory, AppPaths.PlayHistoryFileName);
    private string SearchHistoryFile => Path.Combine(AccountDirectory, AppPaths.SearchHistoryFileName);
    private string LogDirectory => Path.Combine(_root, "logs");
    private string UpdateDownloadDirectory => Path.Combine(_root, "updates");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private StorageMaintenanceService NewService(
        IPlayHistoryStore? playHistory = null,
        ISearchHistorySink? searchHistory = null,
        ICoverDiskCache? coverCache = null,
        ICurrentAccount? account = null) =>
        new(playHistory, searchHistory, coverCache, TimeProvider.System, rootDirectory: _root, account: account);

    private void WriteLog(string name, string content, DateTime lastWrite)
    {
        Directory.CreateDirectory(LogDirectory);
        var path = Path.Combine(LogDirectory, name);
        File.WriteAllText(path, content);
        File.SetLastWriteTime(path, lastWrite);
    }

    // ── 统计 ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Measure_ReportsEveryKind_EvenWhenNothingExists()
    {
        var usages = await NewService().MeasureAsync(Ct);

        Assert.Equal(5, usages.Count);
        Assert.All(usages, usage => Assert.Equal(0, usage.Bytes));
    }

    [Fact]
    public async Task Measure_AddsUpFilesAndLogs()
    {
        Directory.CreateDirectory(AccountDirectory);
        await File.WriteAllTextAsync(PlayHistoryFile, new string('x', 500), Ct);
        await File.WriteAllTextAsync(SearchHistoryFile, new string('y', 300), Ct);
        WriteLog("bowen-20260101.log", "old", DateTime.Now.AddDays(-3));

        var usages = await NewService().MeasureAsync(Ct);

        Assert.Equal(500, usages.Single(u => u.Item == StorageItemKind.PlayHistory).Bytes);
        Assert.Equal(300, usages.Single(u => u.Item == StorageItemKind.SearchHistory).Bytes);
        Assert.Equal(3, usages.Single(u => u.Item == StorageItemKind.Logs).Bytes);
        Assert.Equal(1, usages.Single(u => u.Item == StorageItemKind.Logs).FileCount);
    }

    // ── 清理 ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ClearPlayHistory_GoesThroughTheStore()
    {
        Directory.CreateDirectory(AccountDirectory);
        await File.WriteAllTextAsync(PlayHistoryFile, new string('x', 500), Ct);
        var store = new StubPlayHistoryStore();

        var outcome = await NewService(playHistory: store).ClearAsync(StorageItemKind.PlayHistory, Ct);

        Assert.True(store.Cleared);
        Assert.True(outcome.Succeeded);

        // 腾出的量按清理**之前**算：清完文件还在（内容变成空数组），事后量会得到 0。
        Assert.Equal(500, outcome.BytesFreed);
    }

    [Fact]
    public async Task ClearSearchHistory_GoesThroughTheSinkNotTheFile()
    {
        Directory.CreateDirectory(AccountDirectory);
        await File.WriteAllTextAsync(SearchHistoryFile, new string('y', 300), Ct);
        var sink = new StubSearchHistorySink();

        var outcome = await NewService(searchHistory: sink).ClearAsync(StorageItemKind.SearchHistory, Ct);

        // 只落盘是不够的：内存里那份也要清，否则当前会话里建议列表还是满的。
        Assert.Equal(1, sink.ClearCount);
        Assert.True(outcome.Succeeded);
    }

    [Fact]
    public async Task ClearLogs_KeepsTodaysFileAndDeletesOlderOnes()
    {
        WriteLog("bowen-today.log", "now", DateTime.Now);
        WriteLog("bowen-yesterday.log", "old", DateTime.Now.AddDays(-1));
        WriteLog("bowen-lastweek.log", "older", DateTime.Now.AddDays(-7));

        var outcome = await NewService().ClearAsync(StorageItemKind.Logs, Ct);

        Assert.True(outcome.Succeeded);
        Assert.Equal(2, outcome.FilesRemoved);
        Assert.True(File.Exists(Path.Combine(LogDirectory, "bowen-today.log")));
        Assert.False(File.Exists(Path.Combine(LogDirectory, "bowen-yesterday.log")));
        Assert.False(File.Exists(Path.Combine(LogDirectory, "bowen-lastweek.log")));
    }

    [Fact]
    public async Task ClearLogs_ReportsSkippedFilesWithoutFailingTheWholeRun()
    {
        WriteLog("bowen-locked.log", "held", DateTime.Now.AddDays(-1));
        WriteLog("bowen-free.log", "free", DateTime.Now.AddDays(-1));

        // 拿住其中一个的文件句柄，模拟「正被 Serilog 占用」。
        using (File.Open(Path.Combine(LogDirectory, "bowen-locked.log"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var outcome = await NewService().ClearAsync(StorageItemKind.Logs, Ct);

            // 单个删不掉不能让整次清理变成失败，但要在结果里说出来。
            Assert.False(outcome.Succeeded);
            Assert.Contains("未能删除", outcome.Message, StringComparison.Ordinal);

            // 能删的那个照样要删掉。
            Assert.False(File.Exists(Path.Combine(LogDirectory, "bowen-free.log")));
        }
    }

    [Fact]
    public async Task Measure_IncludesUpdatePackages()
    {
        Directory.CreateDirectory(UpdateDownloadDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(UpdateDownloadDirectory, "BowenMusic-0.9.0-setup.exe"), new string('x', 700), Ct);
        await File.WriteAllTextAsync(
            Path.Combine(UpdateDownloadDirectory, "portable.zip.part"), new string('y', 100), Ct);

        var usage = (await NewService().MeasureAsync(Ct))
            .Single(item => item.Item == StorageItemKind.UpdatePackages);

        Assert.Equal(800, usage.Bytes);
        Assert.Equal(2, usage.FileCount);
    }

    [Fact]
    public async Task ClearUpdatePackages_DeletesFilesAndKeepsDirectory()
    {
        Directory.CreateDirectory(UpdateDownloadDirectory);
        var package = Path.Combine(UpdateDownloadDirectory, "BowenMusic-0.9.0-setup.exe");
        await File.WriteAllTextAsync(package, new string('x', 700), Ct);

        var outcome = await NewService().ClearAsync(StorageItemKind.UpdatePackages, Ct);

        Assert.True(outcome.Succeeded);
        Assert.Equal(1, outcome.FilesRemoved);
        Assert.Equal(700, outcome.BytesFreed);
        Assert.False(File.Exists(package));
        Assert.True(Directory.Exists(UpdateDownloadDirectory));
    }

    [Fact]
    public async Task ClearUpdatePackages_ReportsLockedFileWithoutFailingTheRest()
    {
        Directory.CreateDirectory(UpdateDownloadDirectory);
        var locked = Path.Combine(UpdateDownloadDirectory, "BowenMusic-0.9.0-setup.exe");
        var free = Path.Combine(UpdateDownloadDirectory, "portable.zip");
        await File.WriteAllTextAsync(locked, "held", Ct);
        await File.WriteAllTextAsync(free, "free", Ct);

        // 拿住其中一个的文件句柄，模拟「正在下载的 .part」。
        using (File.Open(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var outcome = await NewService().ClearAsync(StorageItemKind.UpdatePackages, Ct);

            Assert.False(outcome.Succeeded);
            Assert.Contains("未能删除", outcome.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(free));
        }
    }

    [Fact]
    public async Task ClearAll_VisitsEveryKind()
    {
        Directory.CreateDirectory(AccountDirectory);
        await File.WriteAllTextAsync(PlayHistoryFile, "x", Ct);
        var store = new StubPlayHistoryStore();
        var sink = new StubSearchHistorySink();

        var outcomes = await NewService(playHistory: store, searchHistory: sink).ClearAllAsync(Ct);

        Assert.Equal(5, outcomes.Count);
        Assert.True(store.Cleared);
        Assert.Equal(1, sink.ClearCount);
    }

    [Fact]
    public async Task ClearAll_SucceedsWithNoStoresWired()
    {
        // 播放记录与搜索历史都不可用时，其余几项仍要能清，整体不抛。
        var outcomes = await NewService().ClearAllAsync(Ct);

        Assert.Equal(5, outcomes.Count);
        Assert.False(outcomes.Single(o => o.Item == StorageItemKind.PlayHistory).Succeeded);
        Assert.False(outcomes.Single(o => o.Item == StorageItemKind.SearchHistory).Succeeded);
    }

    [Fact]
    public async Task AnotherAccountsHistory_IsNeitherMeasuredNorCleared()
    {
        // 跨账号清别人的数据，比「清不干净」严重得多。
        var account = new FakeCurrentAccount();
        account.SignIn("111");

        var mine = AppPaths.AccountFilePath("111", AppPaths.PlayHistoryFileName, _root);
        var theirs = AppPaths.AccountFilePath("222", AppPaths.PlayHistoryFileName, _root);
        Directory.CreateDirectory(Path.GetDirectoryName(mine)!);
        Directory.CreateDirectory(Path.GetDirectoryName(theirs)!);
        await File.WriteAllTextAsync(mine, new string('x', 500), Ct);
        await File.WriteAllTextAsync(theirs, new string('y', 300), Ct);

        var service = NewService(playHistory: new StubPlayHistoryStore(), account: account);
        var usages = await service.MeasureAsync(Ct);

        Assert.Equal(500, usages.Single(u => u.Item == StorageItemKind.PlayHistory).Bytes);

        await service.ClearAsync(StorageItemKind.PlayHistory, Ct);

        Assert.True(File.Exists(theirs));
        Assert.Equal(300, new FileInfo(theirs).Length);
    }

    private sealed class StubPlayHistoryStore : IPlayHistoryStore
    {
        public bool Cleared { get; private set; }

        public Task<IReadOnlyList<PlayHistoryEntry>> GetRecentAsync(
            int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PlayHistoryEntry>>([]);

        public Task RecordAsync(Track track, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            Cleared = true;
            return Task.CompletedTask;
        }
    }

    private sealed class StubSearchHistorySink : ISearchHistorySink
    {
        public int ClearCount { get; private set; }

        public void ClearSearchHistory() => ClearCount++;
    }
}
