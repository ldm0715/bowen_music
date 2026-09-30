using System.Globalization;

namespace Bodian.Core.Media;

/// <summary>
/// 把酷我封面地址规范成系统媒体控件（SMTC）能吃的 JPEG 地址。
/// </summary>
/// <remarks>
/// <para>
/// <b>SMTC 不认 webp</b>，而曲目详情给的封面恰好是 webp（搜索列表给的是 jpg）。
/// 但**不需要转码**：酷我 CDN 是按需生成尺寸与格式的，同一路径把 <c>.webp</c> 换成 <c>.jpg</c>
/// 就能拿到真的 JPEG（实测 200 <c>image/jpeg</c>）。
/// </para>
/// <para>
/// 这条比「下载字节 + WIC 转码」好在一个地方：<b>Win10 不预装 WebP 编解码器</b>
/// （要装商店的 WebP Image Extensions，Win11 才预装），转码方案会在开发机上一直成功、
/// 换台干净 Win10 就静默失败。URL 改写零依赖、零字节处理。
/// </para>
/// <para>
/// <b>只改认识的形状</b>：不是 <c>.kuwo.cn</c>、或路径不是 <c>/star/albumcover/&lt;尺寸&gt;/</c> 的，
/// 一律原样返回。改写是建立在实测过的样本上的，不认识的地址不猜。
/// </para>
/// </remarks>
public static class CoverArtUrl
{
    /// <summary>目标边长。120px 在媒体浮层里偏糊。</summary>
    public const int PreferredSize = 500;

    private const string AlbumCoverSegment = "/star/albumcover/";

    /// <summary>
    /// 转成 SMTC 可用的 JPEG 地址。转不了时返回原地址（失败退化成「没有缩略图」，不影响其它字段）。
    /// </summary>
    /// <param name="cover">原始封面地址，可为 <c>null</c>。</param>
    /// <param name="size">目标边长。</param>
    public static Uri? Jpeg(Uri? cover, int size = PreferredSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);

        if (cover is null || cover.Scheme is not ("http" or "https"))
        {
            return cover;
        }

        if (!cover.Host.EndsWith(".kuwo.cn", StringComparison.OrdinalIgnoreCase))
        {
            return cover;
        }

        var path = cover.AbsolutePath;

        if (!TryReplaceSize(path, size, out var rewritten))
        {
            return cover;
        }

        if (rewritten.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
        {
            rewritten = string.Concat(rewritten.AsSpan(0, rewritten.Length - ".webp".Length), ".jpg");
        }

        var builder = new UriBuilder(cover) { Path = rewritten };

        return builder.Uri;
    }

    /// <summary>把尺寸段换成目标边长。路径形状不认识时返回 <c>false</c>，不改。</summary>
    private static bool TryReplaceSize(string path, int size, out string rewritten)
    {
        rewritten = path;

        if (!path.StartsWith(AlbumCoverSegment, StringComparison.Ordinal))
        {
            return false;
        }

        // 尺寸段 = 「一串数字 + /」，后面才是 albumcover 的其余路径。
        var tail = path.AsSpan(AlbumCoverSegment.Length);
        var slash = tail.IndexOf('/');

        if (slash <= 0 || !int.TryParse(tail[..slash], CultureInfo.InvariantCulture, out _))
        {
            return false;
        }

        rewritten = string.Concat(
            AlbumCoverSegment,
            size.ToString(CultureInfo.InvariantCulture),
            tail[slash..]);

        return true;
    }
}
