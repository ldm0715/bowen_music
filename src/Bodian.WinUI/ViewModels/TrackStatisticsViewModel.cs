using Bodian.Core.Api;
using Bodian.Core.Models;
using Bodian.Core.Services.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.WinUI.ViewModels;

/// <summary>当前歌曲的统计与喜欢状态，播放器与歌词页共享；详情请求可供评论角标复用。</summary>
public sealed partial class TrackStatisticsViewModel : ObservableObject, IDisposable
{
    private readonly IBodianApi _api;
    private readonly ILikedSongsService? _likedSongs;
    private readonly ILogger<TrackStatisticsViewModel> _logger;
    private CancellationTokenSource? _request;
    private Task<Track?>? _details;
    private Track? _track;
    private int _generation;
    private bool _disposed;

    /// <param name="likedSongs">
    /// 喜欢状态的来源。为 <c>null</c> 时按钮只展示计数、不参与喜欢往返（离线测试与无此依赖的宿主）。
    /// </param>
    public TrackStatisticsViewModel(IBodianApi api, ILogger<TrackStatisticsViewModel>? logger = null,
        ILikedSongsService? likedSongs = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
        _likedSongs = likedSongs;
        _logger = logger ?? NullLogger<TrackStatisticsViewModel>.Instance;
    }

    public long? MusicId => _track?.Id;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FavoriteBadgeText))]
    [NotifyPropertyChangedFor(nameof(HasFavoriteCount))]
    [NotifyPropertyChangedFor(nameof(FavoriteAutomationName))]
    public partial long? FavoriteCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShareBadgeText))]
    [NotifyPropertyChangedFor(nameof(HasShareCount))]
    [NotifyPropertyChangedFor(nameof(ShareAutomationName))]
    public partial long? ShareCount { get; set; }

    /// <summary>当前曲目是否已在「我喜欢的」歌单里。</summary>
    /// <remarks>
    /// 服务端没有这个字段（<c>music/info</c> 只给全站 <c>favorite</c> 计数），
    /// 值来自 <see cref="ILikedSongsService"/> 自己维护的集合。
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FavoriteAutomationName))]
    public partial bool IsFavorite { get; set; }

    /// <summary>写请求进行中。用来防止重复点击。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanToggleFavorite))]
    public partial bool IsFavoriteBusy { get; set; }

    public bool CanToggleFavorite => !IsFavoriteBusy;

    public string FavoriteBadgeText => FavoriteCount is { } count ? CommentCountLabel.Format(count) : "";
    public string ShareBadgeText => ShareCount is { } count ? CommentCountLabel.Format(count) : "";
    public bool HasFavoriteCount => FavoriteCount.HasValue;
    public bool HasShareCount => ShareCount.HasValue;
    public string FavoriteAutomationName => FavoriteCount is { } count
        ? $"喜欢 · {count:N0} 次{(IsFavorite ? "，已喜欢" : "")}"
        : IsFavorite ? "喜欢，已喜欢" : "喜欢";
    public string ShareAutomationName => ShareCount is { } count
        ? $"分享 · {count:N0} 次，复制链接" : "分享，复制链接";

    /// <summary>
    /// 喜欢 / 取消喜欢当前曲目。已喜欢时点按是<b>取消</b>，走删歌接口。
    /// </summary>
    /// <remarks>
    /// 计数只在写成功之后调整：<c>favorite</c> 是全站计数，这里按「它是红心数」的假设乐观 ±1，
    /// 该假设尚未实测，见 <c>docs/like-share.md</c>。
    /// </remarks>
    public async Task<LikedSongsOutcome> ToggleFavoriteAsync(CancellationToken cancellationToken = default)
    {
        if (_likedSongs is null) return LikedSongsOutcome.Failed;
        if (_track?.Id is not { } musicId || musicId <= 0) return LikedSongsOutcome.Failed;
        if (IsFavoriteBusy) return LikedSongsOutcome.AlreadyPending;

        IsFavoriteBusy = true;
        try
        {
            var desired = !IsFavorite;
            var outcome = await _likedSongs.SetLikedAsync(musicId, desired, cancellationToken).ConfigureAwait(true);
            if (outcome != LikedSongsOutcome.Succeeded) return outcome;
            // 期间切了歌就别往新曲目上写状态。
            if (_track?.Id != musicId) return outcome;

            IsFavorite = desired;
            if (FavoriteCount is { } count)
            {
                FavoriteCount = Math.Max(0, count + (desired ? 1 : -1));
            }

            return outcome;
        }
        finally
        {
            IsFavoriteBusy = false;
        }
    }

    /// <summary>优先显示曲目已带的数量，缺项时补读详情；同一首歌复用完成或进行中的请求。</summary>
    public Task LoadAsync(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_track?.Id == track.Id && (_request is not null || HasCompleteCounts(_track)))
        { return _details ?? Task.CompletedTask; }
        _generation++;
        _request?.Cancel();
        _request = null;
        _track = Normalize(track);
        ApplyCounts(_track);
        StartFavoriteLookup(_track.Id);
        if (track.Id <= 0 || HasCompleteCounts(_track))
        {
            _details = Task.FromResult<Track?>(_track);
            return _details;
        }
        var request = new CancellationTokenSource();
        _request = request;
        _details = FetchAsync(_track, request, _generation);
        return _details;
    }

    /// <summary>评论角标复用当前曲目的详情；等待者取消不会取消播放器共享的请求。</summary>
    public Task<Track?> GetDetailsAsync(long musicId, CancellationToken cancellationToken = default)
    {
        if (_track?.Id == musicId && _details is { } details)
        { return details.WaitAsync(cancellationToken); }
        return _api.GetTrackAsync(musicId, cancellationToken);
    }

    private async Task<Track?> FetchAsync(Track source, CancellationTokenSource request, int generation)
    {
        try
        {
            var detail = await _api.GetTrackAsync(source.Id, request.Token).ConfigureAwait(true);
            if (generation != _generation || request.IsCancellationRequested || detail?.Id != source.Id)
            { return source; }
            var updated = Normalize(source with
            {
                FavoriteCount = detail.FavoriteCount ?? source.FavoriteCount,
                ShareCount = detail.ShareCount ?? source.ShareCount,
                CommentCount = detail.CommentCount ?? source.CommentCount,
            });
            _track = updated;
            ApplyCounts(updated);
            return updated;
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { return source; }
        catch (Exception ex)
        {
            if (generation == _generation)
            { _logger.LogDebug(ex, "歌曲 {MusicId} 统计数量获取失败", source.Id); }
            return source;
        }
        finally
        {
            if (ReferenceEquals(_request, request)) { _request = null; }
            request.Dispose();
        }
    }

    /// <summary>
    /// 切歌后按已知集合判定喜欢状态。集合需要同步时由服务在后台拉一次，
    /// 这里不等它 —— 状态没回来之前先按「未喜欢」显示，回来再翻。
    /// </summary>
    private void StartFavoriteLookup(long musicId)
    {
        IsFavorite = false;
        if (_likedSongs is null || musicId <= 0) return;

        var generation = _generation;
        _ = UpdateFavoriteAsync(musicId, generation);
    }

    private async Task UpdateFavoriteAsync(long musicId, int generation)
    {
        try
        {
            var liked = await _likedSongs!.IsLikedAsync(musicId).ConfigureAwait(true);
            // 迟到的结果不能覆盖已经切走的曲目。
            if (generation != _generation || _track?.Id != musicId) return;
            IsFavorite = liked == true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "歌曲 {MusicId} 喜欢状态获取失败", musicId);
        }
        catch (OperationCanceledException)
        {
            // 后台预取被取消，不影响界面状态。
        }
    }

    /// <summary>
    /// 分享上报：把当前曲目的分享数 +1。
    /// </summary>
    /// <remarks>
    /// <b>这是一个写操作</b>（文档 2.8 实测每调一次 <c>share</c> 就 +1），
    /// 响应里的文案本项目用不到。任何异常都收敛成 <see cref="ShareOutcome.Failed"/>，
    /// 不能让分享流程因为没有计数而失败。
    /// </remarks>
    public async Task<ShareOutcome> ReportShareAsync(CancellationToken cancellationToken = default)
    {
        if (_track?.Id is not { } musicId || musicId <= 0) return ShareOutcome.Failed;

        try
        {
            var outcome = await _api.ReportTrackShareAsync(musicId, cancellationToken).ConfigureAwait(true);
            if (outcome == ShareOutcome.Succeeded && _track?.Id == musicId && ShareCount is { } count)
            {
                ShareCount = count + 1;
            }

            return outcome;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "歌曲 {MusicId} 分享上报失败", musicId);
            return ShareOutcome.Failed;
        }
    }

    private static bool HasCompleteCounts(Track track) =>
        track.FavoriteCount.HasValue && track.ShareCount.HasValue && track.CommentCount.HasValue;

    private static Track Normalize(Track track) => track with
    {
        FavoriteCount = track.FavoriteCount is { } favorites ? Math.Max(0, favorites) : null,
        ShareCount = track.ShareCount is { } shares ? Math.Max(0, shares) : null,
        CommentCount = track.CommentCount is { } comments ? Math.Max(0, comments) : null,
    };

    private void ApplyCounts(Track track)
    {
        FavoriteCount = track.FavoriteCount;
        ShareCount = track.ShareCount;
    }

    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        _generation++;
        _request?.Cancel();
        _request = null;
    }
}
