namespace Bodian.Core.Api;

public enum BodianHttpVerb
{
    Get,
    Post,
}

/// <summary>
/// 一次请求的形状。
/// </summary>
/// <remarks>
/// <b><see cref="Path"/> 不含 <c>/api</c> 前缀</b>——签名覆盖的就是这个形态
/// （<c>fixtures/sign-golden.json</c> 的 <c>pathForm</c> 是 <c>Bare</c>）。
/// </remarks>
public sealed record BodianRequest
{
    /// <summary>业务路径，不含 <c>/api</c> 前缀。例：<c>service/music/info</c>。</summary>
    public required string Path { get; init; }

    public BodianHttpVerb Verb { get; init; } = BodianHttpVerb.Get;

    /// <summary>业务参数。<c>uid</c> / <c>token</c> / <c>timestamp</c> / <c>sign</c> 由传输层补。</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Query { get; init; } = [];

    /// <summary>
    /// 即将发送的精确 UTF-8 JSON 串。**签名覆盖这份字节**，所以序列化必须由调用方完成，
    /// 传输层只做搬运，不做规范化。
    /// </summary>
    public string? JsonBody { get; init; }

    /// <summary>是否需要 <c>timestamp</c> 与 <c>sign</c>。</summary>
    public bool Signed { get; init; }

    /// <summary>
    /// 除 <see cref="BodianErrorCode.Success"/> 之外还接受哪些业务码（不抛异常）。
    /// </summary>
    /// <remarks>
    /// 典型用法是登录轮询：<c>11027</c>（已扫未确认）是正常中间态，
    /// 传进来即可继续轮询而不是当成失败。
    /// </remarks>
    public IReadOnlyCollection<BodianErrorCode> AcceptedCodes { get; init; } = [];
}
