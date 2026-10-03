using System.Text.Json.Serialization;

namespace Bodian.Core.Api.Dto;

/// <summary>
/// 歌手详情的 <c>data</c>：<c>{ artistInfo: {...} }</c>。
/// </summary>
/// <remarks>
/// 实测样本 <c>fixtures/artist-336.json</c>。<b>只读 <c>artistInfo</c> 一个键</b>，
/// 详情不含曲目与专辑 —— 那两条走 <c>service/artist/music/{id}</c> 与 <c>service/artist/album/{id}</c>。
/// </remarks>
internal sealed class ArtistInfoPayload
{
    [JsonPropertyName("artistInfo")] public ArtistInfoDto? ArtistInfo { get; init; }
}

/// <summary>
/// 歌手详情的条目形状。键名照 <c>fixtures/artist-336.json</c> 写。
/// </summary>
/// <remarks>
/// <para>
/// <c>desc</c> <b>极长</b>（周杰伦那条实测 12466 字符，是整篇人物介绍），界面上必须单独滚动。
/// </para>
/// <para>
/// <c>musicCnt</c> 与曲目端点给的 <c>total</c> <b>对不上</b>（同一个歌手实测 1708 对 1750，
/// 见 <c>bodian-api-reference.md</c> §2.7）。这里原样透传，<b>不拿它算页数</b>。
/// </para>
/// </remarks>
internal sealed class ArtistInfoDto
{
    [JsonPropertyName("name")] public string? Name { get; init; }

    /// <summary>别名，如周杰伦的 <c>Jay Chou</c>。可能为空。</summary>
    [JsonPropertyName("aliasName")] public string? AliasName { get; init; }

    [JsonPropertyName("pic")] public string? Pic { get; init; }

    [JsonPropertyName("fansCnt")] public long FansCount { get; init; }

    [JsonPropertyName("musicCnt")] public int MusicCount { get; init; }

    [JsonPropertyName("albumCnt")] public int AlbumCount { get; init; }

    /// <summary>歌手介绍，整篇人物生平。</summary>
    [JsonPropertyName("desc")] public string? Description { get; init; }
}
