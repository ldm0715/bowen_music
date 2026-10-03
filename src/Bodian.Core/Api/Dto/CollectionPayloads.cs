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
/// 歌单详情的 <c>data</c>（<c>GET service/playlist/info/{id}?source=&lt;s&gt;</c>）。
/// </summary>
/// <remarks>
/// <para>
/// 实测样本 <c>fixtures/playlist-info-collected.json</c>（已收藏）与
/// <c>fixtures/playlist-info-not-collected.json</c>（未收藏）。
/// </para>
/// <para>
/// <b>「是否已收藏」看 <see cref="CollectTime"/> 是否存在</b> ——
/// <b>不是 <c>isFond</c></b>：列表 payload 里的 <c>isFond</c> 与个人收藏态无关（连已收藏的也是 0），
/// 而详情响应里根本没有这个字段。已收藏的歌单详情会给 <c>collectTime</c>（收藏时间），
/// 未收藏的不给这个键。证据见 <c>reverse/findings/13-collect-playlist-follow-artist.md</c> §3.3。
/// </para>
/// </remarks>
internal sealed class PlaylistInfoDto
{
    [JsonPropertyName("id")] public long Id { get; init; }

    [JsonPropertyName("name")] public string? Name { get; init; }

    [JsonPropertyName("pic")] public string? Pic { get; init; }

    [JsonPropertyName("musicCount")] public int MusicCount { get; init; }

    [JsonPropertyName("sourceType")] public int SourceType { get; init; }

    /// <summary>全站收藏数，**不是**个人态 —— 与 <c>music/info</c> 的 <c>favorite</c> 同类陷阱。</summary>
    [JsonPropertyName("collectedCnt")] public int CollectedCount { get; init; }

    /// <summary>收藏时间（<c>yyyy-MM-dd HH:mm:ss</c>）。**存在即「已收藏」**，未收藏时整个键不出现。</summary>
    [JsonPropertyName("collectTime")] public string? CollectTime { get; init; }
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
