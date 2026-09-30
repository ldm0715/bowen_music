using System.Globalization;
using HanumanInstitute.LibMpv;
using HanumanInstitute.LibMpv.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;

namespace Bodian.WinUI.Playback;

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

    /// <summary>
    /// UI 线程的 dispatcher。<b>首次播放时才取</b>，不在构造函数里取 ——
    /// 这个服务是 DI 单例，构造发生在容器第一次解析它的那一刻，那时取到的队列未必可靠。
    /// 取错的表现很隐蔽：事件会在 mpv 线程上直接操作 UI，抛跨线程异常把事件循环整个干掉，
    /// 之后进度、状态一律不再更新，且没有任何崩溃迹象。
    /// </summary>
    private DispatcherQueue? _dispatcher;

    private MpvContext? _mpv;
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
    private bool _disposed;

    public LibMpvPlaybackService(ILogger<LibMpvPlaybackService>? logger = null)
    {
        _logger = logger ?? NullLogger<LibMpvPlaybackService>.Instance;
    }

    public bool IsAvailable => _mpv is not null;

    public string? UnavailableReason => _unavailableReason;

    public PlaybackState State => _state;

    public TimeSpan Position => _position;

    public TimeSpan Duration => _duration;

    public double Volume => _volume;

    public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;

    public event EventHandler<PlaybackPositionChangedEventArgs>? PositionChanged;

    public event EventHandler? Ended;

    public event EventHandler<PlaybackFailedEventArgs>? Failed;

    public Task LoadAsync(PlaybackSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!EnsureInitialized() || _mpv is not { } mpv)
        {
            RaiseFailed(_unavailableReason ?? "音频引擎不可用");
            return Task.CompletedTask;
        }

        // ★ 不能用 mpv.LoadFile(path, extraArgs:)：那个重载把 extra 参数从 index 2 开始写，
        //   覆盖了 flags 位（应为 3），mpv 会报 "Invalid flag for option loadfile" 并静默放弃加载
        //   （症状是 idle-active 恒为 1、读不到 time-pos）。自己发命令，用 mpv 原生的
        //   loadfile <url> <flags> <index> <options> 形式。
        var args = new List<object?> { "loadfile", source.StreamUrl.ToString(), "replace" };

        var options = new List<string>();

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
        _position = TimeSpan.Zero;
        _duration = TimeSpan.Zero;
        SetState(PlaybackState.Loading);

        try
        {
            mpv.RunCommand(null, args.ToArray());
        }
        catch (Exception ex)
        {
            RaiseFailed($"播放请求失败：{ex.Message}");
        }

        return Task.CompletedTask;
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

        try
        {
            mpv.SetPropertyDouble("time-pos", seconds);
            _lastReported = null;
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

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;

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
        SetState(PlaybackState.Playing);
    }

    private void OnEndFile(MpvEndFileEventArgs e)
    {
        _logger.LogDebug("mpv 结束文件：reason={Reason} error={Error}", e.Reason, e.Error);

        _position = TimeSpan.Zero;

        // 用 Error 码判失败，而不是比 MpvEndFileReason 的名字。
        if (e.Error != 0)
        {
            RaiseFailed($"播放中断（mpv 错误码 {e.Error}）");
            return;
        }

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

        // idle 状态下 time-pos 不存在，读不到是正常的。
        if (ReadDouble(mpv, "time-pos") is not { } seconds)
        {
            return;
        }

        var position = TimeSpan.FromSeconds(seconds);

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

        Raise(() => PositionChanged?.Invoke(this, new PlaybackPositionChangedEventArgs(position, duration)));
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

            var mpv = new MpvContext();

            // 纯音频 headless。构造时已经 mpv_initialize 过，所以走 property 而不是 option。
            mpv.SetPropertyString("vo", "null");
            mpv.SetPropertyString("vid", "no");
            mpv.SetPropertyString("audio-display", "no");
            mpv.SetPropertyString("keep-open", "no");
            mpv.SetPropertyString("idle", "yes");
            mpv.SetPropertyString("ytdl", "no");
            mpv.SetPropertyString("gapless-audio", "yes");

            mpv.FileLoaded += (_, _) => OnFileLoaded();
            mpv.EndFile += (_, e) => OnEndFile(e);
            mpv.Tick += (_, _) => OnTick();

            // mpv 自身的日志（warn 以上）转到应用日志。传 "warn" 而不是 "info"：
            // info 级每次解码、每次缓冲都会刷一行，把应用日志淹掉。
            // 排查加载失败时把它临时改成 "v" 即可。
            mpv.LogMessage += (_, e) => _logger.LogDebug("[mpv] {Text}", e.Text);
            mpv.RequestLogMessages("warn");

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
