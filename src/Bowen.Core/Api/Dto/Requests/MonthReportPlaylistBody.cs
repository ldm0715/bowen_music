using System.Text.Json.Serialization;

namespace Bowen.Core.Api.Dto.Requests;

/// <summary>
/// <c>play/month/report/playlist</c> 的请求体。
/// </summary>
/// <remarks>
/// 反编译里只有 <c>date</c> 一个键（<c>YYYYMM</c>），服务端自己命名歌单。
/// 响应体、重复调用是否幂等均未实网验证，见 <c>docs/audio-memory.md</c>。
/// </remarks>
internal sealed record MonthReportPlaylistBody
{
    [JsonPropertyName("date")] public required string Date { get; init; }
}
