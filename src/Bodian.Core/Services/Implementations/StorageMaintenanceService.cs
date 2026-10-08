using Bodian.Core.Models;
using Bodian.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.Core.Services.Implementations;

/// <inheritdoc cref="IStorageMaintenanceService" />
/// <remarks>
/// <para>
/// <b>清理语义逐项不同</b>，因为清完之后对正在运行的界面影响不同：
/// 播放记录清完「最近播放」页自然就空了（那一页每次进入重读）；
/// 搜索历史必须走 <see cref="ISearchHistorySink"/>，否则内存里那份还在；
/// 日志当天那一份删不掉，因为 Serilog 正持有它的文件句柄。
/// </para>
/// <para>
/// <b>播放记录与搜索历史只算、只清当前账号那一份。</b> 封面缓存与日志是设备级的，跨账号共用。
/// 跨账号清别人的数据比「清不干净」严重得多。
/// </para>
/// </remarks>
public sealed class StorageMaintenanceService : IStorageMaintenanceService
{
    private readonly IPlayHistoryStore? _playHistory;
    private readonly ISearchHistorySink? _searchHistory;
    private readonly ICoverDiskCache? _coverCache;
    private readonly ICurrentAccount? _account;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly string _logDirectory;

    /// <param name="rootDirectory">
    /// 数据根目录。默认 <see cref="AppPaths.LocalAppData"/>；<b>测试可指向临时目录</b> ——
    /// 否则「清理播放记录」这类用例会把跑测试这台机器上的真实历史删掉。
    /// 文件名仍从 <see cref="AppPaths"/> 里取，避免同一个名字在两处各写一遍。
    /// </param>
    /// <param name="account">
    /// 当前账号，决定播放记录与搜索历史算哪一份。为 <c>null</c> 时按匿名桶算。
    /// </param>
    public StorageMaintenanceService(
        IPlayHistoryStore? playHistory = null,
        ISearchHistorySink? searchHistory = null,
        ICoverDiskCache? coverCache = null,
        TimeProvider? timeProvider = null,
        ILogger<StorageMaintenanceService>? logger = null,
        string? rootDirectory = null,
        ICurrentAccount? account = null)
    {
        _playHistory = playHistory;
        _searchHistory = searchHistory;
        _coverCache = coverCache;
        _account = account;
        _time = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<StorageMaintenanceService>.Instance;

        RootDirectory = rootDirectory ?? AppPaths.LocalAppData;
        _logDirectory = Path.Combine(RootDirectory, Path.GetFileName(AppPaths.LogDirectory));
    }

    public string RootDirectory { get; }

    /// <summary>取的是缓存的字段而不是 <c>RootDirectory</c> —— 后者不含 <c>logs</c> 这一段。</summary>
    public string LogDirectory => _logDirectory;

    /// <summary>当前账号的播放记录文件。<b>每次读它都按当前账号解析</b>，免得缓存住切换前的那个。</summary>
    private string PlayHistoryFile => ScopedFile(AppPaths.PlayHistoryFileName);

    /// <summary>当前账号的搜索历史文件。</summary>
    private string SearchHistoryFile => ScopedFile(AppPaths.SearchHistoryFileName);

    private string ScopedFile(string fileName) =>
        AppPaths.AccountFilePath(_account?.Scope ?? AppPaths.AnonymousScope, fileName, RootDirectory);

    public async Task<IReadOnlyList<StorageUsage>> MeasureAsync(CancellationToken cancellationToken = default)
    {
        var usages = new List<StorageUsage>(4);

        var cover = _coverCache is null
            ? new CoverCacheUsage(0, 0)
            : await _coverCache.MeasureAsync(cancellationToken).ConfigureAwait(true);
        usages.Add(new StorageUsage(StorageItemKind.CoverCache, cover.Bytes, cover.FileCount));

        usages.Add(MeasureFile(StorageItemKind.PlayHistory, PlayHistoryFile));
        usages.Add(MeasureFile(StorageItemKind.SearchHistory, SearchHistoryFile));

        var (logBytes, logCount) = MeasureDirectory(_logDirectory, "*.log");
        usages.Add(new StorageUsage(StorageItemKind.Logs, logBytes, logCount));

        return usages;
    }

    public async Task<StorageClearOutcome> ClearAsync(
        StorageItemKind item, CancellationToken cancellationToken = default)
    {
        switch (item)
        {
            case StorageItemKind.CoverCache:
                return await ClearCoverCacheAsync(cancellationToken).ConfigureAwait(true);

            case StorageItemKind.PlayHistory:
                return await ClearFileAsync(StorageItemKind.PlayHistory, PlayHistoryFile)
                    .ConfigureAwait(true);

            case StorageItemKind.SearchHistory:
                return await ClearSearchHistoryAsync().ConfigureAwait(true);

            case StorageItemKind.Logs:
                return ClearLogs();

            default:
                throw new ArgumentOutOfRangeException(nameof(item));
        }
    }

    public async Task<IReadOnlyList<StorageClearOutcome>> ClearAllAsync(
        CancellationToken cancellationToken = default)
    {
        var outcomes = new List<StorageClearOutcome>(4);

        foreach (var item in Enum.GetValues<StorageItemKind>())
        {
            outcomes.Add(await ClearAsync(item, cancellationToken).ConfigureAwait(true));
        }

        return outcomes;
    }

    private async Task<StorageClearOutcome> ClearCoverCacheAsync(CancellationToken cancellationToken)
    {
        if (_coverCache is null)
        {
            return new StorageClearOutcome(StorageItemKind.CoverCache, 0, 0, "封面缓存不可用");
        }

        var before = await _coverCache.MeasureAsync(cancellationToken).ConfigureAwait(true);
        var removed = await _coverCache.ClearAsync(cancellationToken).ConfigureAwait(true);

        // 正在显示的封面不会因此消失：位图已经解码在内存里，删掉文件只是下次要重新下。
        return new StorageClearOutcome(StorageItemKind.CoverCache, removed, before.Bytes);
    }


    private Task<StorageClearOutcome> ClearSearchHistoryAsync()
    {
        if (_searchHistory is null)
        {
            return Task.FromResult(new StorageClearOutcome(
                StorageItemKind.SearchHistory, 0, 0, "搜索历史不可用"));
        }

        // 走 sink 而不是直接写 store：内存里那份也要清，否则当前会话里建议列表还是满的。
        _searchHistory.ClearSearchHistory();
        return Task.FromResult(new StorageClearOutcome(StorageItemKind.SearchHistory, 1, 0));
    }

    /// <remarks>
    /// <b>跳过当天那一份。</b> Serilog 正持有它的句柄，删了要么抛 IOException，
    /// 要么更糟 —— 让后续写入落进一个已经不在目录里的文件。
    /// 删不掉的单个文件计入 <c>Message</c>，不让整次清理变成失败。
    /// </remarks>
    private StorageClearOutcome ClearLogs()
    {
        if (!System.IO.Directory.Exists(_logDirectory))
        {
            return new StorageClearOutcome(StorageItemKind.Logs, 0, 0);
        }

        var today = _time.GetLocalNow().Date;
        long freed = 0;
        var removed = 0;
        var skipped = 0;

        foreach (var file in new DirectoryInfo(_logDirectory).EnumerateFiles("*.log"))
        {
            if (file.LastWriteTime.Date == today)
            {
                continue;
            }

            try
            {
                var length = file.Length;
                file.Delete();
                freed += length;
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped++;
                _logger.LogDebug(ex, "日志文件删不掉：{Path}", file.FullName);
            }
        }

        return new StorageClearOutcome(
            StorageItemKind.Logs,
            removed,
            freed,
            skipped == 0 ? "" : $"有 {skipped} 个日志文件正被占用，未能删除");
    }

    /// <remarks>
    /// <b>腾出的字节数按清理**之前**的大小算。</b> 播放记录清完文件还在（只是内容变成空数组），
    /// 事后去量会得到 0，而用户刚刚明明看到那一栏有几百 KB。
    /// </remarks>
    private async Task<StorageClearOutcome> ClearFileAsync(StorageItemKind item, string path)
    {
        if (item == StorageItemKind.PlayHistory && _playHistory is null)
        {
            return new StorageClearOutcome(item, 0, 0, "播放记录不可用");
        }

        long bytes = File.Exists(path) ? new FileInfo(path).Length : 0;

        if (item == StorageItemKind.PlayHistory)
        {
            await _playHistory!.ClearAsync().ConfigureAwait(true);
        }

        return new StorageClearOutcome(item, 1, bytes);
    }

    private static StorageUsage MeasureFile(StorageItemKind item, string path)
    {
        if (!File.Exists(path))
        {
            return new StorageUsage(item, 0, 0);
        }

        return new StorageUsage(item, new FileInfo(path).Length, 1);
    }

    private static (long Bytes, int Count) MeasureDirectory(string directory, string pattern)
    {
        if (!System.IO.Directory.Exists(directory))
        {
            return (0, 0);
        }

        long bytes = 0;
        var count = 0;

        foreach (var file in new DirectoryInfo(directory).EnumerateFiles(pattern))
        {
            bytes += file.Length;
            count++;
        }

        return (bytes, count);
    }
}
