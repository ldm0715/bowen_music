using Bodian.Core.Api.Dto;
using Bodian.Core.Models.Account;

namespace Bodian.Core.Api;

/// <summary>
/// 从账号授权信息里判读「是不是会员、什么时候到期」。
/// </summary>
/// <remarks>
/// <para>
/// 为什么要单独一步：服务端给了一堆同义字段（<c>isVip</c> / <c>isVipBoolean</c> /
/// <c>vipType</c> / <c>actVipType</c> / <c>payVipType</c> / <c>expireDate</c> /
/// <c>payExpireDate</c> / <c>actExpireDate</c> …）。
/// <b>实测样本里 <c>isVip=1</c> 而 <c>expireDate=0</c>，真正生效的是
/// <c>actVipType=1</c> + <c>actExpireDate</c></b>（活动赠送的会员），
/// 所以挑单一字段读一定会判错。
/// </para>
/// <para>
/// <b>这只是界面展示用的快照</b>，不参与任何权限判断 ——
/// 能不能播永远由服务端的 <c>checkRight</c> 裁决。
/// </para>
/// </remarks>
internal static class VipStatus
{
    /// <summary>到期时间戳的合理上限。超出这个范围的值按「不可信」丢弃。</summary>
    private const long MaxPlausibleUnixMs = 4102444800000;   // 2100-01-01

    /// <summary>判读会员状态。</summary>
    /// <returns>是否会员，以及最晚的到期时刻（没有可用到期字段时为 <c>null</c>）。</returns>
    public static (bool IsVip, DateTimeOffset? ExpiresAt) Resolve(AccountPayInfoDto? payInfo)
    {
        if (payInfo is null)
        {
            return (false, null);
        }

        var isVip = payInfo.IsVipBoolean
            || payInfo.IsVip > 0
            || payInfo.IsPayVipBoolean
            || payInfo.IsBigVipBoolean
            || payInfo.IsActVipBoolean
            || payInfo.IsBigPayVipBoolean
            || payInfo.IsCtVipBoolean
            || payInfo.VipType > 0
            || payInfo.PayVipType > 0
            || payInfo.ActVipType > 0;

        return (isVip, LatestExpiry(payInfo));
    }

    /// <summary>
    /// 判读该用哪一个会员徽标。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 分支结构照官方客户端 <c>vipLogo</c>（<c>vip_public_util.dart:0x104d70c</c>）：
    /// <c>x3 == 1 ? (x2 == 2 ? ct : crown) : (x3 == 2 ? free : gray)</c>，
    /// 其中 <c>x3 = vipType</c>、<c>x2 = payVipType</c>。
    /// </para>
    /// <para>
    /// <b>档位名是实测校准过的</b>（见 <c>reverse/findings/14</c> §1）：
    /// 本机账号（<c>vipType=1 / payVipType=1 / isBigVipBoolean=true</c>）在官方客户端是**大会员**，
    /// 所以 <c>vipType==1</c> 这个 crown 分支就是大会员；<c>vipType==2</c> 的活动会员是福利会员。
    /// 剩下 <c>payVipType==2</c> 的 ct 分支归畅听会员（该档本机没有可对照的账号）。
    /// </para>
    /// <para>
    /// <b>是会员但档位认不出来时退回大会员，不画灰标</b>：灰标在安卓那边表达的是
    /// 「没有会员」，拿它去标一个确实是会员的账号会读成相反的结论。退回 crown 分支
    /// 也与反编译里「默认走 crown」的结构一致。
    /// </para>
    /// </remarks>
    public static VipBadgeKind ResolveBadge(AccountPayInfoDto? payInfo)
    {
        if (payInfo is null || !Resolve(payInfo).IsVip)
        {
            return VipBadgeKind.None;
        }

        // 与安卓同序：先分 vipType，再看 payVipType 是不是畅听那一档。
        if (payInfo.VipType == 1)
        {
            return payInfo.PayVipType == 2 ? VipBadgeKind.Standard : VipBadgeKind.Big;
        }

        return payInfo.VipType == 2 ? VipBadgeKind.Welfare : VipBadgeKind.Big;
    }

    /// <summary>
    /// 在各类会员的到期字段里取最晚的那个。
    /// </summary>
    /// <remarks>
    /// 取最晚而不是取第一个非零：这些字段对应不同种类的会员（普通 / 付费 / 大会员 / 活动），
    /// 用户可能同时持有几种，界面上显示最晚的那个才不会低估。
    /// </remarks>
    private static DateTimeOffset? LatestExpiry(AccountPayInfoDto payInfo)
    {
        ReadOnlySpan<long> candidates =
        [
            payInfo.ExpireDate,
            payInfo.PayExpireDate,
            payInfo.BigExpireDate,
            payInfo.BigPayExpireDate,
            payInfo.CtExpireDate,
            payInfo.CtPayExpireDate,
            payInfo.ActExpireDate,
        ];

        DateTimeOffset? latest = null;

        foreach (var raw in candidates)
        {
            // 0 与明显越界的值都不作数：实测里大量 0 是「没有这种会员」的表达。
            if (raw <= 0 || raw > MaxPlausibleUnixMs)
            {
                continue;
            }

            var moment = DateTimeOffset.FromUnixTimeMilliseconds(raw);

            if (latest is null || moment > latest)
            {
                latest = moment;
            }
        }

        return latest;
    }
}
