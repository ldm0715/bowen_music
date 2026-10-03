using System.Text.Json.Serialization;

namespace Bodian.Core.Api.Dto.Requests;

/// <summary>
/// 歌单加歌 / 删歌的请求体，两条路径共用同一个形状。
/// </summary>
/// <remarks>
/// 字段来自官方桌面端二进制（findings/06 §8）与文档 2.4 的往返实测。
/// <c>playListId</c> 与 <c>musicIdList</c> 元素都必须传安全整数，不能传字符串。
/// </remarks>
internal sealed record PlaylistMusicBody
{
    [JsonPropertyName("playListId")] public long PlayListId { get; init; }

    [JsonPropertyName("musicIdList")] public required long[] MusicIdList { get; init; }
}
