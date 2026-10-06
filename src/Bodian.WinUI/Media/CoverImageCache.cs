using Bodian.Core.Media;
using Bodian.Core.Services.Abstractions;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Bodian.WinUI.Media;

/// <summary>
/// UI 线程上的有界封面缓存；限制解码尺寸，复用虚拟化容器反复请求的相同图片。
/// </summary>
/// <remarks>
/// <para>
/// <b>内存这一层（L1）背后还有一份磁盘缓存（L2）。</b> 这里命中就直接返回；
/// 没命中时先问磁盘，磁盘有就从本地文件读，没有才走网络并把字节写穿到磁盘。
/// 磁盘是内存的**持久化后盾**，不是替代 —— 进程内的反复请求仍然一次网络都不发。
/// </para>
/// <para>
/// <b><see cref="Get"/> 必须保持同步签名。</b> 它被 <c>Formats.CoverSource</c> 在
/// <c>x:Bind</c> 里同步调用，分布在十几个页面的模板中；改成返回 <c>Task</c> 要动全应用所有模板。
/// 磁盘命中那条路走 <see cref="BitmapImage.SetSourceAsync"/> —— 它允许图片先挂到
/// <c>Image.Source</c>、之后再填充，所以「先给控件、后有图」是安全的。
/// </para>
/// </remarks>
internal static class CoverImageCache
{
    private const int Capacity = 128;
    private const long DecodedByteCapacity = 64L * 1024 * 1024;

    private static long _estimatedDecodedBytes;
    private static ICoverDiskCache? _disk;
    private static readonly Dictionary<(Uri Uri, int Pixels), LinkedListNode<Entry>> Entries = [];
    private static readonly LinkedList<Entry> Recency = new();

    /// <summary>接上磁盘层。由 <c>App</c> 在建窗口之前调一次。</summary>
    public static void AttachDiskCache(ICoverDiskCache disk) => _disk = disk;

    /// <summary>丢掉内存这一层。已经赋给 <c>Image.Source</c> 的位图不受影响。</summary>
    public static void Clear()
    {
        Entries.Clear();
        Recency.Clear();
        _estimatedDecodedBytes = 0;
    }

    public static BitmapImage? Get(Uri? uri, int pixels)
    {
        if (uri is null) return null;
        var source = CoverArtUrl.Jpeg(uri, pixels)!;
        var key = (source, pixels);
        if (Entries.TryGetValue(key, out var cached))
        {
            Recency.Remove(cached);
            Recency.AddFirst(cached);
            return cached.Value.Image;
        }

        // 先指定解码尺寸，再开始请求，磁盘中的原样字节也遵循同一约定。
        var image = new BitmapImage { DecodePixelWidth = pixels };
        var fallback = CoverArtUrl.Fallback(source) ?? (source != uri ? uri : null);
        var entry = new Entry(key, image, (long)pixels * pixels * 4, fallback);
        image.ImageFailed += (_, _) => OnImageFailed(entry);

        // 先登记再发起加载：失败事件可能很快到达，必须能移除对应条目。
        var node = Recency.AddFirst(entry);
        Entries.Add(key, node);
        _estimatedDecodedBytes += entry.EstimatedBytes;
        while ((Entries.Count > Capacity || _estimatedDecodedBytes > DecodedByteCapacity) && Recency.Last is { } oldest)
        {
            Remove(oldest.Value);
        }

        Load(entry, source);
        return image;
    }

    private static void Load(Entry entry, Uri source)
    {
        var cacheKey = CoverCacheKey.For(source, entry.Key.Pixels);
        if (_disk is not null && _disk.TryGetPath(cacheKey, out var path) && path is not null)
        {
            entry.IsReadingDisk = true;
            _ = LoadFromDiskAsync(entry, path, source, cacheKey);
        }
        else
        {
            LoadFromNetwork(entry, source, cacheKey);
        }
    }

    private static void LoadFromNetwork(Entry entry, Uri source, string cacheKey)
    {
        entry.Image.UriSource = source;
        _ = _disk?.WriteThroughAsync(cacheKey, source);
    }

    private static void OnImageFailed(Entry entry)
    {
        // SetSourceAsync 的失败由磁盘读取任务处理，先重新请求同一地址。
        if (entry.IsReadingDisk) return;

        if (!entry.HasTriedFallback && entry.Fallback is { } fallback)
        {
            entry.HasTriedFallback = true;
            Load(entry, fallback);
            return;
        }

        // 终态失败不保留空位图；下次绑定可以重新加载，且本次不会无限重试。
        Remove(entry);
    }

    private static void Remove(Entry entry)
    {
        // 旧位图可能已被淘汰或清空；迟到的失败不能移除同地址的新位图。
        if (!Entries.TryGetValue(entry.Key, out var node) || !ReferenceEquals(node.Value, entry)) return;

        Entries.Remove(entry.Key);
        Recency.Remove(node);
        _estimatedDecodedBytes -= entry.EstimatedBytes;
    }

    /// <remarks>
    /// 流必须在 SetSourceAsync 完成后才释放。磁盘读取失败时回到同一地址的网络请求，
    /// 网络再失败才尝试备用地址，避免两条失败路径同时改写位图。
    /// </remarks>
    private static async Task LoadFromDiskAsync(Entry entry, string path, Uri source, string cacheKey)
    {
        try
        {
            using var stream = File.OpenRead(path);
            await entry.Image.SetSourceAsync(stream.AsRandomAccessStream());
            entry.IsReadingDisk = false;
        }
        catch (Exception)
        {
            entry.IsReadingDisk = false;
            TryDelete(path);
            LoadFromNetwork(entry, source, cacheKey);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 删不掉就算了，下次读取失败仍可退回网络。
        }
    }

    private sealed record Entry((Uri Uri, int Pixels) Key, BitmapImage Image, long EstimatedBytes, Uri? Fallback)
    {
        public bool IsReadingDisk { get; set; }
        public bool HasTriedFallback { get; set; }
    }
}
