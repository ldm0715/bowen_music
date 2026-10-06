using System.Collections.ObjectModel;
using Bodian.Core.Api;
using Bodian.Core.Models;
using Bodian.Core.Services;
using Bodian.Core.Services.Abstractions;
using Bodian.WinUI.Media;
using Bodian.WinUI.Playback;
using Bodian.WinUI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Xaml.Media;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 底部播放条。**单例**，跨页面常驻。
/// </summary>
/// <remarks>
/// 因为是单例，<b>不要在页面卸载时退订引擎事件</b> —— 那会导致从别的页面回来后进度条不动。
/// 订阅在构造函数里做一次，生命周期与进程一致。
/// </remarks>
public sealed partial class PlayerViewModel : ObservableObject, IVolumeSource
{
    private readonly PlaybackCoordinator _coordinator;
    private readonly IPlaybackService _engine;
    private readonly NotificationViewModel _notifications;
    private readonly BodianSession _session;
    private readonly IClipboardService _clipboard;
    private readonly ILogger<PlayerViewModel> _logger;

    public PlayerViewModel(
        PlaybackCoordinator coordinator,
        IPlaybackService engine,
        TrackStatisticsViewModel statistics,
        NotificationViewModel notifications,
        BodianSession session,
        IClipboardService clipboard,
        ILogger<PlayerViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(statistics);
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(clipboard);
        Statistics = statistics;

        _coordinator = coordinator;
        _engine = engine;
        _notifications = notifications;
        _session = session;
        _clipboard = clipboard;
        _logger = logger ?? NullLogger<PlayerViewModel>.Instance;

        _coordinator.QualityOptionsChanged += (_, _) => RefreshQualityOptions();
        _coordinator.QualityChanged += (_, e) =>
        {
            QualityText = AudioQualityTable.Describe(e.Source);
            PositionSeconds = e.Position.TotalSeconds;
            IsPlaying = _engine.State == PlaybackState.Playing;

            if (e.Source.WasDowngraded)
            {
                _notifications.Show(QualityText);
            }

            RefreshQualityOptions();
        };
        _coordinator.QualityChangeFailed += (_, e) => _notifications.Show(e.Message, NoticeSeverity.Error);
        _coordinator.Started += OnStarted;
        _coordinator.Blocked += OnBlocked;
        _coordinator.AuditionEnded += OnAuditionEnded;
        _coordinator.QueueExhausted += OnQueueExhausted;
        _coordinator.Queue.Changed += OnQueueChanged;
        Mode = _coordinator.Mode;

        _engine.PositionChanged += OnPositionChanged;
        _engine.StateChanged += OnEngineStateChanged;
        _engine.Failed += OnEngineFailed;
        RefreshQualityOptions();
    }

    public TrackStatisticsViewModel Statistics { get; }

    public ObservableCollection<AudioQualityOption> QualityOptions { get; } = [];

    [ObservableProperty]
    public partial AudioQualityOption? SelectedQualityOption { get; set; }

    /// <summary>当前正在用的档位，供音质胶囊取色。还没取到音源时为 <c>null</c>。</summary>
    [ObservableProperty]
    public partial AudioQuality? CurrentQuality { get; set; }

    [ObservableProperty]
    public partial bool CanChangeQuality { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQualityStatus))]
    public partial string QualityStatus { get; set; } = "";

    public bool HasQualityStatus => QualityStatus.Length > 0;

    public Task SwitchQualityAsync(AudioQuality quality) => _coordinator.SwitchQualityAsync(quality);

    private void RefreshQualityOptions()
    {
        var track = _coordinator.CurrentTrack;
        var audition = _coordinator.CurrentPolicy?.IsAudition == true;
        var busy = _coordinator.IsChangingQuality;
        var current = _coordinator.CurrentSource is { } source
            ? AudioQualityTable.ServedQuality(source) : _coordinator.PreferredQuality;
        CanChangeQuality = !busy;
        CurrentQuality = current;
        QualityStatus = busy ? "正在切换音质…" : audition ? "试听片段不支持音质切换" : "";
        SelectedQualityOption = null;
        QualityOptions.Clear();
        AudioQualityOption? selected = null;
        AudioQuality[] order = [AudioQuality.Standard, AudioQuality.High, AudioQuality.Lossless];
        foreach (var quality in order)
        {
            var variant = track?.AudioVariants.Where(v => AudioQualityTable.IsSupportedVariant(v) && v.Quality == quality)
                .OrderByDescending(v => v.BitrateKbps).FirstOrDefault();
            var available = track is null || track.AvailableQualities.Contains(quality);
            var size = variant?.SizeBytes ?? 0;
            if (current == quality && _coordinator.CurrentSource is { SizeBytes: > 0 } actual) { size = actual.SizeBytes; }
            var option = new AudioQualityOption(quality, AudioQualityTable.DisplayName(quality),
                available ? AudioQualityTable.FormatSize(size) : "暂无音源", available && !audition && !busy,
                current == quality);
            QualityOptions.Add(option);
            if (current == quality) { selected = option; }
        }
        SelectedQualityOption = selected;
    }

    /// <summary>播放条是否该显示。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NowPlayingText))]
    public partial bool HasTrack { get; set; }

    /// <summary>当前这首歌有没有 MV。驱动播放条上那颗 MV 按钮的显隐（没有就折叠，不置灰）。</summary>
    [ObservableProperty]
    public partial bool HasMv { get; set; }

    /// <summary>
    /// 当前曲目。供外壳构造 MV 页用。
    /// </summary>
    /// <remarks>
    /// <b>刻意不是 public</b>：XAML 类型信息生成器会为公开属性里的类型生成激活代码，
    /// 而 <see cref="Track"/> 有 <c>required</c> 成员 —— 生成器造不出实例，直接编译失败。
    /// 同一个坑见 <c>MvPage.Track</c>。
    /// </remarks>
    internal Track? CurrentTrack { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NowPlayingText))]
    public partial string Title { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NowPlayingText))]
    public partial string ArtistText { get; set; } = "";

    [ObservableProperty]
    public partial string AlbumText { get; set; } = "";

    /// <summary>
    /// 托盘菜单顶部那一行：「歌名 - 歌手」。
    /// </summary>
    /// <remarks>
    /// 没有曲目时给一句占位而不是空串 —— 菜单里的空行看起来像渲染坏了。
    /// 歌手名留空时不补那个连字符，否则会显示成「歌名 - 」。
    /// </remarks>
    public string NowPlayingText => !HasTrack
        ? "未在播放"
        : ArtistText.Length == 0 ? Title : $"{Title} - {ArtistText}";

    /// <summary>封面。为 <c>null</c> 时界面显示占位底色。</summary>
    [ObservableProperty]
    public partial ImageSource? CoverImage { get; set; }

    /// <summary>
    /// 当前封面的**原始地址**。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="CoverImage"/> 是两回事：那个是给界面直接显示的 <c>BitmapImage</c>，
    /// 而氛围背景要拿地址自己去解码取色 —— 从 <c>ImageSource</c> 里拿不回地址。
    /// 取色那一步会把它改写成 <c>.jpg</c> 小图，见 <c>CoverPaletteLoader</c>。
    /// </remarks>
    [ObservableProperty]
    public partial Uri? CurrentCoverUri { get; set; }

    /// <summary>
    /// 付费标识（<c>VIP</c> / <c>付费</c> / 空）。**只是展示** ——
    /// 能不能播由服务端的 checkRight 裁决，不看这个。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPayLabel))]
    public partial string PayLabel { get; set; } = "";

    public bool HasPayLabel => PayLabel.Length > 0;

    /// <summary>实际产品音质、大小及降级提示，不在界面暴露格式或码率参数。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QualityLabel))]
    public partial string QualityText { get; set; } = "";

    public string QualityLabel => QualityText.Length == 0 ? "音质"
        : IsAudition ? "试听"
        : _coordinator.CurrentSource is { } source && AudioQualityTable.ServedQuality(source) is { } quality
            ? AudioQualityTable.DisplayName(quality) : "音质未知";

    [ObservableProperty]
    public partial bool IsPlaying { get; set; }

    /// <summary>
    /// 当前曲目的 <c>musicId</c>。曲目列表用它决定哪一行显示「正在播放」。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>这是「正在播放」的唯一权威来源。</b> 页面 ViewModel 上那个同名的 <c>CurrentTrack</c>
    /// 语义是「本页最后被点播的曲目」—— <b>自动下一首不会更新它，被拒绝的曲目也会写进去</b>，
    /// 拿它做高亮会在自动切歌时停在原地。
    /// </para>
    /// <para>
    /// 播不成的曲目也照样赋值：用户得看见「刚才点的是这一首、它正在播不了」，
    /// 这与播放条的行为一致（播放条在被拒时同样会显示曲目信息）。
    /// </para>
    /// </remarks>
    [ObservableProperty]
    public partial long? CurrentTrackId { get; set; }

    /// <summary>当前放的是试听片段。</summary>
    [ObservableProperty]
    public partial bool IsAudition { get; set; }

    [ObservableProperty]
    public partial double PositionSeconds { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressMaximum))]
    public partial double DurationSeconds { get; set; }

    /// <summary>
    /// 进度条的上限。
    /// </summary>
    /// <remarks>
    /// <b>不能让它是 0。</b> Slider 在 <c>Maximum</c> 与 <c>Minimum</c> 相等时值域为零，
    /// 轨道宽度算出来是 0，视觉上就是「根本没有进度条」——而 <see cref="DurationSeconds"/>
    /// 在拿到真实时长之前恰好是 0。给它一个非零保底，控件就始终可见。
    /// </remarks>
    public double ProgressMaximum => Math.Max(DurationSeconds, 1);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMuted))]
    public partial double Volume { get; set; } = 100;
    /// <summary>是否静音。音量按钮据此在中/静音两个图标之间切。</summary>
    public bool IsMuted => Volume <= 0;

    [ObservableProperty]
    public partial bool CanGoNext { get; set; }

    [ObservableProperty]
    public partial bool CanGoPrevious { get; set; }

    /// <summary>
    /// 当前播放模式。
    /// </summary>
    /// <remarks>
    /// <b>不从命令里赋值，只跟着队列走</b>：模式是队列的状态，面板和系统媒体控件也可能改它，
    /// 各入口自己刷一遍迟早会漏。这里统一在 <see cref="OnQueueChanged"/> 里同步。
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSequentialMode))]
    [NotifyPropertyChangedFor(nameof(IsListLoopMode))]
    [NotifyPropertyChangedFor(nameof(IsShuffleMode))]
    [NotifyPropertyChangedFor(nameof(PlayModeText))]
    public partial PlayMode Mode { get; set; }

    /// <summary>三个模式图标是叠在同一个按钮里的，靠这三个开关选一个显示。</summary>
    public bool IsSequentialMode => Mode == PlayMode.Sequential;

    public bool IsListLoopMode => Mode == PlayMode.ListLoop;

    public bool IsShuffleMode => Mode == PlayMode.Shuffle;

    public string PlayModeText => Mode.DisplayName();

    /// <summary>
    /// 用户正在拖动进度条。由界面在按下/松开时设置。
    /// </summary>
    /// <remarks>
    /// 拖动期间不能再用引擎的进度覆盖滑块 —— 否则手指还没松开，滑块就被拽回去了。
    /// </remarks>
    public bool IsSeeking { get; set; }

    /// <summary>进度条拖完时由界面调用。</summary>
    /// <remarks>
    /// <b>引擎里没有加载文件时这次拖动不作数。</b> mpv 放完最后一首后回到 idle，
    /// <c>time-pos</c> 属性不存在，设它会抛 <c>property unavailable</c> 并弹一条红条；
    /// 而引擎的失败处理还会把状态砸成 <see cref="PlaybackState.Idle"/>，把进度条打到 0。
    /// 这个状态下本来也没有「跳转」可言，直接吞掉这次拖动。
    /// </remarks>
    public Task SeekToAsync(double seconds)
    {
        IsSeeking = false;

        if (!HasTrack || _engine.State is PlaybackState.Idle or PlaybackState.Stopped)
        {
            // 滑块已经被拖走了，而引擎不会再发进度把它拉回来 —— 手动把当前进度重发一次拨回原位。
            OnPropertyChanged(nameof(PositionSeconds));

            return Task.CompletedTask;
        }

        return _engine.SeekAsync(TimeSpan.FromSeconds(seconds));
    }

    [RelayCommand]
    private async Task TogglePlayPauseAsync()
    {
        if (!HasTrack)
        {
            return;
        }

        _logger.LogDebug(
            "切换播放：界面认为 IsPlaying={IsPlaying}，引擎实际状态={State}",
            IsPlaying,
            _engine.State);

        if (IsPlaying)
        {
            await _engine.PauseAsync();
        }
        else
        {
            // ★ 不能直接调引擎的 PlayAsync()：放完最后一首后 mpv 已回到 idle，
            //   设 pause=false 是空操作（界面还会被乐观更新成「正在播放」）。协调器负责判断
            //   该重新加载哪一首，见 PlaybackCoordinator.PlayAsync。
            await _coordinator.PlayAsync();
        }
    }

    [RelayCommand]
    private Task NextAsync() => _coordinator.NextAsync();

    [RelayCommand]
    private Task PreviousAsync() => _coordinator.PreviousAsync();

    /// <summary>按「顺序播放 → 列表循环 → 列表随机」切到下一个模式。</summary>
    [RelayCommand]
    private void CyclePlayMode() => _coordinator.CyclePlayMode();

    /// <summary>
    /// 直接切到指定模式。托盘菜单的模式子菜单按项指定时用它 ——
    /// 播放条那颗按钮仍然是「循环切下一个」（见 <see cref="CyclePlayMode"/>），两者语义不同。
    /// </summary>
    /// <remarks>
    /// <b>这里不写 <c>Mode = mode</c></b>：模式是队列的状态，<see cref="Mode"/> 的注释已经写死
    /// 「只跟着队列走」。多写一处赋值就是第二个真相来源，主界面切模式时两边会不同步。
    /// </remarks>
    [RelayCommand]
    private void SetPlayMode(PlayMode mode) => _coordinator.SetPlayMode(mode);

    /// <summary>喜欢 / 取消喜欢当前曲目。已喜欢时点按是取消。</summary>
    [RelayCommand]
    private async Task ToggleFavoriteAsync()
    {
        var outcome = await Statistics.ToggleFavoriteAsync();

        // 成功与「已在处理中」都不发提示：心形图标自己会变，再弹一条只是噪音。
        // 空串会被通知服务忽略。
        _notifications.Show(
            outcome switch
            {
                LikedSongsOutcome.Succeeded => "",
                LikedSongsOutcome.NotAuthenticated => "登录后可以喜欢。",
                LikedSongsOutcome.NoLikedPlaylist => "账号还没有「我喜欢的」歌单，暂时无法喜欢。",
                LikedSongsOutcome.AlreadyPending => "",
                _ => "操作没成功，请稍后再试。",
            },
            NoticeSeverity.Error);
    }

    /// <summary>
    /// 分享当前曲目：先上报（分享数 +1），再把本地拼的链接复制到剪贴板。
    /// </summary>
    /// <remarks>
    /// 顺序照官方：「先上报并取文案 → 再拼链」。上报失败不影响复制 ——
    /// 链接不发请求，只有分享计数依赖服务端。
    /// </remarks>
    [RelayCommand]
    private async Task ShareAsync()
    {
        if (CurrentTrackId is not { } musicId || musicId <= 0) return;

        var outcome = await Statistics.ReportShareAsync();

        try
        {
            _clipboard.SetText(ShareLinks.BuildTrackLink(musicId, _session.Uid));
            _logger.LogInformation("已复制分享链接：歌曲 {MusicId}，上报结果 {Outcome}", musicId, outcome);
            _notifications.Show(
                outcome == ShareOutcome.Unsupported ? "该歌曲暂不支持分享，链接已复制。" : "已复制分享链接",
                NoticeSeverity.Success);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "复制分享链接失败：歌曲 {MusicId}", musicId);
            _notifications.Show("复制链接失败。", NoticeSeverity.Error);
        }
    }

    partial void OnVolumeChanged(double value) => _engine.SetVolume(value);

    // ── 引擎事件 ────────────────────────────────────────────────────────────

    private void OnPositionChanged(object? sender, PlaybackPositionChangedEventArgs e)
    {
        if (IsSeeking)
        {
            return;
        }

        PositionSeconds = e.Position.TotalSeconds;

        if (e.Duration > TimeSpan.Zero)
        {
            DurationSeconds = e.Duration.TotalSeconds;
        }
    }

    private void OnEngineStateChanged(object? sender, PlaybackStateChangedEventArgs e)
    {
        IsPlaying = e.State == PlaybackState.Playing;

        if (e.State == PlaybackState.Idle)
        {
            PositionSeconds = 0;
        }
    }

    private void OnEngineFailed(object? sender, PlaybackFailedEventArgs e)
    {
        _notifications.Show(e.Message, NoticeSeverity.Error);
    }

    // ── 协调器事件 ──────────────────────────────────────────────────────────

    private void OnStarted(object? sender, PlaybackStartedEventArgs e)
    {
        HasTrack = true;
        CurrentTrack = e.Track;
        HasMv = e.Track.HasMv;
        Title = e.Track.Title;
        ArtistText = e.Track.ArtistText;
        ApplyTrackDetails(e.Track);
        // 先启动共享详情请求，再通知歌词页切歌，评论角标才能复用同一请求。
        CurrentTrackId = e.Track.Id;
        IsAudition = e.Policy.IsAudition;
        QualityText = AudioQualityTable.Describe(e.Source, e.Policy.IsAudition);
        PositionSeconds = 0;

        // 试听时进度条的上限是试听终点，不是整曲时长 ——
        // 否则会出现「走到 4:29 却停在 0:29」的错觉。
        DurationSeconds = e.Policy.StopAt?.TotalSeconds ?? 0;

        IsPlaying = _engine.State == PlaybackState.Playing;
        RefreshQualityOptions();
        UpdateQueueButtons();
    }

    private void OnBlocked(object? sender, PlaybackBlockedEventArgs e)
    {
        HasTrack = true;
        CurrentTrack = e.Track;
        HasMv = e.Track.HasMv;
        Title = e.Track.Title;
        ArtistText = e.Track.ArtistText;
        ApplyTrackDetails(e.Track);
        // 先启动共享详情请求，再通知歌词页切歌，评论角标才能复用同一请求。
        CurrentTrackId = e.Track.Id;
        QualityText = "";
        IsPlaying = false;
        IsAudition = false;
        PositionSeconds = 0;
        DurationSeconds = 0;

        _notifications.Show(
            e.Reason switch
            {
                PlaybackDenialReason.NotAuthenticated => "登录后可播放完整歌曲",
                PlaybackDenialReason.NoPermission => "该歌曲暂无播放权限",
                PlaybackDenialReason.NoUsableQuality => "该歌曲在当前客户端没有可用音质",
                PlaybackDenialReason.TrackUnavailable => "该歌曲已下架，无法播放",
                PlaybackDenialReason.AuditionUnavailable => "该歌曲只能试听，但试听片段取不到",
                PlaybackDenialReason.NoStreamUrl => e.Detail is { Length: > 0 } detail
                    ? $"取不到音源：{detail}"
                    : "取不到音源地址",
                _ => "暂时无法播放这首歌",
            },
            NoticeSeverity.Error);

        UpdateQueueButtons();
    }

    private void OnAuditionEnded(object? sender, EventArgs e)
    {
        IsPlaying = false;
        _notifications.Show("试听片段已结束，登录后可播放完整歌曲");
    }

    private void OnQueueExhausted(object? sender, EventArgs e)
    {
        IsPlaying = false;
        _notifications.Show("已经是最后一首了");
    }

    private void OnQueueChanged(object? sender, EventArgs e)
    {
        UpdateQueueButtons();
        Mode = _coordinator.Mode;
    }

    private void UpdateQueueButtons()
    {
        CanGoNext = _coordinator.Queue.HasNext;
        CanGoPrevious = _coordinator.Queue.HasPrevious;
    }

    /// <summary>
    /// 把曲目上的展示信息铺到播放条上（专辑 / 封面 / 付费标识）。
    /// </summary>
    /// <remarks>
    /// 播不成的时候也要铺 —— 用户得知道刚才想播的是哪一首、以及它为什么播不了。
    /// </remarks>
    private void ApplyTrackDetails(Track track)
    {
        AlbumText = track.AlbumName ?? "";

        // BitmapImage 自己异步加载；地址失效时图是空的，不影响布局。
        CoverImage = CoverImageCache.Get(track.CoverImage, 1024);

        // 氛围背景靠这个自己去解码取色。
        CurrentCoverUri = track.CoverImage;

        PayLabel = track.RequiresVip ? "VIP"
            : track.RequiresPurchase ? "付费"
            : "";
        _ = Statistics.LoadAsync(track);
    }

}

/// <summary>音质弹层里的一档。</summary>
/// <param name="IsCurrent">是否当前正在用的档位。弹层里当前档摆满、其余收暗。</param>
public sealed record AudioQualityOption(AudioQuality Quality, string Name, string SizeText, bool IsEnabled, bool IsCurrent);
