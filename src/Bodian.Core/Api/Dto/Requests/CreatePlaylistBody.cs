using System.Text.Json.Serialization;

namespace Bodian.Core.Api.Dto.Requests;

/// <summary>
/// <c>service/playlist</c> 新建歌单的请求体。
/// </summary>
/// <remarks>
/// <para>
/// 键名来自移动端反汇编（<c>reverse/findings/11-share-playlist-crud.md</c> §2.1），
/// 2026-10-03 实测走通并确认了方向：传 <c>true</c> → 读回 <c>isPrivate: 1</c>。
/// </para>
/// <para>
/// ★ <b>请求键是 <c>private</c>（JSON 布尔），而响应回的键是 <c>isPrivate</c>（数字）</b> ——
/// 两个名字、两种类型，见 <c>PlaylistDto.IsPrivate</c>。写错不会有报错，
/// 只会静默建出一个公开歌单。
/// </para>
/// </remarks>
internal sealed record CreatePlaylistBody
{
    [JsonPropertyName("name")] public required string Name { get; init; }

    /// <summary>是否设为隐私歌单。</summary>
    [JsonPropertyName("private")] public required bool Private { get; init; }
}
