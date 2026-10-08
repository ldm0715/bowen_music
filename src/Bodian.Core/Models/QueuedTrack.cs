namespace Bodian.Core.Models;

/// <summary>
/// 播放队列里一条曲目的快照。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么不直接存 <see cref="Track"/></b>：与 <see cref="PlayHistoryEntry"/> 同一条理由 ——
/// <see cref="Track"/> 是对外的展示模型，字段会随界面需求变，一旦给它加一个 <c>required</c> 属性，
/// 磁盘上的旧队列就会反序列化失败、整条队列凭空消失。独立类型把「存储格式」与「展示模型」隔开。
/// </para>
/// <para>
/// <b>只有 <see cref="Id"/> 是 <c>required</c>，其余一律给默认值。</b> 这比
/// <see cref="PlayHistoryEntry"/> 更保守：队列可能有几百条，任何一个字段以后被改名都不该让整条队列
/// 一条不剩 —— 少几首比全没了好得多。加载方按 <c>Id &lt;= 0</c> 或空标题丢弃坏项。
/// </para>
/// <para>
/// <b>与 <see cref="PlayHistoryEntry"/> 有数十行重复的 <c>From</c> / <c>ToTrack</c>，这是刻意保留的。</b>
/// 抽公共基类就得动 <c>history.json</c> 的嵌套结构（那是格式破坏），而两份文件的字段本来就该各自演进：
/// 历史以后可能加「播放次数」，队列以后可能加「加入队列的时间」。
/// </para>
/// </remarks>
public sealed record QueuedTrack
{
    /// <summary>musicId。真实值会超出 int32。</summary>
    public required long Id { get; init; }

    /// <summary>曲名快照。</summary>
    public string Title { get; init; } = "";

    /// <summary>展示用的艺人串快照。</summary>
    public string ArtistText { get; init; } = "";

    public string? AlbumName { get; init; }

    /// <summary>专辑 id 快照。0 表示写下这条时服务端没给。</summary>
    public long AlbumId { get; init; }

    public Uri? CoverImage { get; init; }

    /// <summary>时长，单位秒。存秒是为了 JSON 可读、且不依赖时长类型的序列化格式。</summary>
    public int DurationSeconds { get; init; }

    /// <summary>当时这首歌可播的档位。取音源要按档位算 br，没有它就播不了。</summary>
    public AudioQuality[] AvailableQualities { get; init; } = [];

    /// <summary>当时这首歌声明的真实音源明细，按它构造 br。</summary>
    public AudioVariant[] AudioVariants { get; init; } = [];

    /// <summary>当时服务端标记的付费要求。<b>只用于展示</b>，权限永远由 checkRight 裁决。</summary>
    public bool RequiresVip { get; init; }

    public bool RequiresPurchase { get; init; }

    /// <summary>写下这条时这首歌有没有 MV。不存就永远不亮。</summary>
    public bool HasMv { get; init; }

    /// <summary>从队列里的一首歌造一条快照。</summary>
    public static QueuedTrack From(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);

        return new QueuedTrack
        {
            Id = track.Id,
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
            HasMv = track.HasMv,
        };
    }

    /// <summary>
    /// 还原成可播的曲目。
    /// </summary>
    /// <remarks>
    /// <b>艺人明细与歌词轨信息是空的</b>：这两项没存。消费方都有降级路径 ——
    /// 播放条没有艺人明细会显示拼好的艺人串，点击可现查一次；歌词轨为空时取词走
    /// 「先试逐字、空了再退逐行」的兜底；专辑 id 为 0 时「查看专辑」按不可点处理。
    /// 与 <see cref="PlayHistoryEntry.ToTrack"/> 同款。
    /// </remarks>
    public Track ToTrack() => new()
    {
        Id = Id,
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
        HasMv = HasMv,
    };
}
