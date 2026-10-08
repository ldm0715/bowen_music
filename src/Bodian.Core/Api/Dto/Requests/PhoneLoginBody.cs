using System.Text.Json.Serialization;

namespace Bodian.Core.Api.Dto.Requests;

/// <summary>
/// <c>ucenter/users/login</c> 的手机号登录请求体。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="LoginBody"/> 同一个端点，只差 <c>authType</c>：那个是二维码（<c>10</c>），
/// 这个是手机号 + 验证码（<c>1</c>）。
/// </para>
/// <para>
/// <b>手机号与验证码都是明文。</b> 安卓端的 <c>encvMobile</c> / <c>encvVerifyCode</c>
/// 那套 XOR 混淆在桌面端不认，实测明文才通。
/// 证据：<c>reverse/findings/16-phone-login.md</c>。
/// </para>
/// </remarks>
internal sealed class PhoneLoginBody
{
    /// <summary>
    /// 换取手机号会话唯一可用的 <c>authType</c>。
    /// </summary>
    /// <remarks>
    /// <b>桌面端的枚举与安卓端不是一套。</b> 桌面端 <c>2</c> 是 QQ、<c>7</c> 是 Apple、
    /// <c>10</c> 是二维码；手机号是 <c>1</c>。这个值不开放给调用方传，只能取常量。
    /// </remarks>
    public const int SmsAuthType = 1;

    /// <summary>
    /// <c>Endpoints.SendSms</c> 的必填 query 参数 <c>type</c> 在登录场景的取值。
    /// </summary>
    /// <remarks>
    /// 放在这里是因为它和 <see cref="SmsAuthType"/> 是同一件事的两半 —— 只有成对出现才有意义。
    /// </remarks>
    public const int LoginSmsType = 2;

    [JsonPropertyName("authType")] public int AuthType { get; init; } = SmsAuthType;

    [JsonPropertyName("mobile")] public string Mobile { get; init; } = "";

    [JsonPropertyName("verifyCode")] public string VerifyCode { get; init; } = "";
}
