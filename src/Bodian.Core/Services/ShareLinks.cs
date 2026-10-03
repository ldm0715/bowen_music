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
}
