using System.Text.Json.Serialization;

namespace Bodian.Core.Api.Dto.Requests;

/// <summary>
/// <c>play/music/v2/audioUrl</c> 的请求体。请求形态与 <c>checkRight</c> 相同（GET + JSON body + 签名）。
/// </summary>
/// <remarks>
/// <para>
/// <b>这个类型刻意没有 <c>format</c> 属性 —— 不是漏写，是设计。</b>
/// </para>
/// <para>
/// 实测：请求里带 <c>format=flac</c> 会让服务端**静默降级到 320k mp3**，而业务码仍然是 200。
/// 加一个 <c>format</c> 字段就等于给后来的人一支能静默降级的枪；类型里根本没有它，就传不出去。
/// <b>只传 <c>br</c>。</b>
/// </para>
/// <para>
/// 字段顺序与 P0 探针一致（签名覆盖 body 的精确字节，顺序变了签名就变了）。
/// </para>
/// </remarks>
internal sealed class AudioUrlBody
{
    [JsonPropertyName("devId")] public string DevId { get; init; } = "";

    [JsonPropertyName("musicId")] public long MusicId { get; init; }

    [JsonPropertyName("br")] public string Br { get; init; } = "";

    /// <summary>服务端在 <c>payInfo</c> 里下发的防盗链串，客户端只透传。默认空串。</summary>
    [JsonPropertyName("freeSign")] public string FreeSign { get; init; } = "";
}
