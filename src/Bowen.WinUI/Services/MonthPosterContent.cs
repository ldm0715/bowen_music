namespace Bowen.WinUI.Services;

/// <summary>海报里的一首排行曲目。</summary>
public sealed record MonthPosterTrack(
    int Rank,
    string Title,
    string Artist,
    Uri? Cover,
    long PlayCount);

/// <summary>月报海报需要的全部数据。</summary>
/// <remarks>
/// 两种海报共用这一份内容：月度总览用封面墙与统计，歌曲总览用 <see cref="Tracks"/>。
/// 渲染层不碰 ViewModel，方便以后从别的地方复用。
/// </remarks>
public sealed record MonthPosterContent(
    string? Nickname,
    Uri? AvatarUri,
    string? UiFontFamily,
    int Year,
    int Month,
    string TopSongTitle,
    string TopSongArtist,
    Uri? TopSongCover,
    int? SongCount,
    int? PlayCount,
    int? TimeRank,
    string ListenTimeText,
    IReadOnlyList<Uri> AlbumCovers,
    IReadOnlyList<MonthPosterTrack> Tracks);
