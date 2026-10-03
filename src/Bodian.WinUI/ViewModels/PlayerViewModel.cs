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
public sealed partial class PlayerViewModel : ObservableObject
{
    private readonly PlaybackCoordinator _coordinator;
    private readonly IPlaybackService _engine;
    private readonly BodianSession _session;
    private readonly IClipboardService _clipboard;
    private readonly ILogger<PlayerViewModel> _logger;

    /// <summary>最近一条定时提示的序号，用来判断「到期该清的是不是我这一条」。</summary>
    private int _noticeGeneration;

    public PlayerViewModel(
        PlaybackCoordinator coordinator,
        IPlaybackService engine,
        TrackStatisticsViewModel statistics,
        BodianSession session,
        IClipboardService clipboard,
        ILogger<PlayerViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(statistics);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(clipboard);
        Statistics = statistics;

        _coordinator = coordinator;
        _engine = engine;
        _session = session;
        _clipboard = clipboard;
        _logger = logger ?? NullLogger<PlayerViewModel>.Instance;

        _coordinator.QualityOptionsChanged += (_, _) => RefreshQualityOptions();
        _coordinator.QualityChanged += (_, e) =>
        {
            QualityText = AudioQualityTable.Describe(e.Source);
            PositionSeconds = e.Position.TotalSeconds;
            IsPlaying = _engine.State == PlaybackState.Playing;
            Notice = e.Source.WasDowngraded ? QualityText : "";
            RefreshQualityOptions();
        };
        _coordinator.QualityChangeFailed += (_, e) => Notice = e.Message;
        _coordinator.Started += OnStarted;
        _coordinator.Blocked += OnBlocked;
        _coordinator.AuditionEnded += OnAuditionEnded;
        _coordinator.QueueExhausted += OnQueueExhausted;
        _coordinator.Queue.Changed += (_, _) => UpdateQueueButtons();

        _engine.PositionChanged += OnPositionChanged;
        _engine.StateChanged += OnEngineStateChanged;
        _engine.Failed += OnEngineFailed;
        RefreshQualityOptions();
    }

    public TrackStatisticsViewModel Statistics { get; }

    public ObservableCollection<AudioQualityOption> QualityOptions { get; } = [];

    [ObservableProperty]
    public partial AudioQualityOption? SelectedQualityOption { get; set; }

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
                available ? AudioQualityTable.FormatSize(size) : "暂无音源", available && !audition && !busy);
            QualityOptions.Add(option);
            if (current == quality) { selected = option; }
        }
        SelectedQualityOption = selected;
    }

    /// <summary>播放条是否该显示。</summary>
    [ObservableProperty]
    public partial bool HasTrack { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; } = "";

    [ObservableProperty]
    public partial string ArtistText { get; set; } = "";

    [ObservableProperty]
    public partial string AlbumText { get; set; } = "";

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
    [NotifyPropertyChangedFor(nameof(VolumeGlyph))]
    public partial double Volume { get; set; } = 100;

    /// <summary>音量按钮上的图标。静音与有声用两个字形，按钮才不只是个「点我」的方块。</summary>
    public string VolumeGlyph => Volume <= 0 ? "\uE74F" : "\uE767";

    [ObservableProperty]
    public partial bool CanGoNext { get; set; }

    [ObservableProperty]
    public partial bool CanGoPrevious { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    public partial string Notice { get; set; } = "";

    public bool HasNotice => Notice.Length > 0;

    /// <summary>
    /// 显示一条到点自己消失的提示。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Notice"/> 平时只在整曲开始播放时被清空，播放条上那个 InfoBar 又设了不可关闭 ——
    /// 「已加入歌单」这类一次性反馈挂在那里会一直留到下一首播完。所以行内动作走这里。
    /// </para>
    /// <para>
    /// <b>到期时还要比对文案，不只看序号</b>：播放引擎自己也会写 <see cref="Notice"/>
    /// （音质降级、播放被拒），那些不经过这里。中途被别的提示顶掉时就不清，
    /// 免得把播放的提示误清掉。
    /// </para>
    /// </remarks>
    public void TransientNotice(string message, TimeSpan? duration = null)
    {
        Notice = message;

        if (message.Length == 0)
        {
            return;
        }

        var generation = ++_noticeGeneration;
        _ = ClearNoticeAfterAsync(generation, message, duration ?? TimeSpan.FromSeconds(3));
    }

    private async Task ClearNoticeAfterAsync(int generation, string message, TimeSpan delay)
    {
        await Task.Delay(delay).ConfigureAwait(true);

        if (generation == _noticeGeneration && Notice == message)
        {
            Notice = "";
        }
    }

    /// <summary>
    /// 用户正在拖动进度条。由界面在按下/松开时设置。
    /// </summary>
    /// <remarks>
    /// 拖动期间不能再用引擎的进度覆盖滑块 —— 否则手指还没松开，滑块就被拽回去了。
    /// </remarks>
    public bool IsSeeking { get; set; }

    /// <summary>进度条拖完时由界面调用。</summary>
    public Task SeekToAsync(double seconds)
    {
        IsSeeking = false;

        return HasTrack ? _engine.SeekAsync(TimeSpan.FromSeconds(seconds)) : Task.CompletedTask;
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
            await _engine.PlayAsync();
        }
    }

    [RelayCommand]
    private Task NextAsync() => _coordinator.NextAsync();

    [RelayCommand]
    private Task PreviousAsync() => _coordinator.PreviousAsync();

    /// <summary>喜欢 / 取消喜欢当前曲目。已喜欢时点按是取消。</summary>
    [RelayCommand]
    private async Task ToggleFavoriteAsync()
    {
        var outcome = await Statistics.ToggleFavoriteAsync();
        Notice = outcome switch
        {
            LikedSongsOutcome.Succeeded => "",
            LikedSongsOutcome.NotAuthenticated => "登录后可以喜欢。",
            LikedSongsOutcome.NoLikedPlaylist => "账号还没有「我喜欢的」歌单，暂时无法喜欢。",
            LikedSongsOutcome.AlreadyPending => "",
            _ => "操作没成功，请稍后再试。",
        };
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
            Notice = outcome == ShareOutcome.Unsupported ? "该歌曲暂不支持分享，链接已复制。" : "已复制分享链接";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "复制分享链接失败：歌曲 {MusicId}", musicId);
            Notice = "复制链接失败。";
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
        Notice = e.Message;
    }

    // ── 协调器事件 ──────────────────────────────────────────────────────────

    private void OnStarted(object? sender, PlaybackStartedEventArgs e)
    {
        HasTrack = true;
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

        Notice = "";
        IsPlaying = _engine.State == PlaybackState.Playing;
        RefreshQualityOptions();
        UpdateQueueButtons();
    }

    private void OnBlocked(object? sender, PlaybackBlockedEventArgs e)
    {
        HasTrack = true;
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

        Notice = e.Reason switch
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
        };

        UpdateQueueButtons();
    }

    private void OnAuditionEnded(object? sender, EventArgs e)
    {
        IsPlaying = false;
        Notice = "试听片段已结束，登录后可播放完整歌曲";
    }

    private void OnQueueExhausted(object? sender, EventArgs e)
    {
        IsPlaying = false;
        Notice = "已经是最后一首了";
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

public sealed record AudioQualityOption(AudioQuality Quality, string Name, string SizeText, bool IsEnabled);
