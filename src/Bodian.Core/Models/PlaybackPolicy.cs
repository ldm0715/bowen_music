namespace Bodian.Core.Models;

/// <summary>
/// 「这一次播放能不能被当成完整的一首歌」—— 试听规则的唯一落点。
/// </summary>
/// <remarks>
/// <para>
/// <b>试听片段不得被当成完整歌曲</b>，这条要求有三个消费方：播放历史与 SMTC（P3）、
/// 音频缓存（P9）、以及 UI 的进度条上限。三处各写一遍判断迟早会漏，所以收在这一个类里：
/// 要判断「能不能按整曲落盘」就问 <see cref="MayPersistAsCompleteTrack"/>。
/// </para>
/// <para>
/// 停止试听有<b>两道防线</b>：主防线是把 <c>end=</c> 作为 per-file 选项交给 libmpv，
/// 由引擎在终点精确停住；<see cref="ShouldStopAt"/> 是次防线，供 UI 在收到进度回调时复查，
/// 同时它是「试听结束」这个界面事件的来源。
/// </para>
/// </remarks>
public sealed class PlaybackPolicy
{
    private PlaybackPolicy(PlaybackResolution resolution) => Resolution = resolution;

    /// <summary>从一次点播的结论构造策略。</summary>
    public static PlaybackPolicy For(PlaybackResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        return new PlaybackPolicy(resolution);
    }

    /// <summary>这次的播放结论。</summary>
    public PlaybackResolution Resolution { get; }

    /// <summary>这次是试听片段。</summary>
    public bool IsAudition => Resolution is PlaybackResolution.AuditionOnly;

    /// <summary>这次是完整歌曲（有完整播放权限且拿到了音源）。</summary>
    public bool IsCompleteTrack => Resolution is PlaybackResolution.Playable;

    /// <summary>
    /// 能否按「完整曲目」持久化 —— 播放历史、缓存、统计都要先问这里。
    /// </summary>
    public bool MayPersistAsCompleteTrack => IsCompleteTrack;

    /// <summary>试听终点；完整播放为 <c>null</c>。</summary>
    public TimeSpan? StopAt => Resolution is PlaybackResolution.AuditionOnly audition
        ? audition.End
        : null;

    /// <summary>当前位置是否已到试听终点（次防线，主防线是引擎的 <c>end=</c> 选项）。</summary>
    public bool ShouldStopAt(TimeSpan position) => StopAt is { } stopAt && position >= stopAt;
}
