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
    private readonly ILogger _logger;

    /// <summary>播放入口。子类要自己排队时用它（「播放全部」）。</summary>
    protected readonly PlaybackCoordinator Coordinator;

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
        Coordinator = coordinator;
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
    [NotifyPropertyChangedFor(nameof(CountText))]
    public partial bool LoadFailed { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    public partial string StatusText { get; set; } = "";

    /// <summary>
    /// 工具栏左边那行。默认就是状态文案（空列表说明 / 「加载失败：…」/「共 N 首」）。
    /// </summary>
    /// <remarks>
    /// <b>歌单详情会覆盖它</b>：那一页有服务端的曲目总数，比已加载条数准，详见
    /// <c>PlaylistDetailViewModel.CountText</c>。「我喜欢的」没有这个数，用默认实现。
    /// </remarks>
    public virtual string CountText => StatusText;

    /// <summary>已经取完，且列表非空。页脚据此显示「没有更多了哦~」。</summary>
    public bool ShowEnd => Tracks.Count > 0 && !HasMore && !IsBusy && !LoadFailed;

    /// <summary>翻页失败，且确实还有下一页可拉。页脚据此显示「重试」。</summary>
    public bool ShowRetry => LoadFailed && HasMore && !IsBusy;

    [ObservableProperty]
    public partial Track? CurrentTrack { get; set; }

    /// <summary>
    /// 当前歌单的服务端 id；还没解析出来（或歌单不存在）时是 <c>0</c>。
    /// </summary>
    /// <remarks>
    /// 工具栏的「批量移出」要用它 —— 值为 0 时那颗按钮整个收起来，不会出现「点了没反应」。
    /// </remarks>
    public long PlaylistId => _playlistId ?? 0;

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
                OnPropertyChanged(nameof(PlaylistId));
                StatusText = MissingText;
                return;
            }

            _playlistId = playlist.Id;
            OnPropertyChanged(nameof(PlaylistId));
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
    /// 一页一页地把剩下的全部拉完，最多拉 <paramref name="maxPages"/> 页。
    /// </summary>
    /// <returns>
    /// 是否真的拉到了「没有下一页」。<b>撞上限、中途失败、以及进来时就在忙，都返回 <c>false</c>。</b>
    /// </returns>
    /// <remarks>
    /// <para>
    /// 「播放全部」要用它 —— 与 <c>PagedList&lt;T&gt;.LoadAllAsync</c> 同一份语义，改一处时另一处也要看。
    /// 只把当前已加载的那些排进队列的话，177 首的歌单第一次点只播得到首屏那 30 首。
    /// </para>
    /// <para>
    /// 中途失败就停，并保留已加载的部分 —— 队列短一点也比整次操作失败好。
    /// </para>
    /// <para>
    /// <b>进来时就有加载在进行则直接返回 <c>false</c></b>：<see cref="LoadMoreAsync"/>
    /// 在忙时会早退，硬循环只会空转到上限。
    /// </para>
    /// <para>
    /// <b>不改成 <c>PagedList&lt;T&gt;</c></b>：那一族的取数委托是构造时固定的，
    /// 而这里的 <see cref="ResolvePlaylistAsync"/> 要先回答「是哪个歌单」，搬过去要把基类拆了重做，
    /// 收益只是少这一个方法。
    /// </para>
    /// </remarks>
    public async Task<bool> LoadAllAsync(int maxPages = 20, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPages);

        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(true);

        if (IsBusy)
        {
            return false;
        }

        var pages = 0;

        while (HasMore && !LoadFailed && !IsBusy && pages < maxPages)
        {
            pages++;
            await LoadMoreAsync(cancellationToken).ConfigureAwait(true);
        }

        // ★ 失败也要算「没拉全」：首屏就失败时 HasMore 停在 false（ReloadAsync 开头会把它清掉），
        //   只看 !HasMore 会把「什么都没拉到」当成「拉完了」。
        return !HasMore && !LoadFailed;
    }

    /// <summary>
    /// 点播某一行。
    /// </summary>
    /// <remarks>
    /// <b>只把这一首排进队列并立即播放它</b>：点一首歌不该把整个列表拖进队列。
    /// 要一次排进整个列表，走工具栏的「全部加入播放列表」（追加，不打断正在播的），
    /// 或者页头的「播放全部」（整表替换并从头播）。
    /// </remarks>
    [RelayCommand]
    private async Task PlayAsync(Track? track)
    {
        if (track is null)
        {
            return;
        }

        CurrentTrack = track;

        await Coordinator.EnqueueAndPlayAsync(track).ConfigureAwait(true);
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
        // ★ 这里手写「共 N 首」，不用 Formats.TrackCount：本文件被链进
        //   tests\Bodian.Core.Tests 离屏编译，而 Formats.cs 依赖 WinUI 的 Visibility，链不进去。
        //   改文案时这一处要和 Formats.TrackCount 一起改。
        StatusText = Tracks.Count == 0 ? EmptyText : $"共 {Tracks.Count} 首";
    }
}
