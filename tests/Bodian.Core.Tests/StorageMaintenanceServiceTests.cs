using Bodian.Core.Models;
using Bodian.Core.Services.Abstractions;
using Bodian.Core.Services.Implementations;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 「存储」那几项的统计与清理。**零真实用户目录** —— 根目录指向临时目录，
/// 否则「清理播放记录」会把跑测试这台机器上的真实历史删掉。
/// </summary>
public sealed class StorageMaintenanceServiceTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "bodian-storage-tests", Guid.NewGuid().ToString("N"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string PlayHistoryFile => Path.Combine(_root, "history.json");
    private string SearchHistoryFile => Path.Combine(_root, "search-history.json");
    private string LogDirectory => Path.Combine(_root, "logs");

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
        ICoverDiskCache? coverCache = null) =>
        new(playHistory, searchHistory, coverCache, TimeProvider.System, rootDirectory: _root);

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

        Assert.Equal(4, usages.Count);
        Assert.All(usages, usage => Assert.Equal(0, usage.Bytes));
    }

    [Fact]
    public async Task Measure_AddsUpFilesAndLogs()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(PlayHistoryFile, new string('x', 500), Ct);
        await File.WriteAllTextAsync(SearchHistoryFile, new string('y', 300), Ct);
        WriteLog("bodian-20260101.log", "old", DateTime.Now.AddDays(-3));

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
        Directory.CreateDirectory(_root);
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
        Directory.CreateDirectory(_root);
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
        WriteLog("bodian-today.log", "now", DateTime.Now);
        WriteLog("bodian-yesterday.log", "old", DateTime.Now.AddDays(-1));
        WriteLog("bodian-lastweek.log", "older", DateTime.Now.AddDays(-7));

        var outcome = await NewService().ClearAsync(StorageItemKind.Logs, Ct);

        Assert.True(outcome.Succeeded);
        Assert.Equal(2, outcome.FilesRemoved);
        Assert.True(File.Exists(Path.Combine(LogDirectory, "bodian-today.log")));
        Assert.False(File.Exists(Path.Combine(LogDirectory, "bodian-yesterday.log")));
        Assert.False(File.Exists(Path.Combine(LogDirectory, "bodian-lastweek.log")));
    }

    [Fact]
    public async Task ClearLogs_ReportsSkippedFilesWithoutFailingTheWholeRun()
    {
        WriteLog("bodian-locked.log", "held", DateTime.Now.AddDays(-1));
        WriteLog("bodian-free.log", "free", DateTime.Now.AddDays(-1));

        // 拿住其中一个的文件句柄，模拟「正被 Serilog 占用」。
        using (File.Open(Path.Combine(LogDirectory, "bodian-locked.log"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var outcome = await NewService().ClearAsync(StorageItemKind.Logs, Ct);

            // 单个删不掉不能让整次清理变成失败，但要在结果里说出来。
            Assert.False(outcome.Succeeded);
            Assert.Contains("未能删除", outcome.Message, StringComparison.Ordinal);

            // 能删的那个照样要删掉。
            Assert.False(File.Exists(Path.Combine(LogDirectory, "bodian-free.log")));
        }
    }

    [Fact]
    public async Task ClearAll_VisitsEveryKind()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(PlayHistoryFile, "x", Ct);
        var store = new StubPlayHistoryStore();
        var sink = new StubSearchHistorySink();

        var outcomes = await NewService(playHistory: store, searchHistory: sink).ClearAllAsync(Ct);

        Assert.Equal(4, outcomes.Count);
        Assert.True(store.Cleared);
        Assert.Equal(1, sink.ClearCount);
    }

    [Fact]
    public async Task ClearAll_SucceedsWithNoStoresWired()
    {
        // 播放记录与搜索历史都不可用时，另外两项仍要能清，整体不抛。
        var outcomes = await NewService().ClearAllAsync(Ct);

        Assert.Equal(4, outcomes.Count);
        Assert.False(outcomes.Single(o => o.Item == StorageItemKind.PlayHistory).Succeeded);
        Assert.False(outcomes.Single(o => o.Item == StorageItemKind.SearchHistory).Succeeded);
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
