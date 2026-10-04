using System.Text.Json.Serialization;

namespace Bodian.Core.Api.Dto;

/// <summary>
/// <c>service/users/{uid}/metadata</c> 的 <c>data</c>。
/// </summary>
/// <remarks>
/// 字段名来自官方客户端的 <c>UserPraiseAndFansCount::fromJson</c>，
/// 2026-10-04 用探针实测确认（<c>fixtures/users-metadata.json</c>，桌面签名直接通）。
/// 响应里还有 <c>resourceCount</c> 与 <c>privateFriendsCnt</c>，本项目不用，不建。
/// <para>
/// <b>全部声明成可空</b>：这几个键名原本只有静态证据，可空时猜错会得到 <c>null</c>
/// （而不是 0），界面据此显示「—」。这条防线留着 —— 别的地方复用这个 DTO 时同样适用。
/// </para>
/// </remarks>
internal sealed class UserMetadataDto
{
    [JsonPropertyName("followCount")] public long? FollowCount { get; init; }

    [JsonPropertyName("fansCount")] public long? FansCount { get; init; }

    [JsonPropertyName("followArtistCount")] public long? FollowArtistCount { get; init; }

    /// <summary>获赞数。与歌单的 <c>praise</c> 同义，不是「点赞过多少」。</summary>
    [JsonPropertyName("praised")] public long? Praised { get; init; }
}

/// <summary>
/// <c>ucenter/users/pub/{uid}</c> 的 <c>data</c>。<b>只声明本项目用得到的字段。</b>
/// </summary>
/// <remarks>
/// 完整响应与登录响应同构（另有 <c>userInfo</c> / <c>payRights</c> / <c>bid</c> / <c>ipCity</c> …），
/// 这里只取 <c>payInfo</c> —— 用来实时重算会员档位与到期时间。
/// <c>userInfo</c> 里的昵称与头像本项目不从这条取（登录时已经有了）。
/// </remarks>
internal sealed class UserPubDto
{
    [JsonPropertyName("payInfo")] public AccountPayInfoDto? PayInfo { get; init; }
}

/// <summary>
/// <c>ucenter/playdata/user_data</c> 的 <c>data</c>。
/// </summary>
/// <remarks>
/// 官方客户端把响应的 <c>data</c> 子树直接喂给 <c>UserVisitData::fromJson</c>，
/// 所以 <c>data</c> 就是 <c>{playcnt, playTime}</c>，不套壳。
/// 2026-10-04 探针实测确认（<c>fixtures/playdata-user-data.json</c>）。
/// </remarks>
internal sealed class UserPlayDataDto
{
    /// <summary>播放次数。</summary>
    [JsonPropertyName("playcnt")] public long? PlayCount { get; init; }

    /// <summary>
    /// 听歌时长，<b>单位是秒</b>（实测 <c>186344 / 1110 ≈ 168</c> 秒/首），
    /// 见 <c>Bodian.Core.Models.Account.ListenTimeLabel</c>。
    /// </summary>
    [JsonPropertyName("playTime")] public long? PlaySeconds { get; init; }
}
