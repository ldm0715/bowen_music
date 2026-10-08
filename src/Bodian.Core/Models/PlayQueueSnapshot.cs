namespace Bodian.Core.Models;

/// <summary>
/// 重启后要恢复的播放列表状态。<b>一个作用域一个文件</b>（<c>accounts\&lt;scope&gt;\queue.json</c>）。
/// </summary>
/// <remarks>
/// <para>
/// <b>「快照」这个名字是刻意的：它随时可以丢。</b> 丢了只让用户重排一次队，不像播放历史那样是
/// 不可再生的记录 —— 所以读取失败一律退回默认，绝不抛。
/// </para>
/// <para>
/// <b>文件里不再有「开关」。</b> 「记住播放列表」是「这个人怎么用这个软件」，跨账号共用，
/// 所以它待在 <c>playback.json</c> 里；关掉时会把各作用域的这份文件一并删掉，见
/// <see cref="Abstractions.IPlayQueueSnapshotStore.DeleteAllScopes"/>。以前把开关塞在这里，
/// 是为了「关掉开关」能连条目一起原子抹掉 —— 那个理由随多账号一起失效：一次要抹好几个账号的文件，
/// 本来就做不到单文件原子。
/// </para>
/// </remarks>
public sealed record PlayQueueSnapshot
{
    /// <summary>队列本体，顺序即面板显示顺序。</summary>
    public QueuedTrack[] Items { get; init; } = [];

    /// <summary>当前曲目 id。<b>权威</b>——坏条目被过滤后它比下标可靠。</summary>
    public long CurrentTrackId { get; init; }

    /// <summary>当前曲目的显示下标。<see cref="CurrentTrackId"/> 找不到时用它兜底。</summary>
    public int CurrentIndex { get; init; } = -1;

    /// <summary>当前曲目停在第几秒。队列里<b>只有这一首</b>记进度。</summary>
    public double PositionSeconds { get; init; }

    /// <summary>默认状态：空队列、开关开。</summary>
    public static PlayQueueSnapshot Default { get; } = new();
}
