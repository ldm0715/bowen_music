namespace Bodian.Core.Models.Account;

/// <summary>
/// 会员徽标的档位。与官方客户端 vipLogo 的分支一一对应。
/// </summary>
/// <remarks>
/// <b>只用于展示。</b>能不能播永远由服务端的 <c>checkRight</c> 裁决，这里连「是不是会员」
/// 都不作为权限依据。档位的判读在 <c>Bodian.Core.Api.VipStatus.ResolveBadge</c>。
/// </remarks>
public enum VipBadgeKind
{
    /// <summary>不是会员。界面不画徽标。</summary>
    None,

    /// <summary>福利会员。活动赠送或免费领取的会员（<c>vipType==2</c>）。</summary>
    Welfare,

    /// <summary>畅听会员（<c>vipType==1</c> 且 <c>payVipType==2</c>）。</summary>
    Standard,

    /// <summary>大会员（<c>vipType==1</c> 的其余情形）。</summary>
    Big,
}
