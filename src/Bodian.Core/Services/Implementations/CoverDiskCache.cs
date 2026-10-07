using System.Collections.Concurrent;
using System.Net.Http;
using Bodian.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.Core.Services.Implementations;

/// <inheritdoc cref="ICoverDiskCache" />
/// <remarks>
/// <para>
/// <b>淘汰只看最后写入时间，不做读访问记账。</b> 精确的 LRU 要么每次读都写一次文件、
/// 要么维护一份每次读都改写的索引，两种代价都不值；而「用户实际浏览过的封面」这个工作集
/// 用写入顺序近似已经足够 —— 刚看过的图刚写过。
/// </para>
/// <para>
/// <b>写穿是 fire-and-forget，所有异常自己吞。</b> 见接口上的说明。
/// </para>
/// </remarks>
public sealed class CoverDiskCache : ICoverDiskCache, IDisposable
{
    /// <summary>默认上限。256 MiB 大约能装下几千张列表用的封面，够用又不至于占满盘。</summary>
    public const long DefaultCapacityBytes = 256L * 1024 * 1024;

    /// <summary>并发下载上限。别把别人的 CDN 当压测对象。</summary>
    private const int MaxConcurrentDownloads = 4;

    /// <summary>「磁盘上没有」这份备忘的上限，超了就整份丢掉重记。</summary>
    private const int MissingMemoLimit = 4096;

    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _http;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(MaxConcurrentDownloads);
    private readonly ConcurrentDictionary<string, Lazy<Task>> _inFlight = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _missing = new(StringComparer.Ordinal);

    /// <summary>目录内已知的总字节数。<c>-1</c> 表示还没量过。</summary>
    private long _totalBytes = -1;

    public CoverDiskCache(
        HttpMessageHandler handler,
        string? directory = null,
        long capacityBytes = DefaultCapacityBytes,
        ILogger<CoverDiskCache>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _http = new HttpClient(handler, disposeHandler: false) { Timeout = DownloadTimeout };
        _logger = logger ?? NullLogger<CoverDiskCache>.Instance;
        Directory = directory ?? AppPaths.CoverCacheDirectory;
        CapacityBytes = capacityBytes;
    }

    public string Directory { get; }

    public long CapacityBytes { get; }

    public bool TryGetPath(string cacheKey, out string? path)
    {
        path = PathFor(cacheKey);

        // 先看备忘：没命中过磁盘的键不值得每次都去问一次文件系统，
        // 而封面缓存未命中是常态（新图、清过缓存、超出上限被淘汰的）。
        if (_missing.ContainsKey(cacheKey))
        {
            path = null;
            return false;
        }

        if (File.Exists(path))
        {
            return true;
        }

        RememberMissing(cacheKey);
        path = null;
        return false;
    }

    public Task WriteThroughAsync(string cacheKey, Uri source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (TryGetPath(cacheKey, out _))
        {
            return Task.CompletedTask;
        }

        // 同一个键可能在一次滚动里被请求很多次（虚拟化容器反复要同一张图）。
        // 按 key 去重，保证同时只下一份。
        // 先发布 Lazy 再启动下载。GetOrAdd 的工厂可能并发执行，且同步失败可能早于条目插入。
        // 两者都不能留下重复下载或已完成任务，让后续请求永久复用一次失败。
        return _inFlight.GetOrAdd(cacheKey, key => new Lazy<Task>(
            () => DownloadAsync(key, source, cancellationToken), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    public Task<CoverCacheUsage> MeasureAsync(CancellationToken cancellationToken = default)
    {
        var (bytes, count) = Scan();
        _totalBytes = bytes;
        return Task.FromResult(new CoverCacheUsage(bytes, count));
    }

    public Task<CoverCacheUsage> PruneAsync(CancellationToken cancellationToken = default)
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            _totalBytes = 0;
            return Task.FromResult(new CoverCacheUsage(0, 0));
        }

        var files = new DirectoryInfo(Directory)
            .EnumerateFiles("*.bin")
            .OrderBy(file => file.LastWriteTimeUtc)
            .ToList();

        long total = files.Sum(file => file.Length);
        var removed = 0;

        foreach (var file in files)
        {
            if (total <= CapacityBytes)
            {
                break;
            }

            try
            {
                var length = file.Length;
                file.Delete();
                total -= length;
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 删不掉就跳过，继续删后面的 —— 为一张图让整轮淘汰失败不值得。
            }
        }

        _totalBytes = total;
        _logger.LogDebug("封面缓存淘汰：删了 {Removed} 个，剩 {Remaining} 字节", removed, total);

        return Task.FromResult(new CoverCacheUsage(total, files.Count - removed));
    }

    public Task<int> ClearAsync(CancellationToken cancellationToken = default)
    {
        var removed = 0;

        if (System.IO.Directory.Exists(Directory))
        {
            // 连 .tmp 一起清：半截文件留着没有意义。
            foreach (var file in new DirectoryInfo(Directory).EnumerateFiles())
            {
                try
                {
                    file.Delete();
                    removed++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // 同上：单个删不掉不影响整体。
                }
            }
        }

        _totalBytes = 0;
        _missing.Clear();
        _logger.LogInformation("封面缓存已清空，删了 {Removed} 个文件", removed);

        return Task.FromResult(removed);
    }

    public void Dispose()
    {
        _http.Dispose();
        _gate.Dispose();
    }

    private string PathFor(string cacheKey) => Path.Combine(Directory, cacheKey + ".bin");

    private void RememberMissing(string cacheKey)
    {
        if (_missing.Count >= MissingMemoLimit)
        {
            _missing.Clear();
        }

        _missing[cacheKey] = 0;
    }

    private (long Bytes, int Count) Scan()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return (0, 0);
        }

        long bytes = 0;
        var count = 0;

        foreach (var file in new DirectoryInfo(Directory).EnumerateFiles("*.bin"))
        {
            bytes += file.Length;
            count++;
        }

        return (bytes, count);
    }

    /// <remarks>
    /// <b>整段吞异常。</b> 调用方是界面层的 fire-and-forget，漏出去会变成未观察任务异常。
    /// 下载失败不是错误状态 —— 图照样从网络显示，只是这次没缓存下来。
    /// </remarks>
    private async Task DownloadAsync(string cacheKey, Uri source, CancellationToken cancellationToken)
    {
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using var response = await _http
                    .GetAsync(source, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogDebug("封面下载被拒（{Status}）：{Url}", (int)response.StatusCode, source);
                    RememberMissing(cacheKey);
                    return;
                }

                var bytes = await response.Content
                    .ReadAsByteArrayAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (bytes.Length == 0)
                {
                    RememberMissing(cacheKey);
                    return;
                }

                System.IO.Directory.CreateDirectory(Directory);

                // 先写 .tmp 再原子替换：读者永远看不到半截文件
                // （半截 = 解码失败 = 退回网络，静默退化，很难查）。
                var destination = PathFor(cacheKey);
                var temporary = destination + ".tmp";
                await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
                File.Move(temporary, destination, overwrite: true);

                _missing.TryRemove(cacheKey, out _);
                Interlocked.Add(ref _totalBytes, bytes.Length);

                if (Volatile.Read(ref _totalBytes) > CapacityBytes)
                {
                    // 淘汰放到后台：写穿本身是 fire-and-forget，不该为了删旧文件多等一轮 IO。
                    _ = PruneAsync(CancellationToken.None);
                }
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "封面写入磁盘缓存失败：{Url}", source);
        }
        finally
        {
            _inFlight.TryRemove(cacheKey, out _);
        }
    }
}
