using System.Collections.ObjectModel;
using Bodian.Core.Api;
using Bodian.Core.Api.Paging;
using Bodian.Core.Models;
using Bodian.WinUI.Playback;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 「一个歌单的曲目列表」这一类页面的共同部分。
/// </summary>
/// <remarks>
/// <para>
/// 子类只负责回答一件事：<b>要展示哪个歌单</b>。分页、翻页、点播、状态文案都在这里 ——
/// 「我喜欢的」与「自建歌单详情」除了歌单来源不同，其余是同一套流程。
/// </para>
/// <para>
/// <b>首次进入才拉，切回来不重拉。</b> 翻页游标是有状态的，重新进入就重置会让用户
/// 「翻到第 5 页 → 去听一首 → 回来又只剩第一页」。
/// </para>
/// </remarks>
public abstract partial class PlaylistTracksViewModel : ObservableObject
{
    private readonly IBodianApi _api;
    private readonly PlaybackCoordinator _coordinator;
    private readonly ILogger _logger;

    private PagedCursor? _cursor;
    private long? _playlistId;
    private bool _loaded;

    protected PlaylistTracksViewModel(
        IBodianApi api,
        PlaybackCoordinator coordinator,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(logger);

        _api = api;
        _coordinator = coordinator;
        _logger = logger;
    }

    public ObservableCollection<Track> Tracks { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEnd))]
    [NotifyPropertyChangedFor(nameof(ShowRetry))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEnd))]
    [NotifyPropertyChangedFor(nameof(ShowRetry))]
    public partial bool HasMore { get; set; }

    /// <summary>上一次加载失败了。页脚据此让出「重试」入口。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEnd))]
    [NotifyPropertyChangedFor(nameof(ShowRetry))]
    public partial bool LoadFailed { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    /// <summary>已经取完，且列表非空。页脚据此显示「没有更多了哦~」。</summary>
    public bool ShowEnd => Tracks.Count > 0 && !HasMore && !IsBusy && !LoadFailed;

    /// <summary>翻页失败，且确实还有下一页可拉。页脚据此显示「重试」。</summary>
    public bool ShowRetry => LoadFailed && HasMore && !IsBusy;

    [ObservableProperty]
    public partial Track? CurrentTrack { get; set; }

    /// <summary>页头显示的歌单名。拿不到歌单时是子类给的兜底名字。</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = "";

    /// <summary>确定要展示哪个歌单。返回 <c>null</c> 表示这个账号/这个 id 没有歌单。</summary>
    protected abstract Task<Playlist?> ResolvePlaylistAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 取曲目时填进 <c>source</c> 的值。
    /// </summary>
    /// <remarks>
    /// 默认 <c>5</c>（账号歌单：自建与「我喜欢」）。发现页里点进来的**公开歌单不是 5**，
    /// 那种情况由子类覆盖它 —— <b>填错的表现是曲目列表直接是空的</b>，
    /// 因为服务端对不上的 source 只回空、不报错。
    /// </remarks>
    protected virtual int PlaylistSource => 5;

    /// <summary>歌单不存在时的说明文案。</summary>
    protected abstract string MissingText { get; }

    /// <summary>歌单存在但没有可播曲目时的说明文案。</summary>
    protected abstract string EmptyText { get; }

    /// <summary>首次进入时调。<b>已经加载过就什么都不做。</b></summary>
    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await ReloadAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>重新拉第一页。</summary>
    [RelayCommand]
    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        HasMore = false;
        LoadFailed = false;
        CurrentTrack = null;
        Tracks.Clear();
        _cursor = new PagedCursor(PagingConvention.OneBased);

        try
        {
            var playlist = await ResolvePlaylistAsync(cancellationToken).ConfigureAwait(true);

            if (playlist is null)
            {
                _playlistId = null;
                StatusText = MissingText;
                return;
            }

            _playlistId = playlist.Id;
            Title = playlist.Name;

            await AppendNextPageAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载歌单曲目失败");
            StatusText = $"加载失败：{ex.Message}";
            LoadFailed = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LoadMoreAsync(CancellationToken cancellationToken)
    {
        if (IsBusy || _cursor is null || !HasMore)
        {
            return;
        }

        IsBusy = true;
        LoadFailed = false;

        try
        {
            await AppendNextPageAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载下一页失败");
            StatusText = $"加载失败：{ex.Message}";
            LoadFailed = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 点播某一行。
    /// </summary>
    /// <remarks>
    /// 队列就是这个列表本身：点第 N 行 → 整页入队并从第 N 首开始，所以「下一首」在页内有效。
    /// </remarks>
    [RelayCommand]
    private async Task PlayAsync(Track? track)
    {
        if (track is null)
        {
            return;
        }

        var index = Tracks.IndexOf(track);

        if (index < 0)
        {
            return;
        }

        CurrentTrack = track;

        await _coordinator.PlayFromAsync([.. Tracks], index).ConfigureAwait(true);
    }

    private async Task AppendNextPageAsync(CancellationToken cancellationToken)
    {
        if (_cursor is null || _playlistId is not { } playlistId)
        {
            return;
        }

        var page = await _api.GetPlaylistTracksAsync(playlistId, PlaylistSource, _cursor, cancellationToken)
            .ConfigureAwait(true);

        foreach (var track in page.Items)
        {
            Tracks.Add(track);
        }

        // 判断「还有没有下一页」只能看游标，不能信响应里的 total ——
        // 服务端会省略不可用曲目，实测歌单标称 121 首时第一页只回 99 首。
        HasMore = !_cursor.Exhausted && page.Items.Count > 0;

        // 只说条数，「（滚动加载）」已去掉 —— 理由同 PagedList 里那一处。
        StatusText = Tracks.Count == 0 ? EmptyText : $"{Tracks.Count} 首";
    }
}
