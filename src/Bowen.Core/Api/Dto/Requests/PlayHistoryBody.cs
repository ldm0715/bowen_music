using System.Text.Json.Serialization;

namespace Bowen.Core.Api.Dto.Requests;

/// <summary>
/// <c>playlist/history/add</c> 的请求体。
/// </summary>
/// <remarks>
/// <para>
/// 形状来自 5.9.8 arm64 静态反汇编（<c>service/user_api_service.dart</c> 的
/// <c>addPlayHistory</c>）：body 只有 <c>sourceType</c> 与 <c>items</c>，
/// <c>uid</c> / <c>token</c> 由传输层统一补，官方报文里没有这两个键。
/// </para>
/// <para>
/// <b><see cref="SourceType"/> 是历史条目的种类</b>（1 歌曲 / 2 歌单 / 3 专辑），
/// 与歌单对象自身的 <c>sourceType</c> 不是一回事。后者作为 <c>playlistType</c> 放进条目里。
/// </para>
/// </remarks>
internal sealed record PlayHistoryAddBody
{
    [JsonPropertyName("sourceType")] public required int SourceType { get; init; }

    [JsonPropertyName("items")] public required PlayHistoryItem[] Items { get; init; }
}

/// <summary>
/// 一条历史记录。服务端只认 <c>sourceId</c>，曲名、艺人、封面由它自己补。
/// </summary>
internal sealed record PlayHistoryItem
{
    [JsonPropertyName("sourceId")] public required long SourceId { get; init; }

    /// <summary>
    /// 歌单 / 专辑自身的 <c>sourceType</c>。歌曲条目**不带这个键**。
    /// </summary>
    /// <remarks>
    /// 用 <see cref="JsonIgnoreCondition.WhenWritingNull"/> 表达「不带」，而不是序列化出
    /// <c>null</c>：签名覆盖的是即将发出的精确字节，多一个键就是另一份报文。
    /// </remarks>
    [JsonPropertyName("playlistType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PlaylistType { get; init; }
}
