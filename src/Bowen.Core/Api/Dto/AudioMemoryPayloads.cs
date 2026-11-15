using System.Text.Json.Serialization;

namespace Bowen.Core.Api.Dto;

/// <summary>
/// <c>ucenter/playdata/play_ranking</c> 的 <c>data</c>。
/// </summary>
/// <remarks>
/// 每项是完整歌曲对象（<see cref="TrackDto"/>）加一个 <c>playcnt</c>。
/// 2026-11-15 桌面请求头实测，见 <c>docs/audio-memory.md</c>。
/// </remarks>
internal sealed class PlayRankingPayload
{
    [JsonPropertyName("musicList")] public TrackDto[]? MusicList { get; init; }
}

/// <summary>
/// <c>play/month/report/info</c> 的 <c>data</c>。
/// </summary>
/// <remarks>
/// <b><see cref="IsUpdate"/> 不能当存在性判据</b>：无数据的月份同样是 <c>1</c>，
/// 但 <see cref="Info"/> 缺失。
/// </remarks>
internal sealed class MonthReportPayload
{
    [JsonPropertyName("isUpdate")] public int? IsUpdate { get; init; }

    [JsonPropertyName("info")] public MonthReportInfoDto? Info { get; init; }
}

internal sealed class MonthReportInfoDto
{
    /// <summary><c>yyyy-MM-dd</c>。报告月份以它为准。</summary>
    [JsonPropertyName("monthDate")] public string? MonthDate { get; init; }

    [JsonPropertyName("playTotal")] public int? PlayTotal { get; init; }

    [JsonPropertyName("playChain")] public string? PlayChain { get; init; }

    [JsonPropertyName("playContent")] public string? PlayContent { get; init; }

    [JsonPropertyName("listenTotal")] public string? ListenTotal { get; init; }

    [JsonPropertyName("listenChain")] public string? ListenChain { get; init; }

    [JsonPropertyName("listenContent")] public string? ListenContent { get; init; }

    /// <summary>听歌时长最多的曲目名。部分月份不给 <c>listenContent</c> 时用它补位。</summary>
    [JsonPropertyName("listenTop1MusicName")] public string? ListenTop1MusicName { get; init; }

    [JsonPropertyName("songNum")] public int? SongNum { get; init; }

    /// <summary>听歌时长超过的用户百分比。</summary>
    [JsonPropertyName("timeRank")] public int? TimeRank { get; init; }

    [JsonPropertyName("musicTop10")] public ReportTrackDto[]? MusicTop10 { get; init; }

    /// <summary>只有封面 URL，没有专辑 id 与名字。</summary>
    [JsonPropertyName("albumTop10")] public string[]? AlbumTop10 { get; init; }

    [JsonPropertyName("artistName")] public string? ArtistName { get; init; }

    [JsonPropertyName("artistPic")] public string? ArtistPic { get; init; }

    [JsonPropertyName("artistMusicName")] public string? ArtistMusicName { get; init; }

    [JsonPropertyName("artistMusicPic")] public string? ArtistMusicPic { get; init; }

    [JsonPropertyName("artistContent")] public string? ArtistContent { get; init; }
}

/// <summary>月报 Top 列表里的简化曲目。没有音源档位。</summary>
internal sealed class ReportTrackDto
{
    [JsonPropertyName("id")] public long Id { get; init; }

    [JsonPropertyName("name")] public string? Name { get; init; }

    [JsonPropertyName("artist")] public string? Artist { get; init; }

    [JsonPropertyName("album")] public string? Album { get; init; }

    [JsonPropertyName("albumPic")] public string? AlbumPic { get; init; }

    [JsonPropertyName("playCount")] public long? PlayCount { get; init; }
}

/// <summary>
/// <c>play/month/report/summary</c> 的 <c>data</c>。
/// </summary>
/// <remarks>本项目只用 <c>monthReport</c> 定位最近有数据的月份；<c>weekReport</c> 不映射。</remarks>
internal sealed class MonthReportSummaryPayload
{
    [JsonPropertyName("monthReport")] public MonthReportPreviewDto? MonthReport { get; init; }
}

internal sealed class MonthReportPreviewDto
{
    [JsonPropertyName("listenTotal")] public string? ListenTotal { get; init; }

    [JsonPropertyName("listenChain")] public string? ListenChain { get; init; }

    [JsonPropertyName("pic")] public string? Pic { get; init; }

    /// <summary>如 <c>"10月听歌"</c>，没有年份。</summary>
    [JsonPropertyName("monthDesc")] public string? MonthDesc { get; init; }
}
