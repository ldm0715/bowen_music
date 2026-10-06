using System.Security.Cryptography;
using System.Text;

namespace Bodian.Core.Media;

/// <summary>
/// 封面磁盘缓存的键。
/// </summary>
public static class CoverCacheKey
{
    /// <summary>
    /// 键 = <c>SHA256(改写后的绝对 URL + "\n" + 解码边长)</c>，小写十六进制。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>必须把边长算进键里。</b> 同一张封面在列表里按 256 取、在播放条上按 1024 取，
    /// CDN 是按尺寸段返回不同字节的；键里漏掉边长就会把大图位置填上小图，表现为发糊。
    /// </para>
    /// <para>
    /// <b>用 URL 的改写结果而不是原始地址</b>：改写后才是真正去取的那一份
    /// （见 <see cref="CoverArtUrl"/>），拿原始地址当键会让两处指向同一张图的请求算成两个键。
    /// 原始地址里的签名参数会变，也不适合当键。
    /// </para>
    /// </remarks>
    public static string For(Uri rewrittenUrl, int decodePixels)
    {
        ArgumentNullException.ThrowIfNull(rewrittenUrl);

        var bytes = Encoding.UTF8.GetBytes($"{rewrittenUrl.AbsoluteUri}\n{decodePixels}");
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
