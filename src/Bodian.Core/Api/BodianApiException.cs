using System.Text.Json;
using Bodian.Core.Diagnostics;

namespace Bodian.Core.Api;

/// <summary>
/// 业务码不是成功（且不在 <see cref="BodianRequest.AcceptedCodes"/> 里）时抛出。
/// </summary>
/// <remarks>
/// <b><see cref="Exception.Message"/> 与 <see cref="RawBody"/> 都经过脱敏。</b>
/// 消息里带上 <see cref="Path"/>（不含 query）与 <see cref="RequestId"/>，
/// 足够定位问题又不会把凭据带进日志或异常上报。
/// </remarks>
public class BodianApiException : Exception
{
    public BodianApiException(
        BodianErrorCode errorCode,
        int rawCode,
        string path,
        string? serverMessage,
        string? requestId,
        string? rawBody = null)
        : base(BuildMessage(errorCode, rawCode, path, serverMessage, requestId))
    {
        ErrorCode = errorCode;
        RawCode = rawCode;
        Path = path;
        ServerMessage = serverMessage;
        RequestId = requestId;
        RawBody = rawBody is null ? null : LogRedactor.Redact(rawBody);
    }

    public BodianErrorCode ErrorCode { get; }

    /// <summary>服务端给的原始业务码。枚举认不出它时为 <see cref="BodianErrorCode.Unknown"/>。</summary>
    public int RawCode { get; }

    /// <summary>业务路径，不含 query 与 <c>/api</c> 前缀。</summary>
    public string Path { get; }

    public string? ServerMessage { get; }

    public string? RequestId { get; }

    /// <summary>响应原文（已脱敏）。用于排查网关错误页这类非 JSON 响应。</summary>
    public string? RawBody { get; }

    private static string BuildMessage(
        BodianErrorCode errorCode,
        int rawCode,
        string path,
        string? serverMessage,
        string? requestId)
    {
        var text = $"{path} 返回业务码 {rawCode}（{errorCode}）";

        if (!string.IsNullOrWhiteSpace(serverMessage))
        {
            text += $"：{serverMessage}";
        }

        if (!string.IsNullOrWhiteSpace(requestId))
        {
            text += $"（reqId={requestId}）";
        }

        return text;
    }
}

/// <summary>
/// 响应体不是合法 JSON 信封。
/// </summary>
public sealed class BodianMalformedResponseException : BodianApiException
{
    public BodianMalformedResponseException(string path, string rawBody, JsonException inner)
        : base(BodianErrorCode.MalformedResponse, -2, path, inner.Message, null, rawBody)
        => InnerJsonError = inner;

    public JsonException InnerJsonError { get; }
}

/// <summary>
/// 响应体超过 <see cref="BodianTransportOptions.MaxResponseBytes"/>。
/// </summary>
/// <remarks>
/// 限额作用在**解压后**的字节上，所以它同时也是 gzip 炸弹的挡板。
/// </remarks>
public sealed class BodianPayloadTooLargeException : Exception
{
    public BodianPayloadTooLargeException(string path, long limitBytes)
        : base($"{path} 的响应体超过上限 {limitBytes} 字节（已截断）")
    {
        Path = path;
        LimitBytes = limitBytes;
    }

    public string Path { get; }

    public long LimitBytes { get; }
}
