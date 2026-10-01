using System.Text.Json.Serialization;

namespace Bodian.Core.Api.Dto;

/// <summary>
/// 排行榜首页：**顶层就是一个数组**，每项是一组。
/// </summary>
/// <remarks>
/// 形状取自实测样本 <c>fixtures/home-bangNew.json</c>：
/// <c>data</c> 是 <c>[{ moduleName, moduleId, bangList: [...] }]</c>，实测 5 组。
/// <b>注意与 <c>service/home/index</c> 不同</b>：那个是 <c>{ moduleList: [...] }</c> 包一层对象。
/// </remarks>
internal sealed class BangSectionDto
{
    [JsonPropertyName("moduleName")] public string? ModuleName { get; init; }

    [JsonPropertyName("moduleId")] public int ModuleId { get; init; }

    [JsonPropertyName("bangList")] public BangDto[]? BangList { get; init; }
}

/// <summary>一个榜（首页里的形式，带前几首预览）。</summary>
/// <remarks>
/// <c>bangType</c> 实测都是 <c>"bodian"</c>；<c>pub</c> 是日期、<c>pubStr</c> 是人话
/// （形如「09-30更新」）。本项目只消费后者。
/// </remarks>
internal sealed class BangDto
{
    [JsonPropertyName("id")] public long Id { get; init; }

    [JsonPropertyName("name")] public string? Name { get; init; }

    [JsonPropertyName("pic")] public string? Pic { get; init; }

    [JsonPropertyName("bangType")] public string? BangType { get; init; }

    [JsonPropertyName("pub")] public string? Pub { get; init; }

    [JsonPropertyName("pubStr")] public string? PubStr { get; init; }

    /// <summary>预览曲目，实测每个榜 5 首。<b>不是完整榜单。</b></summary>
    [JsonPropertyName("musics")] public TrackDto[]? Musics { get; init; }
}

/// <summary>
/// 一个榜的曲目（<c>service/bang/{id}/musics</c>）。
/// </summary>
/// <remarks>
/// 实测：默认回 20 首、<c>total</c> 是 100；带 <c>pn=2&amp;rn=10</c> 回第 11–20 名，
/// 说明 <b>页号从 1 起、分页有效</b>。所以完整榜单要翻 10 页（rn=10）或更少页（rn 更大）。
/// </remarks>
internal sealed class BangMusicsPayload : ITrackListPayload
{
    [JsonPropertyName("id")] public long Id { get; init; }

    [JsonPropertyName("name")] public string? Name { get; init; }

    [JsonPropertyName("pic")] public string? Pic { get; init; }

    /// <summary>榜单的曲目总数。实测 100。</summary>
    [JsonPropertyName("total")] public int Total { get; init; }

    [JsonPropertyName("musics")] public TrackDto[]? Musics { get; init; }

    IReadOnlyList<TrackDto>? ITrackListPayload.Items => Musics;
}
