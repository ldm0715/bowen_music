namespace Bodian.Core.Api;

/// <summary>
/// 已解析的响应信封。
/// </summary>
/// <remarks>
/// <b>信封不走 JSON 反序列化</b>，因为源生成不支持开放泛型
/// （不能写 <c>JsonSerializable(typeof(BodianEnvelope&lt;&gt;))</c>）。
/// 传输层用 <c>JsonDocument</c> 手工取 <c>code</c> / <c>msg</c> / <c>reqId</c>，
/// 再把 <c>data</c> 子树交给 <c>BodianJsonContext</c> 里的类型信息。
/// </remarks>
/// <param name="Code">业务码原始值。**不认识也保留**，便于排查。</param>
/// <param name="Message">服务端的 <c>msg</c>。</param>
/// <param name="RequestId">服务端的 <c>reqId</c>。排查时拿它去对日志。</param>
/// <param name="Data">反序列化后的 <c>data</c>。业务码非 200 时通常为 <c>null</c>。</param>
/// <param name="SessionRevision">
/// 收到响应时的会话版本号。见 <see cref="BodianSession.Revision"/> 的说明——
/// 调用方发出请求前记下当时的版本，响应回来后若已变化，说明期间发生了登录/登出，
/// **应当丢弃这次的结果**，否则迟到的旧写请求会污染新会话的界面。
/// </param>
/// <param name="Lrcx">
/// 歌词站回显的版式（<c>1</c> 逐字 / <c>0</c> 逐行）。**只有歌词站有这个字段**，
/// 其余端点一律为 <c>null</c>。它不在 <c>data</c> 里，所以进不了 DTO，
/// 只能在信封层取——见 <c>bodian-api-reference.md</c> 2.6 节。
/// </param>
public sealed record BodianEnvelope<T>(
    int Code,
    string? Message,
    string? RequestId,
    T? Data,
    int SessionRevision,
    int? Lrcx = null)
{
    public BodianErrorCode ErrorCode => BodianErrorCodeExtensions.FromRaw(Code);

    public bool IsSuccess => Code == 200;
}
