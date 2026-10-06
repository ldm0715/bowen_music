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

    /// <summary>
    /// 接上磁盘层。由 <c>App</c> 在建窗口之前调一次。
    /// </summary>
    /// <remarks>
    /// 没接上时行为与只有内存缓存时完全一致 —— 少一层兜底，但不会出错。
    /// </remarks>
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

        // 先指定解码尺寸，再开始请求，避免先解码原图再缩小。
        // ★ 磁盘那份存的也是**原样字节**，所以这条约定在两层的任一路上都成立。
        var image = new BitmapImage { DecodePixelWidth = pixels };

        var cacheKey = CoverCacheKey.For(source, pixels);
        if (_disk is not null && _disk.TryGetPath(cacheKey, out var path) && path is not null)
        {
            _ = LoadFromDiskAsync(image, path, source, cacheKey);
        }
        else
        {
            image.UriSource = source;

            // 写穿是后台的，且自己吞掉所有异常。
            _ = _disk?.WriteThroughAsync(cacheKey, source);
        }

        var decodedBytes = (long)pixels * pixels * 4;
        var entry = Recency.AddFirst(new Entry(key, image, decodedBytes));
        _estimatedDecodedBytes += decodedBytes;
        Entries.Add(key, entry);
        while ((Entries.Count > Capacity || _estimatedDecodedBytes > DecodedByteCapacity) && Recency.Last is { } oldest)
        {
            Recency.RemoveLast();
            Entries.Remove(oldest.Value.Key);
            _estimatedDecodedBytes -= oldest.Value.EstimatedBytes;
        }
        return image;
    }

    /// <remarks>
    /// <b>流必须在 <c>SetSourceAsync</c> 完成之后才释放</b>（<c>using</c> 在这一句之后才生效），
    /// 提前释放会得到一张空图。读失败说明磁盘那份坏了：删掉它并退回网络，
    /// 否则这张封面会一直显示不出来。
    /// </remarks>
    private static async Task LoadFromDiskAsync(BitmapImage image, string path, Uri source, string cacheKey)
    {
        try
        {
            using var stream = File.OpenRead(path);
            await image.SetSourceAsync(stream.AsRandomAccessStream());
        }
        catch (Exception)
        {
            TryDelete(path);

            // 无论有没有磁盘层，退回网络这一句都要执行，否则这张封面就是空白。
            image.UriSource = source;
            _ = _disk?.WriteThroughAsync(cacheKey, source);
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
            // 删不掉就算了：下次 TryGetPath 仍会命中这个坏文件，但上面那条退回网络的路径会救回来。
        }
    }

    private sealed record Entry((Uri Uri, int Pixels) Key, BitmapImage Image, long EstimatedBytes);
}
