using System.Text.Json.Serialization;

namespace Bodian.Core.Api.Dto;

/// <summary>
/// <c>ucenter/users/login</c> 的响应（扫码登录的最后一步）。
/// </summary>
/// <remarks>
/// 身份字段有多个且**必须两两一致**（<c>id</c> / <c>bid</c> / <c>userInfo.id</c>）——
/// 不一致说明会话身份错配，必须拒绝而不是采用其中一个。校验规则抽在
/// <c>Services/SessionIdentity</c> 里，是纯函数，用纯值输入单测。
/// <para>
/// <b>fixture 已脱敏</b>：<c>id</c> / <c>bid</c> / <c>userInfo.id</c> / <c>token</c> /
/// <c>nickname</c> / <c>headImg</c> 在 <c>fixtures/login-users-login.json</c> 里全是
/// <c>"&lt;redacted&gt;"</c>。所以身份一致性规则**不能用这份 fixture 测**，
/// 只能测未被脱敏的部分。这也是这几个 id 字段要用 <see cref="LenientInt64Converter"/> 的原因。
/// </para>
/// </remarks>
internal sealed class LoginResultDto
{
    [JsonPropertyName("id")]
    [JsonConverter(typeof(LenientInt64Converter))]
    public long? Id { get; init; }

    [JsonPropertyName("bid")]
    [JsonConverter(typeof(LenientInt64Converter))]
    public long? Bid { get; init; }

    /// <summary>实测登录响应里**没有**这个字段，保留以防将来出现。</summary>
    [JsonPropertyName("uid")]
    [JsonConverter(typeof(LenientInt64Converter))]
    public long? Uid { get; init; }

    /// <summary>会话 token。**凭据字段，绝不入日志。**</summary>
    [JsonPropertyName("token")] public string? Token { get; init; }

    [JsonPropertyName("userInfo")] public UserInfoDto? UserInfo { get; init; }

    /// <summary>
    /// 账号级授权信息。**与曲目级的 <see cref="PayInfoDto"/> 同名不同构**，字段集合完全不同。
    /// </summary>
    [JsonPropertyName("payInfo")] public AccountPayInfoDto? PayInfo { get; init; }

    [JsonPropertyName("userFreeInfo")] public UserFreeInfoDto? UserFreeInfo { get; init; }

    [JsonPropertyName("firstLogin")] public int FirstLogin { get; init; }

    [JsonPropertyName("hasName")] public int HasName { get; init; }

    [JsonPropertyName("isBind")] public int IsBind { get; init; }

    [JsonPropertyName("isSendInvite")] public int IsSendInvite { get; init; }

    [JsonPropertyName("isPrivateFriend")] public int IsPrivateFriend { get; init; }

    [JsonPropertyName("ipCity")] public string? IpCity { get; init; }

    [JsonPropertyName("remainActivateDays")] public int RemainActivateDays { get; init; }

    [JsonPropertyName("hasWxShakeRule")] public bool HasWxShakeRule { get; init; }
}

/// <summary>登录用户的资料。</summary>
internal sealed class UserInfoDto
{
    [JsonPropertyName("id")]
    [JsonConverter(typeof(LenientInt64Converter))]
    public long? Id { get; init; }

    [JsonPropertyName("nickname")] public string? NickName { get; init; }

    [JsonPropertyName("headImg")] public string? HeadImg { get; init; }

    [JsonPropertyName("bgImg")] public string? BgImg { get; init; }

    /// <summary>实测为 <c>3</c>。</summary>
    [JsonPropertyName("authType")] public int AuthType { get; init; }

    [JsonPropertyName("email")] public string? Email { get; init; }

    [JsonPropertyName("prov")] public string? Province { get; init; }

    [JsonPropertyName("city")] public string? City { get; init; }

    [JsonPropertyName("address")] public string? Address { get; init; }

    [JsonPropertyName("description")] public string? Description { get; init; }

    [JsonPropertyName("status")] public int Status { get; init; }

    [JsonPropertyName("createTime")] public string? CreateTime { get; init; }

    [JsonPropertyName("updateTime")] public string? UpdateTime { get; init; }

    [JsonPropertyName("isVip")] public int IsVip { get; init; }

    [JsonPropertyName("vipType")] public int VipType { get; init; }

    [JsonPropertyName("payVipType")] public int PayVipType { get; init; }

    [JsonPropertyName("ipCity")] public string? IpCity { get; init; }

    [JsonPropertyName("gender")] public int Gender { get; init; }
}

/// <summary>
/// 账号级授权信息。
/// </summary>
/// <remarks>
/// <b>与曲目级的 <see cref="PayInfoDto"/> 同名不同构</b>——两者唯一相同的是 JSON 字段名
/// <c>payInfo</c>。这里是会员状态与到期时间，那边是单曲的播放/下载权限位。**不要合并成一个类型。**
/// </remarks>
internal sealed class AccountPayInfoDto
{
    [JsonPropertyName("isVip")] public int IsVip { get; init; }

    [JsonPropertyName("isVipBoolean")] public bool IsVipBoolean { get; init; }

    [JsonPropertyName("isSigned")] public int IsSigned { get; init; }

    [JsonPropertyName("isSignedBoolean")] public bool IsSignedBoolean { get; init; }

    [JsonPropertyName("signType")] public int SignType { get; init; }

    [JsonPropertyName("signPayType")] public int SignPayType { get; init; }

    [JsonPropertyName("vipType")] public int VipType { get; init; }

    [JsonPropertyName("isPayVipBoolean")] public bool IsPayVipBoolean { get; init; }

    [JsonPropertyName("isBigVipBoolean")] public bool IsBigVipBoolean { get; init; }

    [JsonPropertyName("isBigPayVipBoolean")] public bool IsBigPayVipBoolean { get; init; }

    [JsonPropertyName("isCtVipBoolean")] public bool IsCtVipBoolean { get; init; }

    [JsonPropertyName("isCtPayVipBoolean")] public bool IsCtPayVipBoolean { get; init; }

    [JsonPropertyName("isActVipBoolean")] public bool IsActVipBoolean { get; init; }

    [JsonPropertyName("payVipType")] public int PayVipType { get; init; }

    [JsonPropertyName("actVipType")] public int ActVipType { get; init; }

    [JsonPropertyName("expireDate")] public long ExpireDate { get; init; }

    [JsonPropertyName("payExpireDate")] public long PayExpireDate { get; init; }

    [JsonPropertyName("bigExpireDate")] public long BigExpireDate { get; init; }

    [JsonPropertyName("bigPayExpireDate")] public long BigPayExpireDate { get; init; }

    [JsonPropertyName("ctExpireDate")] public long CtExpireDate { get; init; }

    [JsonPropertyName("ctPayExpireDate")] public long CtPayExpireDate { get; init; }

    [JsonPropertyName("actExpireDate")] public long ActExpireDate { get; init; }

    [JsonPropertyName("signedCount")] public int SignedCount { get; init; }

    [JsonPropertyName("grantOrderId")] public string? GrantOrderId { get; init; }

    [JsonPropertyName("lastPayment")] public long LastPayment { get; init; }

    [JsonPropertyName("isFreeCtVip")] public bool IsFreeCtVip { get; init; }
}

/// <summary>免费领取的权益状态。</summary>
internal sealed class UserFreeInfoDto
{
    [JsonPropertyName("csIsFree")] public int CsIsFree { get; init; }

    [JsonPropertyName("csExpireDate")] public long CsExpireDate { get; init; }

    [JsonPropertyName("csRemainSeconds")] public long CsRemainSeconds { get; init; }

    [JsonPropertyName("csCanExtend")] public int CsCanExtend { get; init; }

    /// <summary>实测为 <c>0</c>。</summary>
    [JsonPropertyName("freeAdIsFree")] public int FreeAdIsFree { get; init; }

    [JsonPropertyName("freeAdExpireDate")] public long FreeAdExpireDate { get; init; }

    [JsonPropertyName("freeAdRemainSeconds")] public long FreeAdRemainSeconds { get; init; }
}
