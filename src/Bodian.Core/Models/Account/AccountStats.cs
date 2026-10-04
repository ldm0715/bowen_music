namespace Bodian.Core.Models.Account;

/// <summary>
/// 账号的社交计数。
/// </summary>
/// <remarks>
/// 每一项都可能为 <c>null</c>：服务端没给这个键，或这次读取失败。<b>不要当成 0。</b>
/// </remarks>
public sealed record AccountMetadata(
    long? FollowCount,
    long? FansCount,
    long? FollowArtistCount,
    long? Praised);

/// <summary>
/// 听歌统计。
/// </summary>
/// <remarks>
/// <see cref="PlaySeconds"/> 的**单位是秒**（实测确认），换算与文案见
/// <see cref="ListenTimeLabel"/>。
/// </remarks>
public sealed record AccountPlayData(long? PlayCount, long? PlaySeconds);

/// <summary>
/// 当前账号的会员档位与到期时间，实时取自 <c>ucenter/users/pub/{uid}</c>。
/// </summary>
/// <remarks>
/// <b>只用于展示</b>，播放权限一律以服务端 <c>checkRight</c> 为准。
/// 与登录时存进凭据的那份快照相比，这份是「点开就看得到最新」的。
/// </remarks>
public sealed record AccountVipInfo(VipBadgeKind Badge, DateTimeOffset? ExpiresAt);
