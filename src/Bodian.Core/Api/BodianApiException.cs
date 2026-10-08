using System.Runtime.CompilerServices;
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

/// <summary>
/// 本地就判定这个接口需要登录 —— <b>请求根本没发出去</b>。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="BodianApiException"/> 的分工是「谁做的判断」：那个是服务端回的失败码，
/// 这个是客户端自己拦下的。分开是为了让界面给出不同的话 —— 前者是「加载失败」，
/// 后者是「登录后可查看」，而且后者<b>不该给重试按钮</b>（重试还是同一个结果）。
/// </para>
/// <para>
/// <b>它不表示「权限被绕过」。</b> 真正的权限一律由服务端裁决；这里拦的只有
/// 「本地压根没有可用会话」这一种情况。
/// </para>
/// <para>
/// 继承 <see cref="InvalidOperationException"/>：语义就是「这个对象在这个状态下不接受这个调用」，
/// 而这一族守卫原来抛的就是它 —— 派生之后既有的断言与上游的兜底 catch 都不受影响。
/// </para>
/// </remarks>
public sealed class BodianNotSignedInException : InvalidOperationException
{
    /// <param name="caller">发起调用的方法名。由编译器补，日志里据此定位是哪一处拦下的。</param>
    public BodianNotSignedInException([CallerMemberName] string? caller = null)
        : base($"这个接口需要登录后才能调用（{caller}）")
        => Caller = caller;

    public string? Caller { get; }
}
