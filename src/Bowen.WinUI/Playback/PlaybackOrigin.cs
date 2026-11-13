namespace Bowen.WinUI.Playback;

/// <summary>
/// 当前播放队列是从哪种集合点进来的。
/// </summary>
/// <remarks>
/// 只用于播放历史上报：从歌单/专辑详情开始播放时，把整个集合也记进账号的「最近播放」。
/// 枚举值与 <c>playlist/history/add</c> 的 <c>sourceType</c> 一致。
/// </remarks>
public enum PlaybackCollectionKind
{
    /// <summary>歌单。</summary>
    Playlist = 2,

    /// <summary>专辑。</summary>
    Album = 3,
}

/// <summary>
/// 播放来源：集合种类、集合 id、以及集合对象自身的 <c>sourceType</c>。
/// </summary>
/// <param name="Kind">歌单还是专辑。</param>
/// <param name="Id">歌单 / 专辑 id。</param>
/// <param name="SourceType">
/// 歌单 / 专辑对象自身的 <c>sourceType</c>，上报时作为条目的 <c>playlistType</c>。
/// 自建歌单实测为 5，专辑为 6，公开歌单可能是 4 或 13 —— 必须透传，不能写死。
/// </param>
public sealed record PlaybackOrigin(PlaybackCollectionKind Kind, long Id, int SourceType)
{
    /// <summary>三个值都有效才能上报。</summary>
    public bool IsValid => Id > 0 && SourceType > 0 && Enum.IsDefined(Kind);
}
