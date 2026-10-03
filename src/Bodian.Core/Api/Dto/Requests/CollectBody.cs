using System.Text.Json.Serialization;

namespace Bodian.Core.Api.Dto.Requests;

/// <summary>
/// <c>service/collect</c> 的请求体。收藏歌单/专辑与关注歌手**共用**同一个端点与形状。
/// </summary>
/// <remarks>
/// <para>
/// 字段与取值语义来自 <c>reverse/findings/13-collect-playlist-follow-artist.md</c>：
/// 静态反汇编（5.2.5 与 5.9.8 逐字一致）+ 真机往返双证。
/// </para>
/// <para>
/// <b><c>op</c>：<c>1</c> = 收藏 / 关注，<c>2</c> = 取消收藏 / 取消关注。</b>
/// 官方是「先查当前态、再算目标态」的 toggle，服务端把它当一次<b>设置</b>而不是切换 ——
/// 所以客户端必须自己先确定目标态，不能靠服务端翻转。
/// </para>
/// <para>
/// <b><c>token</c> 只有关注歌手（<c>source=7</c>）才带</b>；收藏歌单/专辑（<c>source=4</c>）
/// 的报文里没有这个键。这里用 <see cref="JsonIgnoreCondition.WhenWritingNull"/> 表达「不带」，
/// 而不是序列化出一个 <c>null</c> —— 签名覆盖的是即将发出的精确字节，多一个键就是另一份报文。
/// </para>
/// </remarks>
internal sealed record CollectBody
{
    /// <summary>收藏类型：歌单/专辑是 <c>4</c>，关注歌手是 <c>7</c>。</summary>
    [JsonPropertyName("source")] public required int Source { get; init; }

    /// <summary>收藏对象的 id 列表。**服务端要数组**，传标量会 400（findings/01 §4）。</summary>
    [JsonPropertyName("sourceId")] public required long[] SourceId { get; init; }

    /// <summary><c>1</c> = 收藏/关注，<c>2</c> = 取消。</summary>
    [JsonPropertyName("op")] public required int Op { get; init; }

    [JsonPropertyName("uid")] public required long Uid { get; init; }

    /// <summary>关注歌手时才带；其余场景保持 <c>null</c>，序列化时整个键不出现。</summary>
    [JsonPropertyName("token")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Token { get; init; }
}
