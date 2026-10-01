using System.Text.Json.Serialization;

namespace Bodian.Core.Api.Dto;

/// <summary>
/// 歌单**列表**的信封：<c>{ playLists: [...], total: N }</c>。
/// </summary>
/// <remarks>
/// <para>
/// 两个端点共用它：<c>service/playlist/userCreate</c>（自建歌单）与
/// <c>service/collect/{source}/list</c>（收藏族）。
/// </para>
/// <para>
/// <b>这一族各家字段名都不一样，所以不抽公共基类。</b> 文档 2.2 的列表字段表就是被逼出来的：
/// 搜索是 <c>resultList</c>、歌单曲目是 <c>list</c>、收藏歌单是 <c>playLists</c>、
/// 收藏专辑是 <c>albumList</c>。已购那一族更远 —— 总数键是 <c>size</c> 不是 <c>total</c>。
/// 抽一个 <c>Total</c> 基类会在第一次遇到 <c>size</c> 时崩掉，且崩在解析层而不是调用点。
/// </para>
/// <para>
/// <b>收藏专辑（<c>source=6</c>）暂时没有</b>：它的 <c>source</c> 取值与数组键**都**没有
/// 静态证据（全树只命中 4 / 7 / 8 / 12 四个 source，且不同 source 用不同的键：
/// <c>4→playLists</c>、<c>7→artistList</c>、<c>8→userList</c>）。
/// 见 <c>docs/library-sidebar.md</c> §3.2。
/// </para>
/// </remarks>
internal sealed class PlaylistListPayload
{
    [JsonPropertyName("total")] public int Total { get; init; }

    [JsonPropertyName("playLists")] public PlaylistDto[]? PlayLists { get; init; }
}

/// <summary>
/// 歌单曲目的信封：<c>{ list: [...], total: N }</c>。
/// </summary>
/// <remarks>
/// 元素与搜索结果是同一个 <see cref="TrackDto"/>，差异只在信封键 ——
/// 搜索是 <c>resultList</c>，这里是 <c>list</c>。
/// </remarks>
internal sealed class PlaylistTracksPayload : ITrackListPayload
{
    [JsonPropertyName("total")] public int Total { get; init; }

    [JsonPropertyName("list")] public TrackDto[]? List { get; init; }

    IReadOnlyList<TrackDto>? ITrackListPayload.Items => List;
}
