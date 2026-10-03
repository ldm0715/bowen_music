namespace Bodian.Core.Api;

public enum BodianHttpVerb
{
    Get,
    Post,

    /// <summary>
    /// 删歌单。**只有 <c>service/playlist</c> 那一条走它**。
    /// </summary>
    Delete,

    /// <summary>
    /// 编辑歌单。与 <see cref="Post"/>、<see cref="Delete"/> 共用 <c>service/playlist</c>，
    /// 靠 method 区分，见 <c>Endpoints.PlaylistCrud</c>。
    /// </summary>
    Put,
}

/// <summary>
/// 一次 <c>multipart/form-data</c> 上传的文件部分。
/// </summary>
/// <remarks>
/// 上传封面的请求体是二进制，没有 JSON 形态，所以不能复用 <see cref="BodianRequest.JsonBody"/>。
/// 带它的请求**签名的 body 部分是 null**（只签 path 与 query）—— 桌面签名对 body 算的是
/// <c>md5(body + "kuwotest")</c>，那是针对 JSON 字符串的，二进制没有良定义的字符串形态。
/// </remarks>
public sealed record BodianFormFile(string FieldName, string FileName, string ContentType, byte[] Content);

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

    /// <summary>此请求的 plat 头覆盖值；移动端专属的评论列表使用 android。</summary>
    public string? Platform { get; init; }

    /// <summary>业务参数。<c>uid</c> / <c>token</c> / <c>timestamp</c> / <c>sign</c> 由传输层补。</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Query { get; init; } = [];

    /// <summary>
    /// 即将发送的精确 UTF-8 JSON 串。**签名覆盖这份字节**，所以序列化必须由调用方完成，
    /// 传输层只做搬运，不做规范化。
    /// </summary>
    public string? JsonBody { get; init; }

    /// <summary>
    /// 要上传的二进制文件（multipart）。**与 <see cref="JsonBody"/> 互斥**，
    /// 两者同时给时以 <see cref="JsonBody"/> 为准。
    /// </summary>
    public BodianFormFile? File { get; init; }

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
