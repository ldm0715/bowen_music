using Bodian.Core.Api;
using Bodian.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.WinUI.ViewModels;

/// <summary>当前歌曲的只读统计，播放器与歌词页共享；详情请求可供评论角标复用。</summary>
public sealed partial class TrackStatisticsViewModel : ObservableObject, IDisposable
{
    private readonly IBodianApi _api;
    private readonly ILogger<TrackStatisticsViewModel> _logger;
    private CancellationTokenSource? _request;
    private Task<Track?>? _details;
    private Track? _track;
    private int _generation;
    private bool _disposed;

    public TrackStatisticsViewModel(IBodianApi api, ILogger<TrackStatisticsViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
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

    public string FavoriteBadgeText => FavoriteCount is { } count ? CommentCountLabel.Format(count) : "";
    public string ShareBadgeText => ShareCount is { } count ? CommentCountLabel.Format(count) : "";
    public bool HasFavoriteCount => FavoriteCount.HasValue;
    public bool HasShareCount => ShareCount.HasValue;
    public string FavoriteAutomationName => FavoriteCount is { } count
        ? $"收藏 · {count:N0} 次（暂未开放）" : "收藏（暂未开放）";
    public string ShareAutomationName => ShareCount is { } count
        ? $"分享 · {count:N0} 次（暂未开放）" : "分享（暂未开放）";

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
