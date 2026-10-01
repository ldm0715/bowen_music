using System.Text.Json.Serialization;

namespace Bodian.Core.Api.Dto;

/// <summary>
/// 专辑对象（<c>AlbumItem</c>）。
/// </summary>
/// <remarks>
/// <para>
/// <b>来源</b>：反编译 Dart 的解析代码 —— `AlbumItem.fromJson`
/// （<c>allin/data/details_album_data.dart:1028</c>）。**不是照文档写的，也没有 fixture**
/// （本机缓存里没有专辑样本）。证据见 <c>reverse/findings/06-library-api.md</c> §12.1。
/// </para>
/// <para>
/// <b>这一个类型服务三处</b>：已购专辑列表、搜索结果专辑、歌手专辑。
/// </para>
/// <para>
/// <b>也服务专辑详情</b>：<c>service/album/{id}</c> 用的是 <c>AlbumInfo</c>，
/// 是本类型的真超集（多出 <c>artistPic</c> / <c>info</c> / <c>collectedCnt</c> 等，
/// 只少 <c>buyTotal</c>），键名与语义完全一致，所以详情页直接复用本类型 ——
/// 详情页要用的那几个字段见下面「只有专辑详情那一族会给的」。
/// 实测样本 <c>fixtures/album-1293.json</c>。
/// </para>
/// <para>
/// <b>没有 <c>releaseDate</c></b>：那是曲目对象（<c>SongData</c>）的键，不在专辑上。
/// </para>
/// </remarks>
internal sealed class AlbumDto
{
    // ── 安卓端 AlbumItem 的形状（已验证：已购专辑、歌手专辑、搜索结果都用它）──────

    [JsonPropertyName("albumId")] public long AlbumId { get; init; }

    [JsonPropertyName("artist")] public string? Artist { get; init; }

    [JsonPropertyName("artistId")] public long ArtistId { get; init; }

    /// <summary>发行日期的字符串形式。</summary>
    [JsonPropertyName("showtime")] public string? ShowTime { get; init; }

    /// <summary>已购数。只有已购那一族会给。</summary>
    [JsonPropertyName("buyTotal")] public int BuyTotal { get; init; }

    // ── 只有专辑详情那一族会给的 ────────────────────────────────────────────

    /// <summary>艺人头像。详情页头部用。</summary>
    [JsonPropertyName("artistPic")] public string? ArtistPic { get; init; }

    /// <summary>专辑简介。**实测很长**（整篇企划文案），界面上要折叠显示。</summary>
    [JsonPropertyName("info")] public string? Info { get; init; }

    /// <summary>全站收藏数。</summary>
    [JsonPropertyName("collectedCnt")] public int CollectedCount { get; init; }

    // ── 两端共有的 ──────────────────────────────────────────────────────────

    [JsonPropertyName("name")] public string? Name { get; init; }

    [JsonPropertyName("pic")] public string? Pic { get; init; }

    [JsonPropertyName("musicCount")] public int MusicCount { get; init; }

    // ── 桌面端特有的归一化形状（⚠️ 临时，见下）──────────────────────────────

    /// <summary>
    /// 桌面端把专辑**归一化成与歌单同一套模型**，主键是 <c>id</c> 而不是 <c>albumId</c>。
    /// </summary>
    /// <remarks>
    /// 证据是官方桌面端自己的缓存 <c>hist/histAlbumList.json</c>：里面那条收藏专辑的键是
    /// <c>id</c> / <c>name</c> / <c>pic</c> / <c>musicCount</c> / <c>sourceType</c> / <c>type</c> / <c>subType</c>，
    /// 与歌单对象完全同形，其中 <b><c>sourceType</c> 是 <c>6</c></b>。
    /// </remarks>
    [JsonPropertyName("id")] public long Id { get; init; }

    /// <summary>专辑是 <c>6</c>，歌单是 <c>4</c>（公开集合）或 <c>5</c>（账号歌单）。</summary>
    [JsonPropertyName("sourceType")] public int SourceType { get; init; }

    /// <summary>桌面端归一化模型里，专辑是 <c>1</c>、自建歌单是 <c>0</c>、「我喜欢」是 <c>4</c>。</summary>
    [JsonPropertyName("type")] public int Type { get; init; }

    [JsonPropertyName("subType")] public int SubType { get; init; }

    /// <summary>归一化模型里「创建者」的位置 —— 专辑没有创建者，那里放的是艺人。</summary>
    [JsonPropertyName("creatorName")] public string? CreatorName { get; init; }

    /// <summary>
    /// 两种形状取到的 id。<b>谁有值用谁</b>，调用方不必关心这个接口回的是哪一种。
    /// </summary>
    /// <remarks>两个都是 0 时说明这个接口回的两种形状都不是，调用方应当当作解析失败。</remarks>
    public long EffectiveId => AlbumId > 0 ? AlbumId : Id;

    /// <summary>两种形状取到的艺人串。</summary>
    public string? EffectiveArtist =>
        !string.IsNullOrWhiteSpace(Artist) ? Artist : CreatorName;
}
