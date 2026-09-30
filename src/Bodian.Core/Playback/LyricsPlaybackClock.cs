namespace Bodian.Core.Playback;

/// <summary>
/// 把播放引擎的位置插值成每帧连续的位置。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么需要它。</b> 引擎的位置上报是 <b>5Hz</b>（<c>LibMpvPlaybackService</c> 由 libmpv 的
/// tick 驱动，再按 200ms 节流），而逐字歌词要按帧推进。直接拿上报值去渲染，高亮会一跳一跳。
/// </para>
/// <para>
/// <b>机制。</b> 内部只有一个「锚点」（位置 + 时间戳 + 是否在播），外加一个「修正量」。
/// 每次收到引擎上报：
/// </para>
/// <list type="bullet">
/// <item>偏差小 → 不动锚点，只把偏差记成修正量；读侧按时间比例把它衰减掉。</item>
/// <item>偏差大（seek）→ 认定是跳变，换锚点、修正量归零。</item>
/// </list>
/// <para>
/// <b>为什么这样写不会累积漂移。</b> 算偏差时用的预测值<b>不含修正量</b>，所以每次上报
/// 算出的偏差天然包含上一次没消化完的残差，输出既连续又不会越走越偏。
/// </para>
/// <para>
/// <b>线程约定。</b> <see cref="Position"/> 会被渲染线程频繁读；其余成员只从 UI 线程调。
/// 渲染线程<b>零写入</b> —— 锚点是不可变对象，通过引用交换发布。
/// </para>
/// </remarks>
public sealed class LyricsPlaybackClock
{
    /// <summary>偏差超过它就认定是 seek，硬对齐而不是平滑消化。</summary>
    public static readonly TimeSpan JumpThreshold = TimeSpan.FromMilliseconds(400);

    /// <summary>修正量的消化窗口。窗口内线性衰减到零。</summary>
    public static readonly TimeSpan SmoothWindow = TimeSpan.FromMilliseconds(200);

    private readonly TimeProvider _time;

    /// <summary>不可变的锚点。读侧只读它，不做任何写入。</summary>
    private sealed record Anchor(long PositionTicks, long Timestamp, bool Playing);

    private Anchor _anchor;
    private long _correctionTicks;
    private long _correctionSetAt;
    private long _durationTicks;
    private long _jumpCount;

    public LyricsPlaybackClock(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);

        _time = time;
        _anchor = new Anchor(0, time.GetTimestamp(), Playing: false);
        _correctionSetAt = _anchor.Timestamp;
    }

    /// <summary>
    /// 当前应当渲染的位置。播放中按单调时间推进，暂停时冻结。
    /// </summary>
    /// <remarks>任意线程可读，无锁、无分配。</remarks>
    public TimeSpan Position
    {
        get
        {
            var anchor = Volatile.Read(ref _anchor);
            var now = _time.GetTimestamp();

            var elapsed = anchor.Playing ? _time.GetElapsedTime(anchor.Timestamp, now) : TimeSpan.Zero;
            var ticks = anchor.PositionTicks + elapsed.Ticks;

            var correction = Interlocked.Read(ref _correctionTicks);
            if (correction != 0)
            {
                var setAt = Interlocked.Read(ref _correctionSetAt);
                var since = _time.GetElapsedTime(setAt, now).Ticks;
                var alpha = SmoothWindow.Ticks <= 0
                    ? 1.0
                    : Math.Clamp(since / (double)SmoothWindow.Ticks, 0, 1);

                ticks += (long)(correction * (1 - alpha));
            }

            var duration = Interlocked.Read(ref _durationTicks);

            if (ticks < 0)
            {
                ticks = 0;
            }

            if (duration > 0 && ticks > duration)
            {
                ticks = duration;
            }

            return TimeSpan.FromTicks(ticks);
        }
    }

    /// <summary>硬跳（seek、切歌）累计次数。渲染层据此把滚动与浮动切到瞬移模式。</summary>
    public long JumpCount => Interlocked.Read(ref _jumpCount);

    /// <summary>
    /// 用引擎上报的位置校正时钟。
    /// </summary>
    /// <param name="position">引擎给的权威位置。</param>
    /// <param name="force">
    /// 强制硬对齐。拖动进度条结束、切歌这类「明确知道位置变了」的场合传 <c>true</c>，
    /// 免得落进平滑消化、看上去像慢慢滑过去。
    /// </param>
    public void Sync(TimeSpan position, bool force = false)
    {
        var now = _time.GetTimestamp();
        var anchor = Volatile.Read(ref _anchor);

        // 预测值不含修正量 —— 这样 delta 天然含上次的残差，不会累积漂移。
        var elapsed = anchor.Playing ? _time.GetElapsedTime(anchor.Timestamp, now) : TimeSpan.Zero;
        var predicted = anchor.PositionTicks + elapsed.Ticks;
        var delta = position.Ticks - predicted;

        if (force || Math.Abs(delta) > JumpThreshold.Ticks)
        {
            SetAnchor(position.Ticks, now, anchor.Playing);
            Interlocked.Increment(ref _jumpCount);
            return;
        }

        Interlocked.Exchange(ref _correctionTicks, delta);
        Interlocked.Exchange(ref _correctionSetAt, now);
    }

    /// <summary>
    /// 播放状态变化。暂停时冻结，恢复时从冻结处继续。
    /// </summary>
    /// <remarks>
    /// <b>锚点取的是当前输出位置，不是引擎上报的位置。</b> 上报值可能滞后一个周期
    /// （最长 200ms），直接拿它当锚点会在暂停的瞬间看到回跳。
    /// </remarks>
    public void SetPlaying(bool playing)
    {
        var anchor = Volatile.Read(ref _anchor);

        if (anchor.Playing == playing)
        {
            return;
        }

        SetAnchor(Position.Ticks, _time.GetTimestamp(), playing);
    }

    /// <summary>曲目时长，用来把输出钳在 <c>[0, 时长]</c>。传非正值表示不限制。</summary>
    public void SetDuration(TimeSpan duration)
        => Interlocked.Exchange(ref _durationTicks, duration > TimeSpan.Zero ? duration.Ticks : 0);

    /// <summary>切歌：归零并等下一次 <see cref="Sync"/>。</summary>
    public void Reset()
    {
        SetAnchor(0, _time.GetTimestamp(), playing: false);
        Interlocked.Increment(ref _jumpCount);
    }

    private void SetAnchor(long positionTicks, long timestamp, bool playing)
    {
        Volatile.Write(ref _anchor, new Anchor(positionTicks, timestamp, playing));
        Interlocked.Exchange(ref _correctionTicks, 0);
        Interlocked.Exchange(ref _correctionSetAt, timestamp);
    }
}
