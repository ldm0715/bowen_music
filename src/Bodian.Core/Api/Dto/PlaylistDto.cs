using System.Text.Json.Serialization;

namespace Bodian.Core.Api.Dto;

/// <summary>
/// 歌单元数据对象。
/// </summary>
/// <remarks>
/// <para>
/// <b>fixture 来源</b>：<c>fixtures/playlists-userCreate.json</c> 与 <c>playlist-fond.json</c>。
/// 那两个 fixture 的**条目形状**取自官方 PC 客户端本机缓存里真实下发的对象
/// （<c>%LOCALAPPDATA%\cn.wenyu.bodian\bodian_pc\hist\histPlaylist.json</c>，已脱敏），
/// **信封键**（<c>playLists</c> / <c>total</c>）来自反编译 Dart 的解析代码，
/// 证据见 <c>reverse/findings/06-library-api.md</c> §11。**没有任何一次网络请求。**
/// </para>
/// <para>
/// <b>字段类型以本机缓存的真实取值为准</b>：<see cref="IsPrivate"/>、<see cref="Type"/>、
/// <see cref="SubType"/>、<see cref="PlayPos"/> 都是**数字不是布尔**，
/// <see cref="IsRecommendStream"/> 才是布尔。
/// </para>
/// </remarks>
internal sealed class PlaylistDto
{
    [JsonPropertyName("id")] public long Id { get; init; }

    [JsonPropertyName("name")] public string? Name { get; init; }

    [JsonPropertyName("pic")] public string? Pic { get; init; }

    /// <summary>服务端标称的曲目数。**不可信**，见 <c>Models/Playlist</c> 的说明。</summary>
    [JsonPropertyName("musicCount")] public int MusicCount { get; init; }

    /// <summary>
    /// 歌单来源。公开集合是 <c>4</c>，自建歌单与「我喜欢」是 <c>5</c>。
    /// </summary>
    /// <remarks>
    /// 取曲目时的 <c>source</c> 参数用的是这个值 —— 详见 <c>BodianApi.AccountPlaylistSource</c>。
    /// <b>自建歌单的响应可能缺这个字段</b>（文档 6.2），缺时是 <c>0</c>，不能直接拿去当
    /// <c>source</c> 传。
    /// </remarks>
    [JsonPropertyName("sourceType")] public int SourceType { get; init; }

    /// <summary>歌单类型。实测「我喜欢」是 <c>4</c>、自建歌单是 <c>0</c>。</summary>
    [JsonPropertyName("type")] public int Type { get; init; }

    [JsonPropertyName("subType")] public int SubType { get; init; }

    /// <summary><b>数字不是布尔</b>。</summary>
    [JsonPropertyName("isPrivate")] public int IsPrivate { get; init; }

    [JsonPropertyName("description")] public string? Description { get; init; }

    [JsonPropertyName("creatorDescription")] public string? CreatorDescription { get; init; }

    [JsonPropertyName("creatorIcon")] public string? CreatorIcon { get; init; }

    [JsonPropertyName("creatorId")] public long CreatorId { get; init; }

    [JsonPropertyName("creatorName")] public string? CreatorName { get; init; }

    [JsonPropertyName("createTime")] public string? CreateTime { get; init; }

    [JsonPropertyName("initialSongId")] public long InitialSongId { get; init; }

    /// <summary>这个**是**布尔。</summary>
    [JsonPropertyName("isRecommendStream")] public bool IsRecommendStream { get; init; }

    [JsonPropertyName("playPos")] public int PlayPos { get; init; }

    /// <summary>全站播放数。**只有歌单详情会给**，列表来源（侧栏、搜索、收藏列表）没有这个键。</summary>
    /// <remarks>用 <c>long</c>：样本已到 565 万，而播放数是只增不减的累计值。</remarks>
    [JsonPropertyName("playNum")] public long PlayNum { get; init; }

    /// <summary>全站点赞数。</summary>
    /// <remarks>
    /// 实测与 <see cref="CollectedCount"/> **在样本里相等**（文档 2.2），大概率是同一个数的两个名字。
    /// 只消费 <see cref="CollectedCount"/>，这里留着当兜底与证据。
    /// </remarks>
    [JsonPropertyName("praise")] public long Praise { get; init; }

    /// <summary>全站收藏人数。**不是当前账号的收藏态** —— 个人态看 <see cref="CollectTime"/>。</summary>
    [JsonPropertyName("collectedCnt")] public long CollectedCount { get; init; }

    [JsonPropertyName("lastPlayTime")] public string? LastPlayTime { get; init; }

    /// <summary>当前账号的收藏时间（<c>yyyy-MM-dd HH:mm:ss</c>）。**存在即「已收藏」**，未收藏时整个键不出现。</summary>
    [JsonPropertyName("collectTime")] public string? CollectTime { get; init; }
}
