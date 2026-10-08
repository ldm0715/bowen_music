namespace Bodian.Core.Models;

/// <summary>
/// 重启后要恢复的播放列表状态。
/// </summary>
/// <remarks>
/// <para>
/// <b>「快照」这个名字是刻意的：它随时可以丢。</b> 丢了只让用户重排一次队，不像播放历史那样是
/// 不可再生的记录 —— 所以读取失败一律退回默认，绝不抛。
/// </para>
/// <para>
/// <b>开关与条目存在同一个文件里</b>：开关的语义就是「这份文件算不算数」，两者必须一起原子写。
/// 拆成两份的话，「关掉开关」会出现「开关关了但条目还在」的中间态，而那个中间态在重新打开时会把
/// 旧队列复活 —— 正是关开关的人明确不要的行为。
/// </para>
/// </remarks>
public sealed record PlayQueueSnapshot
{
    /// <summary>
    /// 「重启后恢复播放列表」这个开关。<b>默认开</b>——默认关的话，用户放了一堆歌重启后什么都不见了，
    /// 会当成 bug 而不是「有个开关我没打开」。
    /// </summary>
    public bool RestoreEnabled { get; init; } = true;

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
