using System.Text.Json.Serialization;

namespace Bodian.Core.Api.Dto;

/// <summary>
/// 我**收藏的歌单**（<c>service/collect/4/list</c>）的信封。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="CollectedAlbumsPayload"/> 是**同一条端点、同一份 JSON** ——
/// <c>playLists</c> 是混合列表，元素用自带的 <c>sourceType</c> 分型：
/// <c>4</c> = 歌单、<c>6</c> = 专辑（<c>reverse/findings/10-collect-source.md</c>）。
/// 两者的差别只在「用哪个 DTO 接 + 按哪个 sourceType 过滤」。
/// </para>
/// <para>
/// <b>歌单 ≠ 专辑</b>：这是两个不同的概念，只是共用一条读端点。
/// </para>
/// </remarks>
internal sealed class CollectedPlaylistsPayload
{
    [JsonPropertyName("total")] public int Total { get; init; }

    [JsonPropertyName("playLists")] public PlaylistDto[]? PlayLists { get; init; }
}

/// <summary>
/// 我**关注的歌手**（<c>service/collect/7/list</c>）的信封：<c>{ artistList: [...], total: N }</c>。
/// </summary>
/// <remarks>
/// 这是收藏族里**唯一一条数组键不是 <c>playLists</c> 的**。
/// 详情接口 <c>service/artist/{id}</c> 没有任何 follow 字段，所以「是否已关注」只能靠这份列表判定。
/// </remarks>
internal sealed class FollowedArtistsPayload
{
    [JsonPropertyName("total")] public int Total { get; init; }

    [JsonPropertyName("artistList")] public FollowedArtistDto[]? ArtistList { get; init; }
}

/// <summary>
/// 批量查收藏状态的响应（<c>service/collect/multipleState</c>）：<c>{ result: [{id, collect}] }</c>。
/// </summary>
/// <remarks>
/// <b>专辑的「是否已收藏」只有这条路</b> —— 专辑详情 <c>service/album/{id}</c> 里没有任何收藏标志。
/// query 的 <c>source</c> 必须传 <c>6</c>（专辑），传 <c>4</c> 会一律回 <c>false</c>。
/// </remarks>
internal sealed class CollectMultipleStatePayload
{
    [JsonPropertyName("result")] public CollectStateDto[]? Result { get; init; }
}

/// <summary>批量收藏状态里的一项。</summary>
internal sealed class CollectStateDto
{
    [JsonPropertyName("id")] public long Id { get; init; }

    /// <summary>当前账号是否已收藏。</summary>
    [JsonPropertyName("collect")] public bool Collect { get; init; }
}

/// <summary>关注歌手列表的条目。键名照实测响应写（<c>fixtures/collect-7-list-artists.json</c>）。</summary>
internal sealed class FollowedArtistDto
{
    [JsonPropertyName("id")] public long Id { get; init; }

    [JsonPropertyName("name")] public string? Name { get; init; }

    [JsonPropertyName("aliasName")] public string? AliasName { get; init; }

    [JsonPropertyName("pic")] public string? Pic { get; init; }

    [JsonPropertyName("fansCnt")] public long FansCount { get; init; }

    [JsonPropertyName("musicCnt")] public int MusicCount { get; init; }

    [JsonPropertyName("albumCnt")] public int AlbumCount { get; init; }
}
