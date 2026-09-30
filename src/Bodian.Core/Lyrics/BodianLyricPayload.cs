using System.Text;
using Bodian.Core.Api;

namespace Bodian.Core.Lyrics;

/// <summary>
/// 歌词请求的构造与响应解码。
/// </summary>
/// <remarks>
/// <para>
/// 端点是歌词站 <c>https://mlyric.kuwo.cn/mobi.s?f=bodian&amp;q=&lt;base64&gt;</c>，
/// **不走 API 主站的 <c>/api</c> 前缀，也不签名**。
/// </para>
/// <para>
/// <b>只需要一次 Base64 解码。</b> 老酷我歌词端点（<c>f=web</c>）那套
/// <c>tp=content</c> 头剥离 → zlib inflate → Base64 → <c>yeelion</c> 循环 XOR 的解密链
/// **不适用于波点端点**，搬过来会白干一天。见 <c>bodian-api-reference.md</c> 2.6 与 7.5 节。
/// </para>
/// </remarks>
public static class BodianLyricPayload
{
    /// <summary><c>lrcx=1</c> 要逐字版，<c>lrcx=0</c> 要逐行版。</summary>
    public const int WordByWord = 1;

    /// <summary>逐行版。</summary>
    public const int LineByLine = 0;

    /// <summary>
    /// 请求 payload（Base64 之前的那段明文）。
    /// </summary>
    /// <remarks>
    /// <c>rid</c> 填的是**波点的 musicId**，不是酷我 rid。
    /// </remarks>
    public static string BuildRequestPayload(long musicId, int lrcx)
        => $"type=lyric&req=2&lrcx={lrcx}&rid={musicId.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
           + "&songname=&artist=&corp=kuwo&fromchannel=bodian";

    /// <summary>完整的歌词 URL。</summary>
    public static Uri BuildRequestUri(long musicId, int lrcx)
    {
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(BuildRequestPayload(musicId, lrcx)));
        return new Uri($"{Endpoints.LyricHost}/{Endpoints.LyricPath}?f=bodian&q={Uri.EscapeDataString(base64)}");
    }

    /// <summary>
    /// 把响应 <c>data.content</c> 的 Base64 解成歌词文本。
    /// </summary>
    /// <remarks>
    /// <b>只有这一步。</b> 没有二次解码，没有解压，没有 XOR。
    /// <para>空串是合法输入：该曲没有逐字轨时服务端就会返回空串，**业务码仍然是 200**。</para>
    /// </remarks>
    public static string DecodeContent(string? base64Content)
    {
        if (string.IsNullOrWhiteSpace(base64Content))
        {
            return string.Empty;
        }

        // 容忍换行：Base64 里夹空白虽然不该出现，但比在这里抛异常划算。
        var cleaned = new string(base64Content.Where(c => !char.IsWhiteSpace(c)).ToArray());

        return Encoding.UTF8.GetString(Convert.FromBase64String(cleaned));
    }
}
