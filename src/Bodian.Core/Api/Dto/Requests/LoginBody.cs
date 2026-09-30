using System.Text.Json.Serialization;

namespace Bodian.Core.Api.Dto.Requests;

/// <summary>
/// <c>ucenter/users/login</c> 的请求体。
/// </summary>
/// <remarks>
/// <b><see cref="AuthType"/> 只能是 <see cref="QrAuthType"/>。</b> 历史坑：用 <c>9</c> 会让服务端
/// 返回**扫码人以外**的账号会话。这个值不开放给调用方传，只能取常量。
/// </remarks>
internal sealed class LoginBody
{
    /// <summary>换取扫码会话唯一可用的 <c>authType</c>。</summary>
    public const int QrAuthType = 10;

    [JsonPropertyName("authType")] public int AuthType { get; init; } = QrAuthType;

    [JsonPropertyName("qrCode")] public string QrCode { get; init; } = "";
}
