using Bodian.Core.Api;
using Bodian.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// MV 画面的缩放方式。
/// </summary>
/// <remarks>
/// 四个模式里「适应宽度」「适应高度」不是 XAML <c>Stretch</c> 的自带语义：
/// <c>Uniform</c> / <c>UniformToFill</c> 的裁切方向取决于画面比例与容器比例谁大，
/// 而这两个模式是**钉死在某一根轴**上的。所以它们由 <c>MvPage</c> 自己算矩形实现。
/// </remarks>
public enum MvFitMode
{
    /// <summary>适应屏幕：整幅画面都在，宽或高不够的那一边留黑边。</summary>
    FitScreen,

    /// <summary>适应宽度：宽度铺满，高出来的部分裁掉。</summary>
    FitWidth,

    /// <summary>适应高度：高度铺满，宽出来的部分裁掉。</summary>
    FitHeight,

    /// <summary>拉伸填满，不保持比例。</summary>
    Stretch,
}

/// <summary>画面比例菜单里的一行。</summary>
public sealed record MvFitOption(MvFitMode Mode, string Name);

/// <summary>
/// MV 页的状态与播放。
/// </summary>
/// <remarks>
/// <para>
/// <b>MV 走 Windows 的 <see cref="MediaPlayer"/>，不是 mpv。</b> 音频那条链路
/// （<c>LibMpvPlaybackService</c>）是纯音频 headless 且被 <c>PlaybackCoordinator</c> 独占，
/// 借它出画面要拆掉「哑引擎」的边界。两条链路各管各的，靠 <see cref="SwitchToAudio"/> /
/// <see cref="SwitchToVideo"/> 互斥，谁在响是明确的。
/// </para>
/// <para>
/// <b>必须关掉 MediaPlayer 自己的 SMTC 集成</b>（<c>CommandManager.IsEnabled = false</c>）：
/// 否则它会和 <c>SmtcManager</c> 抢系统媒体卡片，看 MV 时系统那边的曲目会来回跳。
/// </para>
/// <para>
/// 位置靠定时器轮询而不是 <c>PlaybackSession.PositionChanged</c>：那个事件在后台线程触发，
/// 每次都要 marshal 回 UI 线程，而 200ms 的轮询本来就和音频那边的上报节流同频。
/// </para>
/// </remarks>
public sealed partial class MvViewModel : ObservableObject, IDisposable
{
    /// <summary>与 <c>LibMpvPlaybackService.PositionReportInterval</c> 同频。</summary>
    private const int PositionPollMilliseconds = 200;

    private readonly IBodianApi _api;
    private readonly PlayerViewModel _audio;
    private readonly ILogger<MvViewModel> _logger;
    private readonly DispatcherQueueTimer _positionTimer;

    private MvInfo? _mv;

    /// <summary>进 MV 页时把音频暂停了没有。退出时据此恢复，别去动用户自己按的暂停。</summary>
    private bool _pausedAudio;

    /// <summary>画面尺寸只上报一次，避免每 200ms 刷一条日志。</summary>
    private bool _reportedNaturalSize;

    private bool _disposed;

    public MvViewModel(IBodianApi api, PlayerViewModel audio, ILogger<MvViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(audio);

        _api = api;
        _audio = audio;
        _logger = logger ?? NullLogger<MvViewModel>.Instance;

        Player = new MediaPlayer { CommandManager = { IsEnabled = false } };

        _positionTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _positionTimer.Interval = TimeSpan.FromMilliseconds(PositionPollMilliseconds);
        _positionTimer.IsRepeating = true;
        _positionTimer.Tick += (_, _) => Poll();
    }

    /// <summary>交给 <c>MediaPlayerElement.SetMediaPlayer</c> 的那一个实例。</summary>
    public MediaPlayer Player { get; }

    /// <summary>正在放的 MV。加载成功前为 <c>null</c>。</summary>
    public MvInfo? Source => _mv;

    /// <summary>
    /// MV 是不是已经装好了。进度条与播放键的可用来都由它决定。
    /// </summary>
    /// <remarks>
    /// <b>必须是可通知属性</b>，不能写成 <c>=&gt; _mv is not null</c>：那样绑定只在加载完成前
    /// 求值一次、拿到 <c>false</c> 就再也不更新，控制条会整条禁用 —— 按不了、拖不动。
    /// </remarks>
    [ObservableProperty]
    public partial bool HasSource { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>加载中 / 失败时盖在画面上的一句话。空串表示不显示。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string StatusText { get; set; } = "";

    public bool HasStatus => StatusText.Length > 0;

    /// <summary>状态文案是不是一个错误。用来决定它的颜色与要不要给重试入口。</summary>
    [ObservableProperty]
    public partial bool StatusIsError { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; } = "";

    [ObservableProperty]
    public partial string ArtistText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsPlaying { get; set; }

    [ObservableProperty]
    public partial double PositionSeconds { get; set; }

    [ObservableProperty]
    public partial double DurationSeconds { get; set; }

    /// <summary>0..1，与 <see cref="MediaPlayer.Volume"/> 同标度。</summary>
    [ObservableProperty]
    public partial double Volume { get; set; } = 1;

    /// <summary>试看上限；不限期为空。</summary>
    [ObservableProperty]
    public partial TimeSpan? PreviewLimit { get; set; }

    /// <summary>
    /// 用户正在拖进度条。拖动期间不把播放位置写回界面，否则滑条会被弹回原位。
    /// </summary>
    public bool IsSeeking { get; set; }

    /// <summary>画面缩放方式。默认适应屏幕 —— 先保证整幅画面都在，嫌小可以再切。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FitModeLabel))]
    [NotifyPropertyChangedFor(nameof(SelectedFitOption))]
    public partial MvFitMode FitMode { get; set; } = MvFitMode.FitScreen;

    /// <summary>菜单里的四项，顺序即显示顺序。</summary>
    public IReadOnlyList<MvFitOption> FitOptions { get; } =
    [
        new(MvFitMode.FitScreen, "适应屏幕"),
        new(MvFitMode.FitWidth, "适应宽度"),
        new(MvFitMode.FitHeight, "适应高度"),
        new(MvFitMode.Stretch, "拉伸填满"),
    ];

    /// <summary>当前缩放方式的中文名。按钮上直接显示它，比再画一颗图标清楚。</summary>
    public string FitModeLabel => SelectedFitOption.Name;

    /// <summary>菜单里那一行的选中项。背景高亮由 ListView 自带的选中底给，不要勾选框。</summary>
    public MvFitOption SelectedFitOption => FitOptions.First(option => option.Mode == FitMode);

    /// <summary>
    /// 画面的宽高比（宽 ÷ 高）。解码器还没报出尺寸时为 <c>0</c>。
    /// </summary>
    /// <remarks>
    /// 取的是 <c>PlaybackSession.NaturalVideoWidth/Height</c>。**如果片源是变形（anamorphic）的，
    /// 这里拿到的是存储尺寸、不含像素宽高比** —— 那种片源四个模式都会看着被拉伸。尚未实测。
    /// </remarks>
    [ObservableProperty]
    public partial double NaturalAspect { get; set; }

    partial void OnVolumeChanged(double value)
    {
        Player.Volume = Math.Clamp(value, 0, 1);
    }

    /// <summary>
    /// 取 MV 地址并装进播放器。
    /// </summary>
    /// <returns>装好了为 <c>true</c>；这首歌没有 MV、或取不到，为 <c>false</c>（文案已写进状态）。</returns>
    public async Task<bool> LoadAsync(Track track, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);

        Title = track.Title;
        ArtistText = track.ArtistText;
        IsBusy = true;
        StatusIsError = false;
        StatusText = "正在加载 MV…";

        try
        {
            var mv = await _api.GetMvInfoAsync(track.Id, cancellationToken).ConfigureAwait(true);

            if (mv is null)
            {
                // 「这首歌没有 MV」与「请求出错」是两回事：前者不该给重试入口。
                StatusIsError = false;
                StatusText = "这首歌没有 MV。";
                return false;
            }

            _mv = mv;
            HasSource = true;
            PreviewLimit = mv.PreviewLimit;
            Player.Source = MediaSource.CreateFromUri(mv.VideoUrl);
            Player.Volume = Volume;
            StatusText = "";
            Player.Play();
            _positionTimer.Start();
            IsPlaying = true;
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "曲目 {MusicId} 的 MV 加载失败", track.Id);
            StatusIsError = true;
            StatusText = "MV 加载失败，请稍后再试。";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void TogglePlayPause()
    {
        if (_mv is null)
        {
            return;
        }

        if (IsPlaying)
        {
            Player.Pause();
        }
        else
        {
            // 试看已经放完的话，重放要从头开始，否则一按就立刻又停。
            if (PreviewLimit is { } limit && PositionSeconds >= limit.TotalSeconds - 0.5)
            {
                Player.PlaybackSession.Position = TimeSpan.Zero;
            }

            Player.Play();
        }
    }

    public void Seek(double seconds)
    {
        if (_mv is null)
        {
            return;
        }

        var target = Math.Clamp(seconds, 0, Math.Max(0, DurationSeconds));
        Player.PlaybackSession.Position = TimeSpan.FromSeconds(target);
        PositionSeconds = target;
    }

    /// <summary>
    /// 进 MV 页时调：把正在放的音频暂停，并记住是自己暂停的。
    /// </summary>
    /// <remarks>
    /// 用户本来就是暂停态时不去动它 —— 退出时也就没有什么可恢复的。
    /// </remarks>
    public void PauseAudioForVideo()
    {
        if (!_audio.IsPlaying)
        {
            return;
        }

        _audio.TogglePlayPauseCommand.Execute(null);
        _pausedAudio = true;
    }

    /// <summary>退出 MV 页时调：把自己暂停掉的音频放回来。</summary>
    public void RestoreAudioAfterVideo()
    {
        if (!_pausedAudio)
        {
            return;
        }

        _pausedAudio = false;

        // 这期间用户可能已经在别处重新播了，别再按一次把它按停。
        if (!_audio.IsPlaying && _audio.HasTrack)
        {
            _audio.TogglePlayPauseCommand.Execute(null);
        }
    }

    /// <summary>音/视频互切：切到「只听」。</summary>
    /// <remarks>
    /// 用户点了这颗按钮就是要听歌，所以**即使进 MV 页之前音频本来是暂停的也放起来**。
    /// 放完把 <see cref="_pausedAudio"/> 清掉：退出页面时 <see cref="RestoreAudioAfterVideo"/>
    /// 不该再「恢复」一次 —— 那时 <c>IsPlaying</c> 还是引擎事件送回来的旧值 <c>false</c>，
    /// 守卫会失效，于是刚放起来的音频立刻又被按停。
    /// </remarks>
    public void SwitchToAudio()
    {
        Player.Pause();
        IsPlaying = false;

        if (!_audio.IsPlaying && _audio.HasTrack)
        {
            _audio.TogglePlayPauseCommand.Execute(null);
        }

        _pausedAudio = false;
    }

    /// <summary>音/视频互切：切到「看 MV」。</summary>
    public void SwitchToVideo()
    {
        if (_audio.IsPlaying)
        {
            _audio.TogglePlayPauseCommand.Execute(null);
        }

        Player.Play();
        IsPlaying = true;
    }

    private void Poll()
    {
        if (_disposed)
        {
            return;
        }

        var session = Player.PlaybackSession;
        DurationSeconds = session.NaturalDuration.TotalSeconds;
        if (!IsSeeking)
        {
            PositionSeconds = session.Position.TotalSeconds;
        }

        // 解码器报出尺寸要等开始解码，所以画面比例是这里刷出来的，而不是加载时。
        var aspect = session.NaturalVideoHeight > 0
            ? session.NaturalVideoWidth / (double)session.NaturalVideoHeight
            : 0;
        if (Math.Abs(aspect - NaturalAspect) > 0.0001)
        {
            NaturalAspect = aspect;

            // 只在比例第一次由「未知」变成有值时记一条：画面模式的四个算法全靠它，
            // 它要是始终是 0，四个按钮就会一动不动。每个 MV 记一次，不刷屏。
            if (!_reportedNaturalSize && aspect > 0)
            {
                _reportedNaturalSize = true;
                _logger.LogInformation(
                    "MV 画面尺寸：{Width}×{Height}，比例 {Aspect:F4}",
                    session.NaturalVideoWidth, session.NaturalVideoHeight, aspect);
            }
        }

        IsPlaying = session.PlaybackState == MediaPlaybackState.Playing;

        // 试看到点就停。服务端用 playLimitTime 下发，实测那个账号一直是 0（不限）。
        if (PreviewLimit is { } limit && PositionSeconds >= limit.TotalSeconds)
        {
            Player.Pause();
            IsPlaying = false;
            _positionTimer.Stop();
            StatusIsError = false;
            StatusText = $"试看 {Formats.Duration(limit)} 已结束。";
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _positionTimer.Stop();
        Player.Source = null;
        Player.Dispose();
    }
}
