using System.Text.Json.Serialization;

namespace Bodian.Core.Api.Dto;

/// <summary>
/// 专辑详情的 <c>data</c>：<c>{ albumInfo: {...} }</c>。
/// </summary>
/// <remarks>
/// 实测样本 <c>fixtures/album-1293.json</c>。<b>只读 <c>albumInfo</c> 一个键</b>，
/// 详情本身不含曲目列表 —— 曲目走 <c>service/album/music/{id}</c>。
/// </remarks>
internal sealed class AlbumInfoPayload
{
    [JsonPropertyName("albumInfo")] public AlbumDto? AlbumInfo { get; init; }
}

/// <summary>
/// 专辑曲目的 <c>data</c>：<c>{ total, rn, resultList, pn }</c>。
/// </summary>
/// <remarks>
/// <b>数组键是 <c>resultList</c></b> —— 与搜索列表同键、与歌单的 <c>list</c> 不同。
/// 实测样本 <c>fixtures/album-1293-tracks.json</c>。
/// </remarks>
internal sealed class AlbumTracksPayload : ITrackListPayload
{
    [JsonPropertyName("total")] public int Total { get; init; }

    [JsonPropertyName("resultList")] public TrackDto[]? ResultList { get; init; }

    IReadOnlyList<TrackDto>? ITrackListPayload.Items => ResultList;
}
