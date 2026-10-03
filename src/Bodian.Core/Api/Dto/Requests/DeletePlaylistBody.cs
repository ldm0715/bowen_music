using System.Text.Json.Serialization;

namespace Bodian.Core.Api.Dto.Requests;

/// <summary>
/// <c>service/playlist</c> 删除歌单的请求体。
/// </summary>
/// <remarks>
/// 值是<b>数组</b>：2026-10-03 实测一次可以删多个
/// （<c>{"playlistIds":[a,b]}</c> → <c>200</c>、<c>data: {}</c>）。
/// 本项目一次只删一个，但形状按服务端的来。
/// </remarks>
internal sealed record DeletePlaylistBody
{
    [JsonPropertyName("playlistIds")] public required long[] PlaylistIds { get; init; }
}
