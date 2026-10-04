using System.Globalization;

namespace Bodian.Core.Models.Account;

/// <summary>
/// 会员到期时间的文案。
/// </summary>
/// <remarks>
/// 数据源是登录响应 <c>payInfo</c> 里那七个到期字段取最晚的一个（见
/// <c>Bodian.Core.Api.VipStatus.LatestExpiry</c>），**不需要额外接口**。
/// 老凭据里没有这个字段，读出来是 <c>null</c>，此时返回空串由调用方收掉。
/// </remarks>
public static class VipExpiryLabel
{
    /// <summary>没有到期时间（非会员、或老凭据里没存）时返回空串。</summary>
    /// <remarks>按**本地时间**出日期 —— 用户看的是「我这边哪天到期」。</remarks>
    public static string Format(DateTimeOffset? expiresAt) =>
        expiresAt is { } moment
            ? string.Create(CultureInfo.InvariantCulture, $"{moment.ToLocalTime():yyyy-MM-dd} 到期")
            : "";
}
