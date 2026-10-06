namespace Bodian.Core.Services.Abstractions;

/// <summary>封面磁盘缓存的占用。</summary>
/// <param name="Bytes">目录内文件的总字节数。</param>
/// <param name="FileCount">文件数。</param>
public sealed record CoverCacheUsage(long Bytes, int FileCount);

/// <summary>
/// 封面图片的磁盘缓存。
/// </summary>
/// <remarks>
/// <para>
/// <b>存的是 CDN 返回的原样字节（编码后），不是解码后的像素。</b> 解码后大三个数量级，
/// 而且界面层「先设解码尺寸再请求图片」这条约定正是靠原样字节保留的
/// （见 <c>docs/performance.md</c>）。
/// </para>
/// <para>
/// 它是内存缓存（界面层的 <c>CoverImageCache</c>）的**持久化后盾**，不是替代：
/// 进程内的命中仍走内存，磁盘只在内存未命中时兜底。
/// </para>
/// </remarks>
public interface ICoverDiskCache
{
    /// <summary>缓存目录。</summary>
    string Directory { get; }

    /// <summary>容量上限。</summary>
    long CapacityBytes { get; }

    /// <summary>命中时给出本地文件全路径。<b>不碰网络</b>。</summary>
    bool TryGetPath(string cacheKey, out string? path);

    /// <summary>
    /// 写穿：后台下载并落盘。已经在下载或已经存在时直接返回。
    /// </summary>
    /// <remarks>
    /// <b>永不抛异常</b>（网络失败、磁盘满、CDN 403 一律自己吞掉并记日志）——
    /// 它由界面层 fire-and-forget 调用，漏出去会变成未观察任务异常，
    /// 被记成 Critical 日志。
    /// </remarks>
    Task WriteThroughAsync(string cacheKey, Uri source, CancellationToken cancellationToken = default);

    /// <summary>统计当前占用。</summary>
    Task<CoverCacheUsage> MeasureAsync(CancellationToken cancellationToken = default);

    /// <summary>按最后写入时间从旧到新淘汰，直到不超上限。</summary>
    Task<CoverCacheUsage> PruneAsync(CancellationToken cancellationToken = default);

    /// <summary>清空。返回删掉的文件数。</summary>
    Task<int> ClearAsync(CancellationToken cancellationToken = default);
}
