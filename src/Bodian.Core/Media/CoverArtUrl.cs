using System.Globalization;
using System.Text.RegularExpressions;

namespace Bodian.Core.Media;

/// <summary>
/// 把酷我封面与歌手头像规范成指定尺寸的 JPEG 地址，并提供已知失效地址的回退。
/// </summary>
/// <remarks>
/// <para>
/// <b>SMTC 不认 webp</b>，而曲目详情给的封面恰好是 webp（搜索列表给的是 jpg）。
/// 酷我 CDN 按需生成尺寸与格式，同一路径把 <c>.webp</c> 换成 <c>.jpg</c> 即可取得 JPEG。
/// </para>
/// <para>
/// 这条比「下载字节 + WIC 转码」好在一个地方：<b>Win10 不预装 WebP 编解码器</b>
/// （要装商店的 WebP Image Extensions，Win11 才预装），转码方案会在开发机上一直成功、
/// 换台干净 Win10 就静默失败。URL 改写零依赖、零字节处理。
/// </para>
/// <para>
/// 只改写实测支持的 <c>/star/albumcover/&lt;尺寸&gt;/</c> 与
/// <c>/star/starheads/&lt;尺寸&gt;/</c>，其它地址保持原样。
/// 腾讯旧歌单封面的 <c>_o.jpg</c> 回退单独处理，酷我歌单的同名后缀仍然有效。
/// </para>
/// </remarks>
public static partial class CoverArtUrl
{
    /// <summary>目标边长。120px 在媒体浮层里偏糊。</summary>
    public const int PreferredSize = 500;

    private const string AlbumCoverSegment = "/star/albumcover/";
    private const string StarHeadsSegment = "/star/starheads/";

    /// <summary>转成指定尺寸的 JPEG 地址。不认识的地址原样返回。</summary>
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

        if (!TryReplaceSize(cover.AbsolutePath, size, out var rewritten))
        {
            return cover;
        }

        if (rewritten.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
        {
            rewritten = string.Concat(rewritten.AsSpan(0, rewritten.Length - ".webp".Length), ".jpg");
        }

        return new UriBuilder(cover) { Path = rewritten }.Uri;
    }

    /// <summary>
    /// 已知腾讯旧封面地址失败时，尝试数字 <c>0</c> 的版本。其它图床与路径没有此回退。
    /// </summary>
    public static Uri? Fallback(Uri? cover)
    {
        if (cover is null || cover.Scheme is not ("http" or "https")
            || !cover.Host.Equals("y.gtimg.cn", StringComparison.OrdinalIgnoreCase)
            || !TencentLegacyAlbumCover().IsMatch(cover.AbsolutePath))
        {
            return null;
        }

        var path = string.Concat(cover.AbsolutePath.AsSpan(0, cover.AbsolutePath.Length - "_o.jpg".Length), "_0.jpg");
        return new UriBuilder(cover) { Path = path }.Uri;
    }

    [GeneratedRegex(@"^/music/photo/album_[0-9]+/[0-9]+/[0-9]+_albumpic_[0-9]+_o\.jpg$", RegexOptions.CultureInvariant)]
    private static partial Regex TencentLegacyAlbumCover();

    /// <summary>把已知路径的尺寸段换成目标边长。</summary>
    private static bool TryReplaceSize(string path, int size, out string rewritten)
    {
        rewritten = path;
        var segment = path.StartsWith(AlbumCoverSegment, StringComparison.Ordinal) ? AlbumCoverSegment
            : path.StartsWith(StarHeadsSegment, StringComparison.Ordinal) ? StarHeadsSegment : null;

        if (segment is null)
        {
            return false;
        }

        var tail = path.AsSpan(segment.Length);
        var slash = tail.IndexOf('/');

        if (slash <= 0 || !int.TryParse(tail[..slash], NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            return false;
        }

        rewritten = string.Concat(segment, size.ToString(CultureInfo.InvariantCulture), tail[slash..]);
        return true;
    }
}
