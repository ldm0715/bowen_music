using System.Globalization;
using System.Diagnostics;
using Bowen.Core.Playback;
using Bowen.Core.Services;
using Bowen.Core.Threading;
using HanumanInstitute.LibMpv;
using HanumanInstitute.LibMpv.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;

namespace Bowen.WinUI.Playback;

/// <summary>
/// libmpv 实现。**全项目唯一碰 <see cref="MpvContext"/> 的地方。**
/// </summary>
/// <remarks>
/// <para>
/// 纯音频 headless，不建渲染链。事件循环用 <see cref="MpvContext"/> 的默认构造 —— 它内部是
/// <c>MpvSimpleEventLoop</c>，自己起一个 Task 跑 <c>mpv_wait_event</c>，够用。
/// 注意 <c>MpvContext</c> 是空 partial 类，**没有转发带参构造**，事件循环模式无法定制。
/// </para>
/// <para>
/// 所有事件都在 mpv 自己的线程上触发，必须经 <see cref="Raise"/> marshalling 回 UI 线程。
/// 漏掉的表现是随机崩溃而不是必现，所以集中在<b>唯一出口</b>处理，不靠调用点自觉。
/// </para>
/// </remarks>
public sealed class LibMpvPlaybackService : IPlaybackService
{
    /// <summary>
    /// 进度上报的最小间隔。libmpv 的 tick 很密，每次都推给 UI 会把主线程灌满，
    /// 而进度条肉眼也不需要更高频率。
    /// </summary>
    private static readonly TimeSpan PositionReportInterval = TimeSpan.FromMilliseconds(200);

    private readonly ILogger<LibMpvPlaybackService> _logger;
    private readonly LatestValueDispatcher<PlaybackPositionChangedEventArgs> _positionEvents;

    /// <summary>
    /// UI 线程的 dispatcher。<b>首次播放时才取</b>，不在构造函数里取 ——
    /// 这个服务是 DI 单例，构造发生在容器第一次解析它的那一刻，那时取到的队列未必可靠。
    /// 取错的表现很隐蔽：事件会在 mpv 线程上直接操作 UI，抛跨线程异常把事件循环整个干掉，
    /// 之后进度、状态一律不再更新，且没有任何崩溃迹象。
    /// </summary>
    private DispatcherQueue? _dispatcher;

    private MpvContext? _mpv;
    private long _lastPropertyPoll;
    private string? _unavailableReason;
    private PlaybackState _state = PlaybackState.Idle;
    private TimeSpan _position;
    private TimeSpan _duration;
    /// <summary>
    /// 上次上报的位置，用于节流。<b>必须是可空的</b>。
    /// </summary>
    /// <remarks>
    /// 别用 <c>TimeSpan.MinValue</c> 当哨兵值：它约等于 -1067 万天，
    /// 拿它参与 <c>position - last</c> 会**算术溢出**并抛 <see cref="OverflowException"/>。
    /// 那个异常从 libmpv 驱动的事件循环 Task 里逃出去会把 Task 打死，
    /// 此后所有 mpv 事件（进度、FileLoaded、EndFile）都不再分发，
    /// 而进程毫无异常迹象 —— 极难定位。
    /// </remarks>
    private TimeSpan? _lastReported;
    private double _volume = 100;
    private string? _audioFilter;
    private bool _disposed;
    private TaskCompletionSource? _pendingLoad;

    /// <summary>有跳转在等声音接上（1）或没有（0）。<b>OnTick 在 mpv 线程读，必须用 Interlocked。</b></summary>
    private int _seekPending;

    /// <summary>等待中的跳转目标。</summary>
    private long _seekTargetTicks;

    /// <summary>发出跳转时的 <see cref="Stopwatch"/> 时间戳，用于测「到声音接上」花了多久。</summary>
    private long _seekStartedAt;

    /// <summary>音频健康采样的时间戳，1 秒一次。</summary>
    private long _lastHealthPoll;

    /// <summary>上一次采样看到的音频输出驱动。</summary>
    private string? _lastAudioOutput;

    /// <summary>上一次采样看到的音轨选择。</summary>
    private string? _lastAudioTrack;

    /// <summary>上一次采样看到的「因缓冲欠载暂停」。</summary>
    private bool? _lastCachePaused;

    /// <summary>
    /// 排查用的 mpv 日志级别。设置环境变量 <c>BOWEN_MPV_LOG</c>（例如 <c>v</c> 或 <c>info</c>）
    /// 后启动，mpv 自己的日志会以 Information 级别写进应用日志；不设置时行为和原来一致。
    /// </summary>
    private static readonly string? MpvLogLevel = Environment.GetEnvironmentVariable("BOWEN_MPV_LOG");

    /// <summary>
    /// 位置连续这么久没变就算卡死。10 秒远超正常缓冲停顿，又短到用户还能忍。
    /// </summary>
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(10);

    /// <summary>串行化 loadfile：正常换歌和卡死后的自动重开不能同时进行。</summary>
    private readonly SemaphoreSlim _loadSerial = new(1, 1);

    /// <summary>当前加载的音源；卡死后原地重开要用。</summary>
    private PlaybackSource? _source;

    /// <summary>每次用户侧 <see cref="LoadAsync"/> 递增，让排队中的自动恢复作废。</summary>
    private long _loadVersion;

    /// <summary>自动恢复是否正在进行（1 表示是）。</summary>
    private int _recovering;

    /// <summary>进行中的断线重连；没有时为 <c>null</c>。用户换歌/停止时取消。</summary>
    private CancellationTokenSource? _recoveryCts;

    /// <summary>
    /// 当前这首已经因为「疑似截断」重连过几次。
    /// </summary>
    /// <remarks>
    /// 服务端时长报长了的时候，正常播完也会被判成截断；重连几次还是同样的结局就认了，
    /// 当成正常结束切下一首，不能无限重试。
    /// </remarks>
    private int _truncationRecoveries;

    /// <summary>上次看到的播放位置（ticks，哨兵 <see cref="long.MinValue"/>）。</summary>
    private long _progressAnchorTicks = long.MinValue;

    /// <summary>位置最后一次变化的时间戳。</summary>
    private long _progressAt;

    /// <summary>是否已经发过「拉流停住」事件（1 表示是）。只在状态翻转时发。</summary>
    private int _stalled;

    /// <summary>本次缓冲欠载开始的时刻；没有欠载时为 0。</summary>
    private long _cachePausedAt;

    /// <summary>欠载持续这么久才算拉流停住，短抖动不弹提示。</summary>
    private static readonly TimeSpan BufferingNoticeDelay = TimeSpan.FromSeconds(3);

    /// <summary>
    /// 位置离结尾还差这么多却收到「正常播完」，就当成断流而不是真的放完。
    /// </summary>
    /// <remarks>
    /// 进度每 200 毫秒上报一次，正常播完时最后一条离结尾不会超过这个数；
    /// 差出好几秒只可能是流被提前截断。
    /// </remarks>
    private static readonly TimeSpan TruncatedStreamTolerance = TimeSpan.FromSeconds(3);

    /// <summary>越过目标这么多才算声音接上：mpv 会先把位置报成目标值，原地不动。</summary>
    private static readonly long SeekSettledTicks = TimeSpan.TicksPerMillisecond * 50;

    public LibMpvPlaybackService(ILogger<LibMpvPlaybackService>? logger = null)
    {
        _logger = logger ?? NullLogger<LibMpvPlaybackService>.Instance;
        _positionEvents = new(action =>
        {
            if (_dispatcher is null || _dispatcher.HasThreadAccess) { action(); return true; }
            return _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () => action());
        }, position => { if (!_disposed) PositionChanged?.Invoke(this, position); });
    }

    public bool IsAvailable => _mpv is not null;

    public string? UnavailableReason => _unavailableReason;

    public PlaybackState State => _state;

    public TimeSpan Position => _position;

    public TimeSpan Duration => _duration;

    public double Volume => _volume;

    public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;

    public event EventHandler<PlaybackPositionChangedEventArgs>? PositionChanged;

    public event EventHandler<SeekPendingEventArgs>? SeekPending;

    public event EventHandler? Ended;

    public event EventHandler<PlaybackStalledEventArgs>? Stalled;

    public event EventHandler<PlaybackFailedEventArgs>? Failed;

    public async Task LoadAsync(PlaybackSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();

        if (!EnsureInitialized() || _mpv is null)
        {
            throw new InvalidOperationException(_unavailableReason ?? "音频引擎不可用");
        }

        // 先记账再排队：排队期间如果卡死恢复正在重开，这次换歌要让它作废。
        Interlocked.Increment(ref _loadVersion);
        CancelRecovery();
        // 用户点了别的歌：上一首那条「正在重连」提示作废。
        ClearStalled();

        await _loadSerial.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            await LoadCoreAsync(source, cancellationToken, TimeSpan.FromSeconds(30)).ConfigureAwait(true);
        }
        finally
        {
            _loadSerial.Release();
        }
    }

    /// <summary>
    /// 真正发 <c>loadfile</c>。调用方必须已经持有 <see cref="_loadSerial"/>。
    /// </summary>
    /// <param name="announceLoading">
    /// 是否对外报 <see cref="PlaybackState.Loading"/> 并把进度归零。
    /// <b>断线重连传 <c>false</c></b>：界面要一直停在原来的歌和进度上，只挂一条重连提示，
    /// 不能让进度条跳回 0、也不能让歌词页按「换歌」重置。
    /// </param>
    private async Task LoadCoreAsync(
        PlaybackSource source,
        CancellationToken cancellationToken,
        TimeSpan timeout,
        bool announceLoading = true)
    {
        if (_mpv is not { } mpv)
        {
            throw new InvalidOperationException(_unavailableReason ?? "音频引擎不可用");
        }

        _source = source;

        // ★ 不能用 mpv.LoadFile(path, extraArgs:)：那个重载把 extra 参数从 index 2 开始写，
        //   覆盖了 flags 位（应为 3），mpv 会报 "Invalid flag for option loadfile" 并静默放弃加载
        //   （症状是 idle-active 恒为 1、读不到 time-pos）。自己发命令，用 mpv 原生的
        //   loadfile <url> <flags> <index> <options> 形式。
        var args = new List<object?> { "loadfile", source.StreamUrl.ToString(), "replace" };

        var options = new List<string> { $"pause={(source.InitiallyPaused ? "yes" : "no")}" };

        if (source.Start is { } start && start > TimeSpan.Zero)
        {
            options.Add($"start={Seconds(start)}");
        }

        if (source.End is { } end)
        {
            options.Add($"end={Seconds(end)}");
        }

        if (options.Count > 0)
        {
            args.Add("0");
            args.Add(string.Join(",", options));
        }

        // 地址是凭据，不进日志；标题可以。
        _logger.LogInformation("加载音频：{Title}", source.Title ?? "(无标题)");

        _lastReported = null;
        if (announceLoading)
        {
            _position = TimeSpan.Zero;
            _duration = TimeSpan.Zero;
            _truncationRecoveries = 0;
        }

        // 换文件后健康状态要重新采样，别拿上一首的 ao/aid 当基线。
        _lastHealthPoll = 0;
        _lastAudioOutput = null;
        _lastAudioTrack = null;
        _lastCachePaused = null;

        // 新文件从头开始，上一首没等到位的跳转标记不能再留着。
        Volatile.Write(ref _seekPending, 0);
        // 卡死判定以新文件的位置为基线重新起算。
        Interlocked.Exchange(ref _progressAnchorTicks, long.MinValue);
        Interlocked.Exchange(ref _progressAt, 0);
        Interlocked.Exchange(ref _cachePausedAt, 0);

        if (announceLoading)
        {
            SetState(PlaybackState.Loading);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.Exchange(ref _pendingLoad, completion)?.TrySetCanceled();
        try
        {
            mpv.SetPropertyFlag("pause", source.InitiallyPaused);
            // `af` 在换文件时不会自动丢，但这条链可能是在引擎初始化前设好的；
            // 每次 loadfile 前重写一次，保证第一首就带上音效。
            ApplyAudioFilter(mpv);
            mpv.RunCommand(null, args.ToArray());
            await completion.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            Interlocked.CompareExchange(ref _pendingLoad, null, completion);
        }
    }

    public Task PlayAsync(CancellationToken cancellationToken = default)
    {
        SetPause(paused: false);
        return Task.CompletedTask;
    }

    public Task PauseAsync(CancellationToken cancellationToken = default)
    {
        SetPause(paused: true);
        return Task.CompletedTask;
    }

    public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
    {
        if (_mpv is not { } mpv)
        {
            return Task.CompletedTask;
        }

        var seconds = Math.Max(0, position.TotalSeconds);
        var target = TimeSpan.FromSeconds(seconds);

        try
        {
            mpv.SetPropertyDouble("time-pos", seconds);
            _lastReported = null;

            // 记下目标与时刻：OnTick 里越过目标才算声音接上，顺便量出这次跳转等了多久。
            Interlocked.Exchange(ref _seekTargetTicks, target.Ticks);
            Interlocked.Exchange(ref _seekStartedAt, Stopwatch.GetTimestamp());
            Volatile.Write(ref _seekPending, 1);

            // 告诉界面「位置已经报到目标了，但声音还没接上」。歌词时钟据此先停住，
            // 否则它会从目标位置按墙钟外推，出现「歌词先滚、没声音」。
            Raise(() => SeekPending?.Invoke(this, new SeekPendingEventArgs(target)));
        }
        catch (Exception ex)
        {
            RaiseFailed($"跳转失败：{ex.Message}");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_mpv is not { } mpv)
        {
            return Task.CompletedTask;
        }

        CancelRecovery();

        try
        {
            mpv.RunCommand(null, "stop");
        }
        catch (Exception ex)
        {
            RaiseFailed($"停止失败：{ex.Message}");
        }

        return Task.CompletedTask;
    }

    public void SetVolume(double volume)
    {
        _volume = Math.Clamp(volume, 0, 100);

        if (_mpv is not { } mpv)
        {
            return;
        }

        try
        {
            mpv.SetPropertyDouble("volume", _volume);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "设置音量失败");
        }
    }

    public void SetAudioFilter(string? filter)
    {
        _audioFilter = string.IsNullOrWhiteSpace(filter) ? null : filter;

        if (_mpv is not { } mpv)
        {
            return;
        }

        ApplyAudioFilter(mpv);
    }

    public void SendAudioFilterCommand(string label, string command, string argument, string? target = null)
    {
        if (_mpv is not { } mpv)
        {
            return;
        }

        try
        {
            mpv.RunCommand(null, "af-command", label, command, argument, target ?? "all");
        }
        catch (Exception ex)
        {
            // 滤镜不在链上时 mpv 会拒绝；音效滑杆不该因此打断播放。
            _logger.LogDebug(ex, "音频滤镜命令被拒绝：{Label}/{Command} {Argument}", label, command, argument);
        }
    }

    private void ApplyAudioFilter(MpvContext mpv)
    {
        try
        {
            mpv.SetPropertyString("af", _audioFilter ?? "");
        }
        catch (Exception ex)
        {
            // 当前 libmpv 没编进链里的某个滤镜时，mpv 会拒绝整串。
            // 这里只记日志，不把播放打停；界面上的开关仍保持用户选择。
            _logger.LogWarning(ex, "设置音效滤镜链失败，已保留主音频");
            try
            {
                mpv.SetPropertyString("af", "");
            }
            catch (Exception clearException)
            {
                _logger.LogDebug(clearException, "清空音效滤镜链也失败了");
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        CancelRecovery();
        Interlocked.Exchange(ref _pendingLoad, null)?.TrySetCanceled();

        var mpv = _mpv;
        _mpv = null;

        if (mpv is null)
        {
            return ValueTask.CompletedTask;
        }

        // ★ 不等它完成。
        //   MpvContext.Dispose 内部会停事件循环并 EventLoopTask.Wait() 死等，而 mpv 在退出时
        //   常常不能及时从 mpv_wait_event 里醒过来（实测每次关闭都会卡满超时）。
        //   等它只是白白拖慢关闭；这里是进程级资源，退出后由 OS 回收。
        _ = Task.Run(() =>
        {
            try
            {
                mpv.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "释放 libmpv 时出错（进程即将退出，忽略）");
            }
        });

        return ValueTask.CompletedTask;
    }

    // ── libmpv 事件（都在 mpv 线程上）────────────────────────────────────────

    private void OnFileLoaded()
    {
        _logger.LogDebug("mpv 已加载文件");
        SetState(_mpv is { } mpv && TryReadFlag(mpv, "pause") == true ? PlaybackState.Paused : PlaybackState.Playing);
        Interlocked.Exchange(ref _pendingLoad, null)?.TrySetResult();
    }

    private void OnEndFile(MpvEndFileEventArgs e)
    {
        _logger.LogDebug("mpv 结束文件：reason={Reason} error={Error}", e.Reason, e.Error);

        // loadfile replace 的旧文件 Stop 不得覆盖正在加载的新文件状态。
        if (_pendingLoad is not null && e.Reason == MpvEndFileReason.Stop && e.Error == 0) { return; }

        // 用 Error 码判失败，而不是比 MpvEndFileReason 的名字。
        if (e.Error != 0)
        {
            // 重连尝试自己的失败：只唤醒重连循环去退避重试，不能再弹一条错误提示，
            // 否则断网期间每试一次就闪一条。
            var recoveringAttempt = Volatile.Read(ref _recovering) == 1;

            // 播着播着被打断（断网、连接被掐）不是「这首歌坏了」：
            // 不往外抛错、不换歌，原地重连并从断点继续。
            if (!recoveringAttempt && _pendingLoad is null && _source is not null && !_disposed)
            {
                BeginRecovery(_position, $"拉流中断：{DescribeError(e.Error)}");
                return;
            }

            // 真正的加载失败（点了一首新歌、地址打不开）照旧交给调用方处理，
            // 但对外文案不暴露 mpv 的内部错误码。
            if (!recoveringAttempt)
            {
                _position = TimeSpan.Zero;
                ClearStalled();
                RaiseFailed($"播放中断：{DescribeError(e.Error)}");
            }

            Interlocked.Exchange(ref _pendingLoad, null)?.TrySetException(
                new IOException($"音源加载失败：{DescribeError(e.Error)}"));
            return;
        }

        // 断流被 mpv 当成「正常播完」时，位置离结尾还差很远：
        // 这不是这一首放完了，按断流原地重连，不能顺势切到下一首。
        if (e.Reason == MpvEndFileReason.EndOfFile
            && _source is not null
            && _source.End is null
            && _duration > TimeSpan.Zero
            && _duration - _position > TruncatedStreamTolerance
            && _position > TimeSpan.Zero
            && _truncationRecoveries < 3
            && !_disposed)
        {
            _truncationRecoveries++;
            BeginRecovery(_position, "音源提前结束（疑似断流）");
            return;
        }

        _position = TimeSpan.Zero;
        ClearStalled();
        SetState(PlaybackState.Stopped);

        // ★ 只有真播完（Eof）才算「这一首结束了」。
        //   loadfile replace 与被替换掉的文件都会产生一条 EndFile(Stop)，
        //   把它当成播完会让协调器每次切歌都自动 NextAsync 一次，
        //   于是队列被自己往前冲、同一首歌被反复加载。
        if (e.Reason == MpvEndFileReason.EndOfFile)
        {
            Raise(() => Ended?.Invoke(this, EventArgs.Empty));
        }
    }

    private void OnTick()
    {
        if (_disposed || _mpv is not { } mpv)
        {
            return;
        }

        // Tick 比 UI 上报密得多；先节流，再跨原生边界读取位置/时长/状态。
        var timestamp = Stopwatch.GetTimestamp();
        if (_lastPropertyPoll != 0 && Stopwatch.GetElapsedTime(_lastPropertyPoll, timestamp).TotalMilliseconds < 100) return;
        _lastPropertyPoll = timestamp;
        // idle 状态下 time-pos 不存在，读不到是正常的。
        if (ReadDouble(mpv, "time-pos") is not { } seconds)
        {
            return;
        }

        var position = TimeSpan.FromSeconds(seconds);

        SampleAudioHealth(mpv);
        TrackProgress(mpv, position);

        // 跳转后位置会先停在目标值；越过它才说明声音真的接上了。这里顺手把等待时长记下来。
        if (Volatile.Read(ref _seekPending) == 1)
        {
            var target = Interlocked.Read(ref _seekTargetTicks);

            if (position.Ticks >= target + SeekSettledTicks
                && Interlocked.CompareExchange(ref _seekPending, 0, 1) == 1)
            {
                var elapsed = Stopwatch.GetElapsedTime(Interlocked.Read(ref _seekStartedAt)).TotalMilliseconds;
                _logger.LogInformation(
                    "跳转完成：目标 {Target:0.0} s，等待 {Elapsed:0} ms，托管堆 {Heap:0.0} MB",
                    target / (double)TimeSpan.TicksPerSecond,
                    elapsed,
                    GC.GetTotalMemory(forceFullCollection: false) / 1048576.0);
            }
        }

        if (ReadDouble(mpv, "duration") is { } durationSeconds and > 0)
        {
            _duration = TimeSpan.FromSeconds(durationSeconds);
        }

        // 只在读得到的时候同步状态。读不到就保留上一次的值 ——
        // 否则读失败会被当成「没暂停」，把 SetPause 的乐观更新覆盖回去。
        if (TryReadFlag(mpv, "pause") is { } paused)
        {
            SetState(paused ? PlaybackState.Paused : PlaybackState.Playing);
        }

        // 节流。用绝对值比较，这样 seek 造成的回退也能立刻上报。
        if (_lastReported is { } last
            && Math.Abs((position - last).TotalMilliseconds) < PositionReportInterval.TotalMilliseconds)
        {
            return;
        }

        _lastReported = position;
        _position = position;

        var duration = _duration;

        if (!_positionEvents.Post(new PlaybackPositionChangedEventArgs(position, duration)))
            _logger.LogWarning("播放进度无法投递回 UI 线程，已丢弃");
    }

    /// <summary>
    /// 记录进度，检测「位置停住但界面仍是播放中」的拉流卡死。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 典型症状：播放到已缓冲范围的右边界时，拉流连接其实已经被 CDN 关掉了，
    /// mpv 踩在死连接上既不报错也不重连 —— 声音停了、进度停了，暂停/播放完全没用。
    /// 只有重新建立一次连接才能恢复，所以这里不做「温和重试」，直接原地重开音源。
    /// </para>
    /// <para>
    /// 暂停、跳转等待中都不算卡死；10 秒阈值远大于正常缓冲停顿。
    /// </para>
    /// </remarks>
    private void TrackProgress(MpvContext mpv, TimeSpan position)
    {
        if (Interlocked.Exchange(ref _progressAnchorTicks, position.Ticks) != position.Ticks)
        {
            Interlocked.Exchange(ref _progressAt, Stopwatch.GetTimestamp());
            // 位置真的前进了，说明拉流恢复，重连提示可以收了。
            ClearStalled();
            return;
        }

        if (_disposed || _source is null || Interlocked.Read(ref _progressAt) == 0) { return; }
        if (Volatile.Read(ref _recovering) == 1) { return; }
        // 暂停时位置本来就不动，不能当卡死；暂停中的跳转也不会「越过去」。
        if (TryReadFlag(mpv, "pause") is not false) { return; }

        // 跳转迟迟不完成也是同一种卡死：位置停在目标值，等多久都不会动。
        // 往缓冲窗口外拖的那一次，就是「跳转等待」把普通的位置检测挡住的最常见入口。
        if (Volatile.Read(ref _seekPending) == 1)
        {
            if (Stopwatch.GetElapsedTime(Interlocked.Read(ref _seekStartedAt)) >= StallTimeout)
            {
                BeginRecovery(
                    TimeSpan.FromTicks(Interlocked.Read(ref _seekTargetTicks)),
                    "跳转迟迟没有完成");
            }

            return;
        }

        if (_state != PlaybackState.Playing) { return; }
        if (Stopwatch.GetElapsedTime(Interlocked.Read(ref _progressAt)) < StallTimeout) { return; }

        BeginRecovery(position, $"播放位置停住超过 {StallTimeout.TotalSeconds:0} 秒");
    }

    /// <summary>
    /// 原地重连当前音源：立即试一次，失败后按指数退避继续试，直到成功或被换歌/停止取消。
    /// </summary>
    /// <remarks>
    /// <b>重连期间界面保持「播放中」和原进度。</b>用户应该看到「网络中断，正在重新连接」，
    /// 而不是这首歌被跳过、进度被清零；网络恢复后从断点继续。
    /// 位置往后挪 0.5 秒，避开恰好卡住的缓冲边界。
    /// </remarks>
    private void BeginRecovery(TimeSpan position, string reason)
    {
        if (Interlocked.CompareExchange(ref _recovering, 1, 0) != 0) { return; }

        var source = _source;
        var version = Interlocked.Read(ref _loadVersion);
        if (source is null || _disposed)
        {
            Volatile.Write(ref _recovering, 0);
            return;
        }

        // 看门狗触发时 mpv 可能连缓冲状态都没报，所以这里也要发一次停住通知。
        MarkStalled();
        _logger.LogWarning("{Reason}；在 {Position:0.0} s 处重连音源", reason, position.TotalSeconds);

        var cts = new CancellationTokenSource();
        Interlocked.Exchange(ref _recoveryCts, cts);
        _ = Task.Run(() => ReconnectLoopAsync(source, position, version, cts));
    }

    private async Task ReconnectLoopAsync(
        PlaybackSource source,
        TimeSpan position,
        long version,
        CancellationTokenSource cts)
    {
        var token = cts.Token;
        var delay = TimeSpan.Zero;
        var attempt = 0;

        try
        {
            while (true)
            {
                await Task.Delay(delay, token).ConfigureAwait(false);
                attempt++;

                try
                {
                    await _loadSerial.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        // 排队期间用户已经切歌/重播，这次重连作废。
                        if (Interlocked.Read(ref _loadVersion) != version) { return; }

                        await LoadCoreAsync(
                            new PlaybackSource(
                                source.StreamUrl,
                                position + TimeSpan.FromMilliseconds(500),
                                source.End,
                                source.Title,
                                InitiallyPaused: _state == PlaybackState.Paused),
                            token,
                            TimeSpan.FromSeconds(15),
                            announceLoading: false).ConfigureAwait(false);
                    }
                    finally { _loadSerial.Release(); }

                    _logger.LogInformation("重连成功（第 {Attempt} 次尝试）", attempt);
                    ClearStalled();
                    return;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "第 {Attempt} 次重连失败，继续重试", attempt);
                    delay = delay == TimeSpan.Zero
                        ? TimeSpan.FromSeconds(2)
                        : TimeSpan.FromSeconds(Math.Min(30, delay.TotalSeconds * 2));
                }
            }
        }
        finally
        {
            Interlocked.CompareExchange(ref _recoveryCts, null, cts);
            // 恢复后的位置从断点开始报，等它真正前进再算。
            Interlocked.Exchange(ref _progressAnchorTicks, long.MinValue);
            Interlocked.Exchange(ref _progressAt, 0);
            Volatile.Write(ref _recovering, 0);
        }
    }

    /// <inheritdoc />
    public void CancelRecovery()
    {
        Interlocked.Exchange(ref _recoveryCts, null)?.Cancel();
        // 用户已经要换歌了，旧歌那条「正在重连」提示不该继续挂着。
        ClearStalled();
    }

    /// <summary>把 mpv 错误码翻成给用户看的说法，不把内部码直接甩到界面上。</summary>
    private static string DescribeError(int code) => code switch
    {
        -13 => "网络连接失败",
        -14 => "音频设备不可用",
        -17 => "音频格式不支持",
        _ => $"mpv 错误码 {code}",
    };

    /// <summary>
    /// 每秒采一次音频链路的状态，只在值变化时落日志。
    /// </summary>
    /// <remarks>
    /// 用来定位「没声音但进度条还在走」：libmpv 没有「用户是否真的听见」这种属性，
    /// 只能看它当时认为自己在用什么输出、选了什么音轨、是否因缓冲欠载停住。
    /// 正常播放时这几个值一条都不变，日志是安静的。
    /// </remarks>
    private void SampleAudioHealth(MpvContext mpv)
    {
        var timestamp = Stopwatch.GetTimestamp();
        if (_lastHealthPoll != 0 && Stopwatch.GetElapsedTime(_lastHealthPoll, timestamp).TotalMilliseconds < 1000)
        {
            return;
        }

        _lastHealthPoll = timestamp;

        var output = ReadString(mpv, "ao");
        if (!string.Equals(output, _lastAudioOutput, StringComparison.Ordinal))
        {
            _logger.LogInformation("音频输出：{From} -> {To}", _lastAudioOutput ?? "(未记录)", output ?? "(读不到)");
            _lastAudioOutput = output;
        }

        var track = ReadString(mpv, "aid");
        if (!string.Equals(track, _lastAudioTrack, StringComparison.Ordinal))
        {
            _logger.LogInformation("音轨选择：{From} -> {To}", _lastAudioTrack ?? "(未记录)", track ?? "(读不到)");
            _lastAudioTrack = track;
        }

        var pausedForCache = TryReadFlag(mpv, "paused-for-cache");
        if (pausedForCache is { } paused && paused != _lastCachePaused)
        {
            _lastCachePaused = paused;
            var buffered = ReadDouble(mpv, "cache-buffering-state") ?? -1;

            if (paused)
            {
                Interlocked.Exchange(ref _cachePausedAt, timestamp);
                _logger.LogWarning("缓冲欠载，播放暂停等待：缓冲 {Buffered:0}%", buffered);
            }
            else
            {
                Interlocked.Exchange(ref _cachePausedAt, 0);
                _logger.LogInformation("缓冲恢复：缓冲 {Buffered:0}%", buffered);
                ClearStalled();
            }
        }

        // 欠载持续一段时间才算真的拉不动；秒级抖动不打扰用户。
        var pausedSince = Interlocked.Read(ref _cachePausedAt);
        if (pausedSince != 0 && Stopwatch.GetElapsedTime(pausedSince, timestamp) >= BufferingNoticeDelay)
        {
            MarkStalled();
        }
    }

    /// <summary>发出「拉流停住」事件；已经发过就什么都不做。</summary>
    private void MarkStalled()
    {
        if (Interlocked.Exchange(ref _stalled, 1) == 1)
        {
            return;
        }

        _logger.LogDebug("拉流停住，等待网络恢复");
        Raise(() => Stalled?.Invoke(this, new PlaybackStalledEventArgs(true)));
    }

    /// <summary>发出「拉流恢复」事件；本来就没停住就什么都不做。</summary>
    private void ClearStalled()
    {
        if (Interlocked.Exchange(ref _stalled, 0) == 0)
        {
            return;
        }

        _logger.LogDebug("拉流恢复");
        Raise(() => Stalled?.Invoke(this, new PlaybackStalledEventArgs(false)));
    }

    // ── 初始化 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 懒初始化。<b>构造函数刻意不碰原生库</b>：这样缺 <c>libmpv-2.dll</c> 的机器上应用照样能启动
    /// 并落到登录页，而不是启动即崩。
    /// </summary>
    private bool EnsureInitialized()
    {
        if (_mpv is not null)
        {
            return true;
        }

        if (_unavailableReason is not null)
        {
            return false;
        }

        try
        {
            // 这一步一定是在 UI 线程（从 LoadAsync 进来），此时取 dispatcher 才可靠。
            _dispatcher ??= DispatcherQueue.GetForCurrentThread();

            // 原生库从 exe 同目录加载。默认值就是 BaseDirectory，显式钉一次更保险 ——
            // 从开始菜单启动时 CurrentDirectory 不等于 exe 目录。
            // 这个静态属性只在这里赋值一次。
            MpvApi.RootPath = AppContext.BaseDirectory;

            AudioCacheCleaner.ClearStale(logger: _logger);

            var mpv = new MpvContext();

            // 纯音频 headless。构造时已经 mpv_initialize 过，所以走 property 而不是 option。
            mpv.SetPropertyString("vo", "null");
            mpv.SetPropertyString("vid", "no");
            mpv.SetPropertyString("audio-display", "no");
            mpv.SetPropertyString("keep-open", "no");
            mpv.SetPropertyString("idle", "yes");
            // 音频精简构建禁用了 Lua，也就没有 ytdl 选项；完整构建仍关闭该脚本。
            if (TryReadFlag(mpv, "options/ytdl") is not null)
            {
                mpv.SetPropertyString("ytdl", "no");
            }
            mpv.SetPropertyString("gapless-audio", "yes");

            // ★ 打开 lavf 的 fastseek：FLAC 的解复用器只有在「有 SEEKTABLE + fastseek」时
            //   才会按索引一步定位，否则每次 seek 都走二分字节搜索 —— 实测一次拖动会发 10 个
            //   Range 请求（每个都是新连接），要 1 秒以上；命中索引后只要 1 个。
            //   酷我的无损 FLAC 带完整 SEEKTABLE（实测 27 个定位点覆盖全曲），没有索引的
            //   文件会自动退回原来的搜索方式，不会更差。精确位置仍由 mpv 自己的 hr-seek 解码补齐。
            mpv.SetPropertyString("demuxer-lavf-o", "fflags=+fastseek");

            // ★ 整首预读，但数据放磁盘：播放期间不再依赖 CDN 长连接。
            //   16 MiB 的缓存填满后 mpv 会主动停止读取，连接闲置两分钟被 CDN 回收；
            //   播放推进到缓存右边界时踩在死连接上，就是 §17 的断流。
            //   cache-on-disk 下 demuxer-max-bytes 只统计元数据，整首预读不会带来
            //   几十 MB 常驻内存；cache-secs 保持默认（1000 小时），不再人为限流。
            //   见 docs/play-queue.md §18。
            mpv.SetPropertyString("cache", "yes");
            mpv.SetPropertyString("cache-on-disk", "yes");
            mpv.SetPropertyString("demuxer-cache-dir", AppPaths.AudioCacheDirectory);
            // 默认 immediate 会在创建后立刻 unlink；Windows 上 mpv 用 FILE_SHARE_DELETE
            // 打开文件，unlink 会成功，缓存文件在资源管理器里永远看不见。
            // whendone 让它在播放期间可见、媒体关闭时再删。
            mpv.SetPropertyString("demuxer-cache-unlink-files", "whendone");
            mpv.SetPropertyString("demuxer-max-bytes", "64MiB");
            mpv.SetPropertyString("demuxer-max-back-bytes", "16MiB");

            // 缓冲欠载后恢复播放所需的缓存时长，默认 1 秒。seek 会清空缓存，
            // 在慢速 CDN 上这 1 秒的要等数据明显拉长停顿；0.2 秒足够吸收抖动。
            mpv.SetPropertyString("cache-pause-wait", "0.2");

            // ★ 拉流连接没有超时/重连，是「播到缓冲边界后永久卡死」的温床：
            //   CDN 会关掉长时间不读的连接，mpv 再次读取时踩在死连接上，默认既不超时也不重连。
            //   network-timeout 让读操作 15 秒内失败；stream-lavf-o 让 lavf 自动重连。
            //   两者都不改变正常播放行为，只在连接已经坏掉时起作用。
            mpv.SetPropertyString("network-timeout", "15");
            mpv.SetPropertyString("stream-lavf-o", "reconnect=1,reconnect_streamed=1,reconnect_delay_max=3");

            mpv.FileLoaded += (_, _) => OnFileLoaded();
            mpv.EndFile += (_, e) => OnEndFile(e);
            mpv.Tick += (_, _) => OnTick();

            // mpv 自身的日志（warn 以上）转到应用日志。默认收 "warn" 且写 Debug，
            // 而 Serilog 最低 Information —— 等于不收，避免 info 级把应用日志淹掉。
            // 排查声音问题时设 BOWEN_MPV_LOG=v 再启动，mpv 的日志才会以 Information 落进文件。
            var verbose = !string.IsNullOrWhiteSpace(MpvLogLevel);
            mpv.LogMessage += (_, e) =>
            {
                if (verbose) { _logger.LogInformation("[mpv] {Text}", e.Text); }
                else { _logger.LogDebug("[mpv] {Text}", e.Text); }
            };
            mpv.RequestLogMessages(verbose ? MpvLogLevel! : "warn");

            _mpv = mpv;

            mpv.SetPropertyDouble("volume", _volume);

            _logger.LogInformation("libmpv 已初始化，client API 版本 0x{Version:X}", mpv.ClientApiVersion());

            return true;
        }
        catch (Exception ex)
        {
            _unavailableReason = $"音频引擎不可用：{ex.Message}";
            _logger.LogError(ex, "初始化 libmpv 失败");

            return false;
        }
    }

    // ── 工具 ────────────────────────────────────────────────────────────────

    private void SetPause(bool paused)
    {
        if (_mpv is not { } mpv)
        {
            _logger.LogWarning("要求{Action}，但引擎尚未初始化", paused ? "暂停" : "播放");
            return;
        }

        try
        {
            mpv.SetPropertyFlag("pause", paused);

            // ★ 乐观更新：不等下一次 tick 回读。
            //   回读要等 mpv 发 tick，而界面必须立刻响应点击；
            //   何况回读本身可能失败（见 ReadFlag）。下一次 tick 会再校正一次。
            SetState(paused ? PlaybackState.Paused : PlaybackState.Playing);

            _logger.LogDebug(
                "已请求 pause={Requested}，回读 pause={Actual}",
                paused,
                TryReadFlag(mpv, "pause"));
        }
        catch (Exception ex)
        {
            RaiseFailed($"{(paused ? "暂停" : "播放")}失败：{ex.Message}");
        }
    }

    private void SetState(PlaybackState state)
    {
        if (_state == state)
        {
            return;
        }

        _logger.LogDebug("引擎状态：{From} -> {To}", _state, state);

        _state = state;
        Raise(() => StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(state)));
    }

    /// <summary>
    /// 事件回到 UI 线程的唯一出口。
    /// </summary>
    /// <remarks>
    /// 没有 dispatcher（headless 或测试）时直接同步触发 —— 此时调用方本来就不在 UI 线程模型里。
    /// </remarks>
    private void Raise(Action raise)
    {
        if (_dispatcher is null || _dispatcher.HasThreadAccess)
        {
            raise();
            return;
        }

        if (!_dispatcher.TryEnqueue(() => raise()))
        {
            // 投递不进去就什么都不会更新（进度条不动、按钮状态不变），必须留痕。
            _logger.LogWarning("事件无法投递回 UI 线程，已丢弃");
        }
    }

    private void RaiseFailed(string message)
    {
        _logger.LogWarning("播放引擎：{Message}", message);
        SetState(PlaybackState.Idle);
        Raise(() => Failed?.Invoke(this, new PlaybackFailedEventArgs(message)));
    }

    private static string Seconds(TimeSpan value) =>
        value.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);

    private static string? ReadString(MpvContext mpv, string name)
    {
        try
        {
            return mpv.GetProperty<string>(name);
        }
        catch (Exception)
        {
            // 属性在当前状态下不存在，不是错误。
            return null;
        }
    }

    private static double? ReadDouble(MpvContext mpv, string name)
    {
        try
        {
            var value = mpv.GetProperty<double>(name);
            return double.IsNaN(value) ? null : value;
        }
        catch (Exception)
        {
            // 属性在当前状态下不存在（例如 idle 时的 time-pos），不是错误。
            return null;
        }
    }

    /// <summary>
    /// 读一个 flag 属性。<b>两级回退，缺一不可。</b>
    /// </summary>
    /// <remarks>
    /// libmpv 的 flag 属性底层是 int，托管层对 <c>bool</c> 的接受程度不一定可靠。
    /// 只写 <c>bool</c> 那一级的话，一旦它抛异常这里就恒返回 <c>false</c>，
    /// 表现为「暂停后界面状态不更新、且再也恢复不了播放」——因为它一直以为自己还在播。
    /// </remarks>
    private static bool? TryReadFlag(MpvContext mpv, string name)
    {
        try
        {
            return mpv.GetProperty<bool>(name);
        }
        catch (Exception)
        {
            // 换一种读法。
        }

        try
        {
            return mpv.GetProperty<int>(name) != 0;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
