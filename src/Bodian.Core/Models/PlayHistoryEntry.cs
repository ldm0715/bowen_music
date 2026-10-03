namespace Bodian.Core.Models;

/// <summary>
/// 「最近播放」里的一条。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么是独立类型而不是直接存 <see cref="Track"/></b>：这份数据要长期留在磁盘上，
/// 而 <see cref="Track"/> 是对外的展示模型，字段会随界面需求变。一旦给它加一个
/// <c>required</c> 属性，旧的历史文件就会反序列化失败、整段历史凭空消失。
/// 独立类型把「存储格式」与「展示模型」隔开，且 JSON 里只有这一页真正要用的字段。
/// </para>
/// <para>
/// <b>存的是快照，不是引用。</b> 曲目可能下架、封面地址可能失效，那时列表仍然要把名字显示出来 ——
/// 所以标题/歌手/专辑/时长在记录的那一刻就固化下来，而不是每次展示再去查一遍。
/// </para>
/// <para>
/// <b>它与官方客户端的历史不互通。</b> 官方把历史存在自己的 SQLite（<c>songDB.db</c>）里，
/// 本项目不读别人的库，也从零开始积累。
/// </para>
/// </remarks>
public sealed record PlayHistoryEntry
{
    /// <summary>musicId。</summary>
    public required long MusicId { get; init; }

    /// <summary>这次播放发生的时间（UTC）。</summary>
    public required DateTimeOffset PlayedAt { get; init; }

    /// <summary>曲名快照。</summary>
    public required string Title { get; init; }

    /// <summary>展示用的艺人串快照。</summary>
    public string ArtistText { get; init; } = "";

    public string? AlbumName { get; init; }

    /// <summary>
    /// 专辑 id 快照。0 表示写下这条记录时服务端没给。
    /// </summary>
    /// <remarks>
    /// 早于这个字段的历史文件里没有这个键，反序列化后是 0 ——「查看专辑」对这类旧条目
    /// 表现为禁用，不会崩。
    /// </remarks>
    public long AlbumId { get; init; }

    public Uri? CoverImage { get; init; }

    /// <summary>时长，单位秒。存秒是为了让 JSON 可读、且不依赖 <see cref="TimeSpan"/> 的序列化格式。</summary>
    public int DurationSeconds { get; init; }

    /// <summary>
    /// 当时这首歌可播的档位。
    /// </summary>
    /// <remarks>
    /// <b>存下来是为了能再次点播</b>：取音源要按档位算 <c>br</c>，没有它就播不了。
    /// 档位可能已经变了，但那由服务端降级兜住（<c>AudioSource.WasDowngraded</c> 会如实上报）。
    /// </remarks>
    public AudioQuality[] AvailableQualities { get; init; } = [];

    public AudioVariant[] AudioVariants { get; init; } = [];

    /// <summary>当时服务端标记的付费要求。**只用于展示**，权限永远由 checkRight 裁决。</summary>
    public bool RequiresVip { get; init; }

    public bool RequiresPurchase { get; init; }

    /// <summary>从「这次真正播起来了」的曲目造一条记录。</summary>
    public static PlayHistoryEntry From(Track track, DateTimeOffset playedAt)
    {
        ArgumentNullException.ThrowIfNull(track);

        return new PlayHistoryEntry
        {
            MusicId = track.Id,
            PlayedAt = playedAt,
            Title = track.Title,
            ArtistText = track.ArtistText,
            AlbumName = track.AlbumName,
            AlbumId = track.AlbumId,
            CoverImage = track.CoverImage,
            DurationSeconds = (int)track.Duration.TotalSeconds,
            AvailableQualities = track.AvailableQualities.Where(q => Enum.IsDefined(q)).Distinct().OrderDescending().ToArray(),
            AudioVariants = track.AudioVariants.Where(AudioQualityTable.IsSupportedVariant).ToArray(),
            RequiresVip = track.RequiresVip,
            RequiresPurchase = track.RequiresPurchase,
        };
    }

    /// <summary>
    /// 还原成可展示、可点播的曲目。
    /// </summary>
    /// <remarks>
    /// <b>艺人明细与歌词轨信息是空的</b>：artistId 这份快照没存（要「查看歌手」只能现查一次详情），
    /// 歌词轨也要现查。专辑 id 则存了 —— 只多一个数字，却能让「查看专辑」在这一页直接用。
    /// 歌词轨为 <c>null</c> 时取词会走「先试逐字、空了再退逐行」的兜底，是正确的降级。
    /// </remarks>
    public Track ToTrack() => new()
    {
        Id = MusicId,
        Title = Title,
        ArtistText = ArtistText,
        AlbumName = AlbumName,
        AlbumId = AlbumId,
        CoverImage = CoverImage,
        Duration = TimeSpan.FromSeconds(DurationSeconds),
        AvailableQualities = AvailableQualities.Where(q => Enum.IsDefined(q)).Distinct().OrderDescending().ToArray(),
        AudioVariants = AudioVariants.Where(AudioQualityTable.IsSupportedVariant).ToArray(),
        RequiresVip = RequiresVip,
        RequiresPurchase = RequiresPurchase,
    };
}
