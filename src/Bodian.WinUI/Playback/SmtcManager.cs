using Bodian.Core.Media;
using Bodian.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Windows.Media;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace Bodian.WinUI.Playback;

/// <summary>
/// 把播放状态暴露给系统媒体控件（SMTC）—— 项目的原始动机就是这个。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么是「借壳」</b>：<c>SystemMediaTransportControls.GetForCurrentView()</c> 在桌面应用必抛
/// <c>Invalid window handle</c>，微软在 WindowsAppSDK#127 以 <c>not_planned</c> 关闭了这个问题，
/// 而官方替代接口 <c>ISystemMediaTransportControlsInterop</c> 在 C# 投影里是 protected。
/// 纯 C# 唯一可行的入口是<b>创建一个不参与播放的 <see cref="MediaPlayer"/>，再取它的
/// <see cref="MediaPlayer.SystemMediaTransportControls"/></b>。这条路已在 Win10 19045 上实测通过。
/// </para>
/// <para>
/// <b>这个类是全项目唯一碰 <see cref="MediaPlayer"/> / <see cref="SystemMediaTransportControls"/> 的地方</b>，
/// 与 <see cref="LibMpvPlaybackService"/> 是唯一碰 <c>MpvContext</c> 的地方同理。
/// </para>
/// <para>
/// <b>绝不让 SMTC 拖垮播放</b>：初始化失败一次就不再重试，每处调用各自 try/catch，
/// 任何异常都不许冒泡回播放链路。SMTC 是锦上添花，播放是命。
/// </para>
/// </remarks>
public sealed class SmtcManager : IDisposable
{
    /// <summary>时间轴刷新间隔。</summary>
    /// <remarks>
    /// 1 秒，不是文档建议的 5 秒：Lyricify 这类消费方用「<c>Position</c> + 距上次更新的时长」插值，
    /// 1 秒能把插值误差压到看不出来，而单次更新只是一次廉价的 COM 调用。
    /// </remarks>
    private static readonly TimeSpan TimelineInterval = TimeSpan.FromSeconds(1);

    /// <summary>时长还未知时的兜底上界。只为让时间轴有个合法值，不代表真实长度。</summary>
    private static readonly TimeSpan FallbackDuration = TimeSpan.FromSeconds(180);

    private readonly PlaybackCoordinator _coordinator;
    private readonly IPlaybackService _engine;
    private readonly TimeProvider _time;
    private readonly ILogger<SmtcManager> _logger;

    private DispatcherQueue? _dispatcher;
    private MediaPlayer? _host;
    private SystemMediaTransportControls? _smtc;

    private bool _initFailed;
    private bool _disposed;

    private TimeSpan _startTime = TimeSpan.Zero;
    private TimeSpan? _auditionEnd;
    private TimeSpan _trackDuration;
    private DateTimeOffset? _lastTimelineUpdate;
    private int _queueCommandBusy;

    public SmtcManager(
        PlaybackCoordinator coordinator,
        IPlaybackService engine,
        TimeProvider? time = null,
        ILogger<SmtcManager>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(engine);

        _coordinator = coordinator;
        _engine = engine;
        _time = time ?? TimeProvider.System;
        _logger = logger ?? NullLogger<SmtcManager>.Instance;

        // 构造里只订阅：此时既没有 DispatcherQueue，也没有窗口。
        _coordinator.Started += OnStarted;
        _coordinator.Blocked += OnBlocked;
        _coordinator.Queue.Changed += OnQueueChanged;
        _engine.StateChanged += OnEngineStateChanged;
        _engine.PositionChanged += OnPositionChanged;
        _engine.Failed += OnEngineFailed;
    }

    /// <summary>
    /// 首次需要时建会话。**不在构造函数里建** —— 那时拿到的 <see cref="DispatcherQueue"/> 不可靠，
    /// 这是 <see cref="LibMpvPlaybackService"/> 踩过的同一个坑。
    /// </summary>
    private bool EnsureInitialized()
    {
        if (_smtc is not null)
        {
            return true;
        }

        if (_initFailed || _disposed)
        {
            return false;
        }

        try
        {
            // 走到这里一定在 UI 线程（事件都由引擎/协调器 marshal 过来了）。
            _dispatcher ??= DispatcherQueue.GetForCurrentThread();

            var host = new MediaPlayer();

            // ★ 先断开自动集成，否则这个壳会把自己当播放器接管按钮语义。
            host.CommandManager.IsEnabled = false;

            var smtc = host.SystemMediaTransportControls;
            smtc.IsEnabled = true;
            smtc.IsPlayEnabled = true;
            smtc.IsPauseEnabled = true;
            smtc.IsStopEnabled = true;

            // ★ Type 必须在碰 MusicProperties 之前设好，否则抛异常。
            smtc.DisplayUpdater.Type = MediaPlaybackType.Music;

            smtc.ButtonPressed += OnButtonPressed;
            smtc.PlaybackPositionChangeRequested += OnPositionChangeRequested;

            _host = host;
            _smtc = smtc;

            _logger.LogInformation("SMTC 会话已建立");
            return true;
        }
        catch (Exception ex)
        {
            // 一次失败就不再重试：SMTC 不该有机会反复拖累播放。
            _initFailed = true;
            _logger.LogWarning(ex, "初始化 SMTC 失败，本次运行不再提供系统媒体控制");
            return false;
        }
    }

    // ── 引擎与协调器事件 ────────────────────────────────────────────────────

    private void OnStarted(object? sender, PlaybackStartedEventArgs e)
    {
        if (!EnsureInitialized() || _smtc is null)
        {
            return;
        }

        _trackDuration = e.Track.Duration;
        _auditionEnd = e.Policy.StopAt;
        _startTime = e.Policy.Resolution is PlaybackResolution.AuditionOnly audition
            ? audition.Start
            : TimeSpan.Zero;

        try
        {
            var updater = _smtc.DisplayUpdater;
            updater.Type = MediaPlaybackType.Music;
            updater.MusicProperties.Title = e.Track.Title;
            updater.MusicProperties.Artist = e.Track.ArtistText;
            updater.MusicProperties.AlbumTitle = e.Track.AlbumName ?? "";

            // 没有封面时必须显式写 null，否则上一首的缩略图会黏在上面。
            updater.Thumbnail = CreateThumbnail(e.Track.CoverImage);

            updater.Update();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "更新 SMTC 元数据失败");
        }

        UpdateButtons();
        UpdateTimeline(force: true);
    }

    private void OnEngineStateChanged(object? sender, PlaybackStateChangedEventArgs e)
    {
        if (!EnsureInitialized() || _smtc is null)
        {
            return;
        }

        try
        {
            _smtc.PlaybackStatus = Map(e.State);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "更新 SMTC 播放状态失败");
        }

        // 暂停时把时间轴钉在当前位置，别让面板停在上一秒。
        if (e.State == PlaybackState.Paused)
        {
            UpdateTimeline(force: true);
        }
    }

    private void OnPositionChanged(object? sender, PlaybackPositionChangedEventArgs e) => UpdateTimeline(force: false);

    private void OnEngineFailed(object? sender, PlaybackFailedEventArgs e) => SetStopped("播放引擎出错");

    /// <summary>
    /// 不能播时只改状态，**不动元数据**。
    /// </summary>
    /// <remarks>
    /// SMTC 没有「出错」这个语义。把被拒绝的曲目写进元数据，消费方（Lyricify 等）会去搜一首
    /// 根本没播的歌的歌词，面板也会显示一个假会话。保持上一条 + Stopped 至少是真话 ——
    /// 「为什么播不了」由应用界面里的提示负责讲清楚。
    /// </remarks>
    private void OnBlocked(object? sender, PlaybackBlockedEventArgs e) => SetStopped("曲目不可播");

    private void OnQueueChanged(object? sender, EventArgs e) => UpdateButtons();

    private void SetStopped(string reason)
    {
        if (_smtc is null)
        {
            return;
        }

        _logger.LogDebug("SMTC 状态置为停止：{Reason}", reason);

        try
        {
            _smtc.PlaybackStatus = MediaPlaybackStatus.Stopped;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "更新 SMTC 播放状态失败");
        }
    }

    // ── SMTC 侧 ─────────────────────────────────────────────────────────────

    private void UpdateButtons()
    {
        if (_smtc is null)
        {
            return;
        }

        try
        {
            _smtc.IsNextEnabled = _coordinator.Queue.HasNext;
            _smtc.IsPreviousEnabled = _coordinator.Queue.HasPrevious;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "更新 SMTC 按钮状态失败");
        }
    }

    private void UpdateTimeline(bool force)
    {
        if (_smtc is null)
        {
            return;
        }

        var now = _time.GetUtcNow();

        // 哨兵用可空的 DateTimeOffset，**不要**用 TimeSpan.MinValue 参与算术：
        // P2 那个溢出把 mpv 事件循环整个打死的教训就在这里。
        if (!force && _lastTimelineUpdate is { } last && now - last < TimelineInterval)
        {
            return;
        }

        var end = ResolveEndTime();

        try
        {
            _smtc.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties
            {
                StartTime = _startTime,

                // ★ 不设 MinSeekTime / MaxSeekTime，SMTC 就不会发 PlaybackPositionChangeRequested，
                //   也就是系统面板上的进度条根本拖不动。
                MinSeekTime = _startTime,
                Position = Clamp(_engine.Position, _startTime, end),
                MaxSeekTime = end,
                EndTime = end,
            });

            _lastTimelineUpdate = now;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "更新 SMTC 时间轴失败");
        }
    }

    /// <summary>
    /// 时间轴的上界。
    /// </summary>
    /// <remarks>
    /// 试听片段的上界是<b>片段终点</b>，与播放条把进度条上限设成 <c>StopAt</c> 的做法同构：
    /// 面板上的进度条要诚实，拖动也要被夹在可播区间内。
    /// 消费方读的是绝对 <c>Position</c>，所以歌词仍然对得上。
    /// </remarks>
    private TimeSpan ResolveEndTime()
    {
        if (_auditionEnd is { } auditionEnd)
        {
            return auditionEnd > _startTime ? auditionEnd : _startTime + FallbackDuration;
        }

        // 引擎实测优先（最准），曲目元数据兜底。
        var duration = _engine.Duration > TimeSpan.Zero ? _engine.Duration : _trackDuration;

        return duration > _startTime ? duration : _startTime + FallbackDuration;
    }

    private void OnButtonPressed(
        SystemMediaTransportControls sender,
        SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        var button = args.Button;
        var dispatcher = _dispatcher;

        // 这个事件不在 UI 线程。下游（协调器、引擎、ViewModel）都假设自己在 UI 线程：
        // 协调器靠 ConfigureAwait(true) 回到 UI 上下文，ViewModel 的 ObservableProperty 也靠它。
        if (dispatcher is null || !dispatcher.TryEnqueue(() => _ = HandleButtonAsync(button)))
        {
            _logger.LogWarning("SMTC 按钮 {Button} 无法投递回 UI 线程，已丢弃", button);
        }
    }

    private void OnPositionChangeRequested(
        SystemMediaTransportControls sender,
        PlaybackPositionChangeRequestedEventArgs args)
    {
        var target = args.RequestedPlaybackPosition;
        var dispatcher = _dispatcher;

        if (dispatcher is null || !dispatcher.TryEnqueue(() => _ = SeekAsync(target)))
        {
            _logger.LogWarning("SMTC 拖动请求无法投递回 UI 线程，已丢弃");
        }
    }

    /// <summary>
    /// 处理按钮。**必须自带 try/catch** —— 这里是用 <c>_ =</c> 点火的未观察任务，
    /// 异常逃出去就是静默丢失。
    /// </summary>
    private async Task HandleButtonAsync(SystemMediaTransportControlsButton button)
    {
        if (_disposed)
        {
            return;
        }

        _logger.LogDebug("收到 SMTC 按钮 {Button}", button);

        try
        {
            switch (button)
            {
                case SystemMediaTransportControlsButton.Play:
                    await PlayAsync().ConfigureAwait(true);
                    break;

                case SystemMediaTransportControlsButton.Pause:
                    await _engine.PauseAsync().ConfigureAwait(true);
                    break;

                case SystemMediaTransportControlsButton.Stop:
                    await _engine.StopAsync().ConfigureAwait(true);
                    break;

                case SystemMediaTransportControlsButton.Next:
                    await RunQueueCommandAsync(_coordinator.NextAsync).ConfigureAwait(true);
                    break;

                case SystemMediaTransportControlsButton.Previous:
                    await RunQueueCommandAsync(_coordinator.PreviousAsync).ConfigureAwait(true);
                    break;

                default:
                    _logger.LogDebug("忽略 SMTC 按钮 {Button}", button);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // 关窗时的正常取消。
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "处理 SMTC 按钮 {Button} 失败", button);
        }
    }

    private Task PlayAsync() => _engine.State switch
    {
        PlaybackState.Paused => _engine.PlayAsync(),

        // Idle/Stopped 时 **不能**调 PlayAsync()：mpv 已经 idle，设 pause=false 什么都不会发生。
        // 必须重新解析一次音源（CDN 直链带签名且有时效，旧的很可能已经过期）。
        PlaybackState.Idle or PlaybackState.Stopped => _coordinator.ReplayCurrentAsync(),

        _ => Task.CompletedTask,
    };

    /// <summary>串行化队列命令，防手快连点。**不覆盖 Play/Pause** —— 那会把「暂停→播放」丢掉。</summary>
    private async Task RunQueueCommandAsync(Func<CancellationToken, Task> command)
    {
        if (Interlocked.Exchange(ref _queueCommandBusy, 1) == 1)
        {
            _logger.LogDebug("上一条队列命令尚未结束，忽略本次");
            return;
        }

        try
        {
            await command(CancellationToken.None).ConfigureAwait(true);
        }
        finally
        {
            Volatile.Write(ref _queueCommandBusy, 0);
        }
    }

    private async Task SeekAsync(TimeSpan target)
    {
        try
        {
            await _engine.SeekAsync(Clamp(target, _startTime, ResolveEndTime())).ConfigureAwait(true);

            // 不等下一次 tick：引擎最早 200ms 后才回报新位置，而节流窗口是 1 秒，
            // 不强制刷新的话面板会先在旧位置停一秒，看起来像「拖了没反应然后跳一下」。
            UpdateTimeline(force: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SMTC 拖动失败");
        }
    }

    private RandomAccessStreamReference? CreateThumbnail(Uri? cover)
    {
        if (CoverArtUrl.Jpeg(cover) is not { } jpeg || jpeg.Scheme is not ("http" or "https"))
        {
            return null;
        }

        try
        {
            // 交给系统去取。取失败只会「没有缩略图」，而 SMTC 本来就允许没有缩略图。
            return RandomAccessStreamReference.CreateFromUri(jpeg);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "缩略图地址不可用：{Cover}", jpeg.GetLeftPart(UriPartial.Path));
            return null;
        }
    }

    private static MediaPlaybackStatus Map(PlaybackState state) => state switch
    {
        PlaybackState.Loading => MediaPlaybackStatus.Changing,
        PlaybackState.Playing => MediaPlaybackStatus.Playing,
        PlaybackState.Paused => MediaPlaybackStatus.Paused,

        // Idle / Stopped。**不用 Closed** —— 那会让会话从系统面板上消失，而重新启用不一定能重新注册；
        // Closed 只留给 Dispose。
        _ => MediaPlaybackStatus.Stopped,
    };

    private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max)
        => value < min ? min : value > max ? max : value;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _coordinator.Started -= OnStarted;
        _coordinator.Blocked -= OnBlocked;
        _coordinator.Queue.Changed -= OnQueueChanged;
        _engine.StateChanged -= OnEngineStateChanged;
        _engine.PositionChanged -= OnPositionChanged;
        _engine.Failed -= OnEngineFailed;

        if (_smtc is null)
        {
            return;
        }

        try
        {
            _smtc.ButtonPressed -= OnButtonPressed;
            _smtc.PlaybackPositionChangeRequested -= OnPositionChangeRequested;
            _smtc.PlaybackStatus = MediaPlaybackStatus.Closed;
            _smtc.DisplayUpdater.ClearAll();
            _smtc.IsEnabled = false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "关闭 SMTC 会话失败");
        }

        _host?.Dispose();
        _smtc = null;
        _host = null;
    }
}
