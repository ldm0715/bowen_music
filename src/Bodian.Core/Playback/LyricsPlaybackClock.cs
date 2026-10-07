namespace Bodian.Core.Playback;

/// <summary>将播放引擎低频上报的位置转换为连续、单向推进的歌词时间。</summary>
/// <remarks>
/// 小幅校正从当前已显示的位置接续，在有限的速度变化内逐步靠近音频时间；
/// 不在上报瞬间改动位置。明确跳转或大幅偏差才立即对齐。
/// 位置、时间戳、播放状态和未完成的校正放在同一个不可变锚点中，读侧无锁、无分配。
/// </remarks>
public sealed class LyricsPlaybackClock
{
    public static readonly TimeSpan JumpThreshold = TimeSpan.FromMilliseconds(400);
    public static readonly TimeSpan SmoothWindow = TimeSpan.FromMilliseconds(200);
    private const double MaximumRateAdjustment = 0.25;
    private readonly TimeProvider _time;

    private sealed record Anchor(
        long PositionTicks,
        long Timestamp,
        bool Playing,
        long CorrectionTicks = 0,
        long CorrectionDurationTicks = 0);

    private Anchor _anchor;
    private long _durationTicks;
    private long _jumpCount;

    public LyricsPlaybackClock(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
        _anchor = new Anchor(0, time.GetTimestamp(), Playing: false);
    }

    public TimeSpan Position
        => TimeSpan.FromTicks(ClampPositionTicks(PositionTicksAt(Volatile.Read(ref _anchor), _time.GetTimestamp())));

    public long JumpCount => Interlocked.Read(ref _jumpCount);
    public bool IsPlaying => Volatile.Read(ref _anchor).Playing;

    public void Sync(TimeSpan position, bool force = false)
    {
        var now = _time.GetTimestamp();
        var anchor = Volatile.Read(ref _anchor);
        var presented = ClampPositionTicks(PositionTicksAt(anchor, now));
        var observed = ClampPositionTicks(position.Ticks);
        var deviation = observed - presented;

        if (force || Math.Abs(deviation) > JumpThreshold.Ticks)
        {
            SetAnchor(observed, now, anchor.Playing);
            Interlocked.Increment(ref _jumpCount);
            return;
        }

        if (!anchor.Playing)
        {
            SetAnchor(observed, now, playing: false);
            return;
        }

        var correctionDuration = Math.Max(SmoothWindow.Ticks,
            (long)Math.Ceiling(Math.Abs(deviation) / MaximumRateAdjustment));
        Volatile.Write(ref _anchor, new Anchor(presented, now, Playing: true, deviation, correctionDuration));
    }

    public void SetPlaying(bool playing)
    {
        var anchor = Volatile.Read(ref _anchor);
        if (anchor.Playing == playing) return;
        var now = _time.GetTimestamp();
        SetAnchor(ClampPositionTicks(PositionTicksAt(anchor, now)), now, playing);
    }

    public void SetDuration(TimeSpan duration)
        => Interlocked.Exchange(ref _durationTicks, duration > TimeSpan.Zero ? duration.Ticks : 0);

    public void Reset()
    {
        SetAnchor(0, _time.GetTimestamp(), playing: false);
        Interlocked.Increment(ref _jumpCount);
    }

    private long PositionTicksAt(Anchor anchor, long timestamp)
    {
        if (!anchor.Playing) return anchor.PositionTicks;
        var elapsed = Math.Max(0, _time.GetElapsedTime(anchor.Timestamp, timestamp).Ticks);
        var correctionProgress = anchor.CorrectionDurationTicks > 0
            ? Math.Min(1, elapsed / (double)anchor.CorrectionDurationTicks) : 0;
        return anchor.PositionTicks + elapsed + (long)(anchor.CorrectionTicks * correctionProgress);
    }

    private long ClampPositionTicks(long ticks)
    {
        var duration = Interlocked.Read(ref _durationTicks);
        return duration > 0 ? Math.Clamp(ticks, 0, duration) : Math.Max(0, ticks);
    }

    private void SetAnchor(long positionTicks, long timestamp, bool playing)
        => Volatile.Write(ref _anchor, new Anchor(positionTicks, timestamp, playing));
}
