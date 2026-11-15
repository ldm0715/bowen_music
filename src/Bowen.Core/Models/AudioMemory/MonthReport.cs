namespace Bowen.Core.Models.AudioMemory;

/// <summary>
/// 一份完整的听歌月报。
/// </summary>
/// <remarks>
/// 只映射界面真正用到的字段。协议里还有 <c>timeRank</c> 等未确认含义的字段，不往模型里搬。
/// 字段类型与空数据行为见 <c>docs/audio-memory.md</c>。
/// </remarks>
public sealed record MonthReport
{
    /// <summary>报告月份，以响应里的 <c>monthDate</c> 为准。</summary>
    public required int Year { get; init; }

    public required int Month { get; init; }

    /// <summary>听歌时长，<b>分钟</b>，服务端给的是数字字符串（如 <c>"175"</c>）。</summary>
    public string? ListenTotalMinutes { get; init; }

    /// <summary>播放次数。</summary>
    public int? PlayCount { get; init; }

    /// <summary>听过的曲目数。</summary>
    public int? SongCount { get; init; }

    /// <summary>听歌时长超过的用户百分比。海报里显示为「超过 43% 用户」。</summary>
    public int? TimeRank { get; init; }

    public string? ListenChain { get; init; }

    public string? PlayChain { get; init; }

    public string? ListenContent { get; init; }

    /// <summary>听歌时长最多的曲目名，用于 <see cref="ListenContent"/> 缺失时的补位卡。</summary>
    public string? ListenTopTrackTitle { get; init; }

    public string? PlayContent { get; init; }

    public string? ArtistName { get; init; }

    public Uri? ArtistCover { get; init; }

    public string? ArtistMusicName { get; init; }

    public Uri? ArtistMusicCover { get; init; }

    public string? ArtistContent { get; init; }

    public IReadOnlyList<ReportTrack> TopTracks { get; init; } = [];

    /// <summary>最爱专辑封面。<b>协议只给 URL</b>，没有专辑名与 id。</summary>
    public IReadOnlyList<Uri> AlbumCovers { get; init; } = [];
}

/// <summary>
/// 月报 Top 列表里的简化曲目。
/// </summary>
/// <remarks>
/// <b>没有音源档位。</b> 直接构造 <see cref="Track"/> 交给播放器会被判成「无可用音质」；
/// 播放前必须先用 <c>GetTrackAsync(Id)</c> 补全。
/// </remarks>
public sealed record ReportTrack(
    long Id,
    string Title,
    string Artist,
    string? AlbumName,
    Uri? Cover,
    long? PlayCount);
