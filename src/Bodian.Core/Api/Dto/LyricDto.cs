using System.Text.Json.Serialization;

namespace Bodian.Core.Api.Dto;

/// <summary>
/// 歌词站 <c>mlyric.kuwo.cn/mobi.s</c> 的响应的 <c>data</c>。
/// </summary>
/// <remarks>
/// <b>顶层还有一个 <c>lrcx</c> 字段</b>回显服务端实际给的版式，它不在 <c>data</c> 里，
/// 所以不进这个类型——由调用方从信封顶层取（见 <c>bodian-api-reference.md</c> 2.6 节）。
/// <para>
/// <c>content</c> 为空串时**业务码仍是 200**：该曲没有逐字轨时请求 <c>lrcx=1</c> 就是这个结果，
/// 空串不等于出错。
/// </para>
/// </remarks>
internal sealed class LyricContentDto
{
    /// <summary>Base64 编码的歌词文本。解码见 <c>Bodian.Core.Lyrics.BodianLyricPayload</c>。</summary>
    [JsonPropertyName("content")] public string? Content { get; init; }
}
