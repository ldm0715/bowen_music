using Bodian.Core.Api.Dto;

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
