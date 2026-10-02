using Bodian.Core.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Bodian.WinUI.Media;

/// <summary>UI 线程上的有界封面缓存；限制解码尺寸，复用虚拟化容器反复请求的相同图片。</summary>
internal static class CoverImageCache
{
    private const int Capacity = 128;
    private const long DecodedByteCapacity = 64L * 1024 * 1024;
    private static long _estimatedDecodedBytes;
    private static readonly Dictionary<(Uri Uri, int Pixels), LinkedListNode<Entry>> Entries = [];
    private static readonly LinkedList<Entry> Recency = new();

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
        var image = new BitmapImage { DecodePixelWidth = pixels, UriSource = source };
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

    private sealed record Entry((Uri Uri, int Pixels) Key, BitmapImage Image, long EstimatedBytes);
}
