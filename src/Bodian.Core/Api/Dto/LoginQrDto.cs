using System.Text.Json.Serialization;

namespace Bodian.Core.Api.Dto;

/// <summary>
/// <c>ucenter/login/qrCode</c> 的响应。<c>data.qrCode</c> 就是后续轮询与换会话要用的 key。
/// </summary>
internal sealed class QrCodeDto
{
    [JsonPropertyName("qrCode")] public string? QrCode { get; init; }
}

/// <summary>
/// <c>ucenter/login/qrCodeStatus</c> 的响应。
/// </summary>
/// <remarks>
/// <c>1</c> = 等待扫码，<c>2</c> = 已过期，<c>3</c> = 已扫码确认（可以换会话了）。
/// </remarks>
internal sealed class QrCodeStatusDto
{
    [JsonPropertyName("status")] public int Status { get; init; }
}
