using System.Globalization;
using Bodian.Core.Api;

namespace Bodian.Core.Services;

/// <summary>
/// 客户端本地拼出来的分享链接。
/// </summary>
/// <remarks>
/// <para>
/// <b>分享没有服务端接口</b>，链接是按内容类型做字符串插值拼出来的
/// （<c>allin/share/share_utils.dart::buildNowWebPageLink</c>，见文档 2.8）。
/// 所以这里不需要网络，也不该出现在 <c>BodianApi</c> 里。
/// </para>
/// <para>
/// <see cref="Endpoints.ShareHost"/> 在官方客户端里可被远程配置覆盖，本项目不实现远程覆盖。
/// </para>
/// </remarks>
public static class ShareLinks
{
    /// <summary>
    /// 歌曲分享链接：<c>playMusic.html?uid=&lt;分享者&gt;&amp;musicId=&lt;歌曲&gt;&amp;opusId=</c>。
    /// </summary>
    /// <param name="musicId">曲目 id。</param>
    /// <param name="uid">
    /// <b>分享者自己</b>的 uid（不是曲目作者的）；未登录时传 <see cref="BodianSession.AnonymousUid"/>。
    /// </param>
    /// <remarks>
    /// <para>
    /// <b><c>opusId</c> 留空</b>：曲目模型里没有这个字段，且文档记它「分享歌曲时是否为空、
    /// 为空时链接能否落地，未确认」。这里按官方模板原样保留这个空参数位，
    /// <b>该链接能否落地尚未验证</b>，见 <c>docs/like-share.md</c>。
    /// </para>
    /// <para>
    /// 域名与路径都是 ASCII，只有 <paramref name="uid"/> 是外部输入，所以只对它做转义。
    /// </para>
    /// </remarks>
    public static string BuildTrackLink(long musicId, string uid)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(musicId);
        ArgumentNullException.ThrowIfNull(uid);
        var escapedUid = Uri.EscapeDataString(uid);
        return string.Create(CultureInfo.InvariantCulture,
            $"{Endpoints.ShareHost}playMusic.html?uid={escapedUid}&musicId={musicId}&opusId=");
    }

    /// <summary>
    /// 歌手分享链接：<c>singer.html?uid=&lt;分享者&gt;&amp;singerId=&lt;歌手&gt;</c>。
    /// </summary>
    /// <param name="artistId">歌手 id。</param>
    /// <param name="uid">
    /// <b>分享者自己</b>的 uid；未登录时传 <see cref="BodianSession.AnonymousUid"/>。
    /// </param>
    /// <remarks>
    /// <para>
    /// 参数名是驼峰的 <c>singerId</c>，与专辑那条的全小写 <c>albumid</c> 不一样 ——
    /// 两条都逐字照抄官方模板（<c>docs/bodian-api-reference.md</c> §2.8 的链接模板表），
    /// <b>不要顺手统一</b>，改错了只会得到一条打不开的链接，没有任何一处会报错。
    /// </para>
    /// <para>
    /// <b>歌手也没有分享上报</b>：<c>shareSource</c> 取 <c>1</c> 已实测就是歌手
    /// （同一个 <c>sourceId</c> 在 <c>0</c> / <c>1</c> 下分别返回歌名与歌手名），
    /// 但这一轮只做复制链接、与专辑一致，所以不涨分享数。要加时值不用再探。
    /// </para>
    /// </remarks>
    public static string BuildArtistLink(long artistId, string uid)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(artistId);
        ArgumentNullException.ThrowIfNull(uid);
        var escapedUid = Uri.EscapeDataString(uid);
        return string.Create(CultureInfo.InvariantCulture,
            $"{Endpoints.ShareHost}singer.html?uid={escapedUid}&singerId={artistId}");
    }

    /// <summary>
    /// 专辑分享链接：<c>album.html?uid=&lt;分享者&gt;&amp;albumid=&lt;专辑&gt;</c>。
    /// </summary>
    /// <param name="albumId">专辑 id。</param>
    /// <param name="uid">
    /// <b>分享者自己</b>的 uid；未登录时传 <see cref="BodianSession.AnonymousUid"/>。
    /// </param>
    /// <remarks>
    /// <para>
    /// 参数名是 <b><c>albumid</c> 全小写</b>，与歌曲链接的 <c>musicId</c> 驼峰写法不同 ——
    /// 官方模板就是这么拼的（<c>docs/bodian-api-reference.md</c> §2.8 的链接模板表），
    /// 改成驼峰会得到一条打不开的链接，所以这里逐字照抄。
    /// </para>
    /// <para>
    /// <b>专辑没有分享上报</b>：<c>service/share/text</c> 的 <c>shareSource</c>
    /// 只实测过 <c>0</c>（歌曲）与 <c>1</c>（歌手），专辑取什么值没有证据。
    /// 所以这里只拼链接，不涨分享数 —— 不是漏做。
    /// </para>
    /// </remarks>
    public static string BuildAlbumLink(long albumId, string uid)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(albumId);
        ArgumentNullException.ThrowIfNull(uid);
        var escapedUid = Uri.EscapeDataString(uid);
        return string.Create(CultureInfo.InvariantCulture,
            $"{Endpoints.ShareHost}album.html?uid={escapedUid}&albumid={albumId}");
    }

    /// <summary>
    /// 歌单分享链接：<c>collection.html?uid=&lt;分享者&gt;&amp;playlistId=&lt;歌单&gt;&amp;source=&lt;s&gt;</c>。
    /// </summary>
    /// <param name="playlistId">歌单 id。</param>
    /// <param name="uid">
    /// <b>分享者自己</b>的 uid；未登录时传 <see cref="BodianSession.AnonymousUid"/>。
    /// </param>
    /// <param name="source">
    /// 歌单来源，**必须原样传进详情页的那个值**（账号歌单 <c>5</c>、发现页 <c>13</c> 等）。
    /// </param>
    /// <remarks>
    /// <para>
    /// <b><c>source</c> 是歌单这条模板独有的参数</b>，其余三条分享链接都没有它。
    /// 因此**不要图省事写死 <c>4</c>** —— 同一个歌单在不同 source 下是不同的东西，
    /// 猜错只会得到一条打不开的链接，没有任何一处会报错。
    /// </para>
    /// <para>
    /// <b>歌单没有分享上报</b>：<c>service/share/text</c> 的 <c>shareSource</c>
    /// 只实测过 <c>0</c>（歌曲）与 <c>1</c>（歌手），歌单取什么值没有证据。
    /// 所以这里只拼链接，不涨分享数 —— 不是漏做。
    /// </para>
    /// </remarks>
    public static string BuildPlaylistLink(long playlistId, string uid, int source)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(playlistId);
        ArgumentNullException.ThrowIfNull(uid);
        var escapedUid = Uri.EscapeDataString(uid);
        return string.Create(CultureInfo.InvariantCulture,
            $"{Endpoints.ShareHost}collection.html?uid={escapedUid}&playlistId={playlistId}&source={source}");
    }
}
