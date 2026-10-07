using Bodian.Core.Api;
using Bodian.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

/// <summary>画质菜单里的一行。</summary>
/// <remarks>
/// 做成可绑对象而不是 record：行里要标出「当前档」，而当前档随选择变 ——
/// 与音质菜单那个 <c>AudioQualityOption</c> 同一套写法。
/// </remarks>
public sealed partial class MvQualityOption : ObservableObject
{
    public MvQualityOption(MvQuality quality, string name, string detail)
    {
        Quality = quality;
        Name = name;
        Detail = detail;
    }

    /// <summary>档位。<b>值就是请求里的 <c>wifi</c> 参数</b>，见 <see cref="MvQuality"/>。</summary>
    public MvQuality Quality { get; }

    /// <summary>档位名，显示在彩色胶囊里（与音质那三档同一套配色）。</summary>
    public string Name { get; }

    /// <summary>
    /// 实测码率。位置与音质菜单行尾那个文件大小相同 ——
    /// 让「三档差在哪」看得见，不然三个词并排看不出所以然。
    /// </summary>
    public string Detail { get; }

    /// <summary>是不是正在用的那一档。非当前档的胶囊收暗。</summary>
    [ObservableProperty]
    public partial bool IsCurrent { get; set; }
}

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
public sealed partial class MvViewModel : ObservableObject, IDisposable, IVolumeSource
{
    /// <summary>与 <c>LibMpvPlaybackService.PositionReportInterval</c> 同频。</summary>
    private const int PositionPollMilliseconds = 200;

    private readonly IBodianApi _api;
    private readonly PlayerViewModel _audio;

    /// <summary>静音标记与「改音量即解除静音」那条规则。</summary>
    private readonly Playback.VolumeState _volumeState = new();

    private readonly ILogger<MvViewModel> _logger;
    private readonly DispatcherQueueTimer _positionTimer;

    /// <summary>
    /// 当前这条源已经打开了。<b>由 <c>MediaPlayer</c> 从后台线程置位</b>。
    /// 换档续播要等它，见 <see cref="Poll"/>。
    /// </summary>
    private volatile bool _sourceOpened;

    /// <summary>这一页在放哪首歌。<b>换画质要按它重新取直链</b>；0 表示还没进过。</summary>
    private long _musicId;

    /// <summary>换档重载之后要 seek 回去的位置；没有待办的续播时为 <c>null</c>。</summary>
    private TimeSpan? _resumeAfterOpen;

    /// <summary>正在飞的那次换档。<b>连点时用它把上一次取消掉</b>，只让最后一次落地。</summary>
    private CancellationTokenSource? _switchCts;
    private readonly CancellationTokenSource _lifetime = new();

    /// <summary>装上新源的时刻（<c>Environment.TickCount64</c>）；0 表示没有在等的源。见看门狗。</summary>
    private long _awaitingOpenSince;

    /// <summary>正在准备中的备用播放器。<b>它打开成功之前，现役那一条继续放着。</b></summary>
    private Action? _releaseStaging;

    /// <summary>新源多久没打开就认作失败。MV 直链是普通 MP4，正常几秒内就出声了。</summary>
    private const int SourceOpenTimeoutMs = 10_000;

    private MvInfo? _mv;

    /// <summary>进 MV 页时把音频暂停了没有。退出时据此恢复，别去动用户自己按的暂停。</summary>
    private bool _pausedAudio;

    /// <summary>画面尺寸只上报一次，避免每 200ms 刷一条日志。</summary>
    private bool _reportedNaturalSize;

    private bool _disposed;
    private bool _presentationSuspended;

    public MvViewModel(IBodianApi api, PlayerViewModel audio, ILogger<MvViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(audio);

        _api = api;
        _audio = audio;
        _logger = logger ?? NullLogger<MvViewModel>.Instance;

        Player = NewPlayer();
        Player.MediaOpened += (_, _) => _sourceOpened = true;

        _positionTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _positionTimer.Interval = TimeSpan.FromMilliseconds(PositionPollMilliseconds);
        _positionTimer.IsRepeating = true;
        _positionTimer.Tick += OnPositionTick;

        // 初始的「当前档」标记。之后由 OnQualityChanged 维护。
        foreach (var option in QualityOptions)
        {
            option.IsCurrent = option.Quality == Quality;
        }
    }

    /// <summary>
    /// 现役播放器，交给 <c>MediaPlayerElement.SetMediaPlayer</c>。
    /// </summary>
    /// <remarks>
    /// <b>换画质时会整个换一个</b>，但只在备用播放器把新源**打开成功之后**才换
    /// （见 <see cref="SwitchQualityAsync"/>）。页面订阅了本对象的 <c>PropertyChanged</c>，
    /// 看到它变了就重新 <c>SetMediaPlayer</c>；顺序由 <see cref="Promote"/> 保证：
    /// 新实例先绑上，旧实例才释放。
    /// </remarks>
    [ObservableProperty]
    public partial MediaPlayer Player { get; set; }

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

    /// <summary>
    /// <see cref="IVolumeSource"/> 要的是 0–100（与播放条那套同标度），换算在这一处做。
    /// </summary>
    /// <remarks>
    /// 显式实现：本类的 <see cref="Volume"/> 是 0..1、直接喂给 <c>MediaPlayer.Volume</c>，
    /// 而音量按钮与它的滑条按 0–100 走（滑条 <c>Maximum=100</c>、<c>Formats.Percent</c>）。
    /// 两套标度各自保留，换算只发生在这里。
    /// </remarks>
    double IVolumeSource.Volume
    {
        get => Volume * 100;
        set => Volume = value / 100;
    }

    /// <remarks>
    /// 与 <see cref="PlayerViewModel.IsMuted"/> 同款的两条路：显式静音，或者音量被拉到 0。
    /// </remarks>
    bool IVolumeSource.IsMuted => _volumeState.Muted || Volume <= 0;

    /// <remarks>
    /// 这里送的是 0–1 的 <see cref="Volume"/>（<c>MediaPlayer.Volume</c> 的标度），
    /// 不是 <see cref="IVolumeSource.Volume"/> 那套 0–100 —— 换算只发生在那个显式属性里。
    /// </remarks>
    void IVolumeSource.ToggleMute()
    {
        _volumeState.Toggle();
        Player.Volume = _volumeState.EffectiveVolume(Volume);
        OnPropertyChanged(nameof(IVolumeSource.IsMuted));
    }

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

    /// <summary>
    /// 画质档位。默认高清 —— 与不传 <c>wifi</c> 时服务端给的那一档相同。
    /// </summary>
    /// <remarks>
    /// <b>改这个值会重新请求直链并从当前进度续播</b>，见 <see cref="SwitchQualityAsync"/>。
    /// 三档是三个不同的 mp4，不是同一份流的本地筛选。
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QualityLabel))]
    [NotifyPropertyChangedFor(nameof(SelectedQualityOption))]
    public partial MvQuality Quality { get; set; } = MvQuality.High;

    /// <summary>菜单里的三档，由高到低。码率是实测值，见 <see cref="MvQuality"/>。</summary>
    public IReadOnlyList<MvQualityOption> QualityOptions { get; } =
    [
        new(MvQuality.High, "高清", "2000 kbps"),
        new(MvQuality.Standard, "标清", "1000 kbps"),
        new(MvQuality.Low, "流畅", "512 kbps"),
    ];

    /// <summary>当前档位名，画在入口那颗胶囊上。</summary>
    public string QualityLabel => SelectedQualityOption.Name;

    /// <summary>菜单里那一行的选中项。</summary>
    public MvQualityOption SelectedQualityOption =>
        QualityOptions.First(option => option.Quality == Quality);

    partial void OnQualityChanged(MvQuality value)
    {
        // 菜单里的「当前档」标记跟着翻（非当前档的胶囊收暗）。
        foreach (var option in QualityOptions)
        {
            option.IsCurrent = option.Quality == value;
        }

        _ = SwitchQualityAsync();
    }

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
        // 用户显式改音量就解除静音，理由与 PlayerViewModel.OnVolumeChanged 相同。
        // 必须排在算 EffectiveVolume 之前，否则解除静音的这一次会把音量送成 0。
        _volumeState.OnVolumeSet(value);

        Player.Volume = _volumeState.EffectiveVolume(Math.Clamp(value, 0, 1));

        // 静音图标要跟着翻。PlayerViewModel 那边由 [NotifyPropertyChangedFor] 负责，
        // 这里的 IsMuted 是显式接口实现，得手动报一次。
        OnPropertyChanged(nameof(IVolumeSource.IsMuted));
    }

    /// <summary>
    /// 取 MV 地址并装进播放器。
    /// </summary>
    /// <returns>装好了为 <c>true</c>；这首歌没有 MV、或取不到，为 <c>false</c>（文案已写进状态）。</returns>
    public async Task<bool> LoadAsync(Track track, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (_disposed) return false;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);

        _musicId = track.Id;
        Title = track.Title;
        ArtistText = track.ArtistText;
        IsBusy = true;
        StatusIsError = false;
        StatusText = "正在加载 MV…";

        try
        {
            var mv = await _api.GetMvInfoAsync(track.Id, Quality, request.Token).ConfigureAwait(true);
            if (_disposed || request.IsCancellationRequested) return false;

            if (mv is null)
            {
                // 「这首歌没有 MV」与「请求出错」是两回事：前者不该给重试入口。
                StatusIsError = false;
                StatusText = "这首歌没有 MV。";
                return false;
            }

            Install(mv);
            return true;
        }
        catch (OperationCanceledException) when (_disposed)
        {
            return false;
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

    /// <summary>
    /// 造一个播放器。
    /// </summary>
    /// <remarks>
    /// <c>CommandManager.IsEnabled = false</c> 是必需的：MediaPlayer 自带的 SMTC 集成会与
    /// <c>SmtcManager</c> 抢系统媒体卡片。<c>MediaOpened</c> 由调用方按用途挂 ——
    /// 现役那个置「已打开」标志（供轮询判断能否 seek），备用那个置「准备完成」标志。
    /// </remarks>
    private MediaPlayer NewPlayer()
    {
        var player = new MediaPlayer { CommandManager = { IsEnabled = false } };
        player.MediaFailed += OnMediaFailed;
        player.Volume = Volume;
        return player;
    }

    /// <summary>
    /// 源没打开。<b>这是「换档后黑屏 + 进度 0」最可能的落点，所以必须留痕。</b>
    /// </summary>
    /// <remarks>
    /// <c>MediaFailed</c> 是唯一带得出原因的信号：<c>Error</c> 是枚举，<c>ExtendedErrorCode</c>
    /// 是具体的 HRESULT。该事件在后台线程触发，这里只做日志与置标志（都是线程安全的）。
    /// 备用播放器上的失败要立刻收工，不必让准备过程等满超时。
    /// </remarks>
    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        _logger.LogWarning(
            "MV 源加载失败：{Error}（HRESULT 0x{Code:X8}）{Message}",
            args.Error, args.ExtendedErrorCode.HResult, args.ErrorMessage);

    }

    /// <summary>把 MV 装进现役播放器并放起来。<b>只用于首次加载。</b></summary>
    /// <remarks>
    /// 换画质不走这里 —— 那条路要先在备用播放器上准备，成功了才切，见
    /// <see cref="SwitchQualityAsync"/>。
    /// </remarks>
    private void Install(MvInfo mv)
    {
        // 换源之前先把「已打开」清掉：Poll 靠它判断新源能不能 seek 了。
        _sourceOpened = false;
        _awaitingOpenSince = Environment.TickCount64;

        _mv = mv;
        HasSource = true;
        PreviewLimit = mv.PreviewLimit;
        Player.Source = MediaSource.CreateFromUri(mv.VideoUrl);
        Player.Volume = Volume;
        StatusText = "";
        Player.Play();
        _positionTimer.Start();
        IsPlaying = true;
    }

    /// <summary>
    /// 换画质：重取直链，并<b>从当前进度续播</b>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 三档是三个不同的 mp4（见 <see cref="MvQuality"/>），只能重新请求一次。
    /// 续播在装新源**之前**先把目标位置记下来，由 <see cref="Poll"/> 等到新源可 seek 了再做 ——
    /// 换源之后立刻 seek 会把位置丢掉（画面也出不来），那里的注释写了原因。
    /// </para>
    /// <para>
    /// 失败时只写状态文案，**不动已经在放的画面** —— 手上有画面，一次网络抖动不该把它停掉。
    /// </para>
    /// </remarks>
    private async Task SwitchQualityAsync()
    {
        // 还没进过 MV 页（或已经离场）时什么都不做：那时候改的是「下次进去用哪档」，
        // 而首次加载本来就会带上当前档位。
        if (_musicId <= 0 || _disposed)
        {
            return;
        }

        var resume = TimeSpan.FromSeconds(PositionSeconds);
        StatusText = $"正在切换到{QualityLabel}…";
        StatusIsError = false;

        // ★ 连点换档只让最后一次落地。
        // 每次换档都会给 MediaPlayer 赋一次 Source，两次叠在一起会把播放器塞进坏状态 ——
        // 表现是「切得快就卡住，之后换哪一档都播不了」。取消上一次 + 丢弃过期响应即可避免。
        _switchCts?.Cancel();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _switchCts = cts;
        var token = cts.Token;

        _logger.LogInformation(
            "MV 换档：请求 {Quality}，准备从 {Position:F1}s 续播", Quality, resume.TotalSeconds);

        try
        {
            if (await _api.GetMvInfoAsync(_musicId, Quality, token).ConfigureAwait(true) is not { } mv)
            {
                StatusText = "";
                _logger.LogInformation("MV 换档 {Quality}：服务端没给直链，保持原状", Quality);
                return;
            }

            // 响应回来了，但这期间用户又点了一档 —— 这次的结果已经过期，丢掉。
            if (token.IsCancellationRequested || _disposed)
            {
                _logger.LogInformation("MV 换档 {Quality}：已被后一次换档取代，丢弃", Quality);
                return;
            }

            // ★ 先在**备用播放器**上把新源打开，成功了才切过去。
            //   实测取流会偶发悬住（既不打开也不报错），直接换源的话现役那条已经没了，
            //   画面就停在黑屏 + 进度 0。准备期间现役照常放着，所以那种情况用户只会看到一条状态。
            var prepared = await PrepareAsync(mv, token).ConfigureAwait(true);
            if (prepared is null)
            {
                return;
            }

            // 续播由 Promote 在里面武装（装上新源之后才设），
            // 否则装源前那 200ms 里轮询会把这次定位消耗在**旧源**上。
            if (token.IsCancellationRequested || _disposed) { ReleasePlayer(prepared); return; }
            Promote(prepared, mv, resume);

            _logger.LogInformation("MV 换档 {Quality}：已切过去，续播 {Position:F1}s",
                Quality, resume.TotalSeconds);
        }
        catch (OperationCanceledException)
        {
            // 被后一次换档取代，不是错误。
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MV 切换画质（{Quality}）失败", Quality);
            StatusIsError = true;
            StatusText = "切换画质失败，仍在放原来那一档。";
        }
        finally
        {
            if (ReferenceEquals(_switchCts, cts)) _switchCts = null;
            cts.Dispose();
        }
    }

    /// <summary>
    /// 在备用播放器上把新源打开。<b>失败返回 <c>null</c>，调用方什么都不用改。</b>
    /// </summary>
    /// <remarks>
    /// 这是「先备好再切」的核心：准备期间现役那条照常放着，所以取流悬住时用户只是
    /// 看到一条状态 —— 画面不会黑、进度不会掉到 0。
    /// <c>MediaOpened</c> / <c>MediaFailed</c> 都在后台线程触发，这里靠置标志 + 轮询等待，
    /// 不碰 UI；真正的定位留给扶正之后的 <see cref="Poll"/>。
    /// </remarks>
    private async Task<MediaPlayer?> PrepareAsync(MvInfo mv, CancellationToken token)
    {
        _releaseStaging?.Invoke();
        var staging = NewPlayer();
        var opened = 0;
        var failed = 0;
        var released = 0;
        var prepared = false;
        void Release()
        {
            if (Interlocked.Exchange(ref released, 1) == 0) ReleasePlayer(staging);
        }
        Action release = Release;
        _releaseStaging = release;
        staging.MediaOpened += (_, _) => Interlocked.Exchange(ref opened, 1);
        staging.MediaFailed += (_, _) => Interlocked.Exchange(ref failed, 1);
        try
        {
            staging.Source = MediaSource.CreateFromUri(mv.VideoUrl);
            for (var waited = 0;
                 waited < SourceOpenTimeoutMs && Volatile.Read(ref opened) == 0 && Volatile.Read(ref failed) == 0 && !_disposed;
                 waited += 100)
                await Task.Delay(100, token).ConfigureAwait(true);

            if (Volatile.Read(ref opened) != 0 && !token.IsCancellationRequested && !_disposed)
            {
                prepared = true;
                return staging;
            }
            if (!token.IsCancellationRequested && !_disposed)
            {
                StatusIsError = true;
                StatusText = "这一档没能加载出来，仍在放原来那一档。";
                _logger.LogWarning("MV 换档：新源在 {Timeout}ms 内没打开，放弃切换", SourceOpenTimeoutMs);
            }
            return null;
        }
        finally
        {
            if (ReferenceEquals(_releaseStaging, release)) _releaseStaging = null;
            // 包含 Task.Delay 被取消、快速连点换档和退出页面，不能留下备用解码器。
            if (!prepared) Release();
        }
    }

    /// <summary>
    /// 把已经准备好的播放器扶正：绑到元素上、接着放、恢复进度，旧的释放掉。
    /// </summary>
    /// <remarks>
    /// <b>顺序不能反</b>：先把新实例绑上去，旧实例才释放。<c>Player</c> 的变更通知是同步的，
    /// 页面在处理器里 <c>SetMediaPlayer</c>，所以赋值那一句返回时旧实例已经从元素上摘下来了 ——
    /// 与 <c>MvPage.OnNavigatedFrom</c> 那条「先解绑再释放」同源。
    /// </remarks>
    private void Promote(MediaPlayer prepared, MvInfo mv, TimeSpan resume)
    {
        var previous = Player;

        _mv = mv;
        HasSource = true;
        PreviewLimit = mv.PreviewLimit;
        StatusText = "";
        StatusIsError = false;

        Player = prepared;

        // 备用播放器已经把源打开过了，轮询可以直接定位，不必再等第二遍。
        // 续播**在这里**才武装：装源之前设会被轮询消耗在旧源上。
        _sourceOpened = true;
        _awaitingOpenSince = 0;
        _resumeAfterOpen = resume;

        prepared.Play();
        _positionTimer.Start();
        IsPlaying = true;

        ReleasePlayer(previous);
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

        // 换档之后把位置续回去。
        // ★ **等新源真的开了、而且会话可 seek 了再动** —— 换源后立刻设 Position 会被丢掉：
        //   位置停在 0、画面也出不来。首次加载没有这一步，所以只有换档才踩得到。
        //   放在轮询里而不是 MediaOpened 回调里：那个回调在后台线程，碰 PlaybackSession 要再 marshal，
        //   而且「事件到了」不等于「已经能 seek」，让 200ms 的轮询等到条件成立更稳。
        if (_resumeAfterOpen is { } resume && _sourceOpened && session.CanSeek)
        {
            _resumeAfterOpen = null;
            session.Position = resume;
            _logger.LogInformation("MV 换档续播：已定位到 {Position:F1}s", resume.TotalSeconds);
        }

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

        // 看门狗：新源迟迟打不开时给一个可点的「重新加载」，别停在黑屏 + 进度 0 上等人去猜。
        // 换档偶发打不开是实测到的（见 docs/mv.md 第 3 节），根因还没查出来，先保证不至于走投无路。
        if (_sourceOpened)
        {
            _awaitingOpenSince = 0;
        }
        else if (_awaitingOpenSince != 0
            && Environment.TickCount64 - _awaitingOpenSince > SourceOpenTimeoutMs)
        {
            _awaitingOpenSince = 0;
            _logger.LogWarning("MV 新源 {Timeout}ms 内没有打开，判定这一档加载失败", SourceOpenTimeoutMs);
            StatusIsError = true;
            StatusText = "这一档没能加载出来。点下面的重新加载，或换一档。";
        }
    }

    /// <summary>
    /// 重新加载：按当前档位再取一次直链，并<b>从当前进度续播</b>。
    /// </summary>
    /// <remarks>
    /// 新源偶发打不开时的手动恢复入口（见看门狗）。走的就是换档那条路 ——
    /// 用户手上已经有画面/位置，重来一次与换一档没有区别。
    /// </remarks>
    [RelayCommand]
    private Task RetryAsync() => SwitchQualityAsync();

    private void OnPositionTick(DispatcherQueueTimer sender, object args) => Poll();

    public void SetPresentationSuspended(bool suspended)
    {
        if (_disposed) return;
        _presentationSuspended = suspended;
        // 后台保留播放和试看限制；没有试看限制时减少 UI 进度属性更新。
        _positionTimer.Interval = TimeSpan.FromMilliseconds(suspended && PreviewLimit is null ? 1000 : PositionPollMilliseconds);
        if (!suspended) Poll();
    }

    partial void OnPreviewLimitChanged(TimeSpan? value)
        => _positionTimer.Interval = TimeSpan.FromMilliseconds(_presentationSuspended && value is null ? 1000 : PositionPollMilliseconds);

    private void ReleasePlayer(MediaPlayer player)
    {
        player.MediaFailed -= OnMediaFailed;
        var source = player.Source;
        player.Source = null;
        (source as IDisposable)?.Dispose();
        player.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();
        _switchCts?.Cancel();
        _resumeAfterOpen = null;
        _positionTimer.Stop();
        _positionTimer.Tick -= OnPositionTick;
        _releaseStaging?.Invoke();
        _releaseStaging = null;
        ReleasePlayer(Player);
        _lifetime.Dispose();
    }
}
