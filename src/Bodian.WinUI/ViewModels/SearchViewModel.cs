using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Bodian.Core.Api;
using Bodian.Core.Api.Paging;
using Bodian.Core.Models;
using Bodian.Core.Services.Abstractions;
using Bodian.WinUI.Playback;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.WinUI.ViewModels;

// 单例：顶部输入框与搜索页共享状态；分页使用已提交的词，避免输入新词时串页。
public sealed partial class SearchViewModel : ObservableObject
{
    private readonly IBodianApi _api;
    private readonly PlaybackCoordinator _coordinator;
    private readonly ILogger<SearchViewModel> _logger;
    private readonly ISearchHistoryStore _historyStore;
    private bool _resettingCategory;
    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _suggestionCancellation;
    private PagedCursor? _cursor;
    private string _searchedKeyword = "";
    private bool _hotWordsLoading;

    public SearchViewModel(IBodianApi api, PlaybackCoordinator coordinator,
        ISearchHistoryStore historyStore, ViewModeService viewMode,
        ILogger<SearchViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(historyStore);
        ArgumentNullException.ThrowIfNull(viewMode);
        _historyStore = historyStore;
        ViewMode = viewMode;
        foreach (var keyword in historyStore.Load()) SearchHistory.Add(keyword);
        HistoryIsEmpty = SearchHistory.Count == 0;
        _api = api;
        _coordinator = coordinator;
        _logger = logger ?? NullLogger<SearchViewModel>.Instance;

        Results.CollectionChanged += (_, _) => OnPropertyChanged(nameof(TrackCountText));

        // 工具栏左侧那行计数跟着「已加载多少条」走。单曲页签用的 Results 在上面那行挂了，
        // 这里补另外三个页签的集合 —— 钩子内容一样，不抽公共方法是因为它只有一行。
        void RecountResultToolbar(object? sender, NotifyCollectionChangedEventArgs args)
            => OnPropertyChanged(nameof(ResultCountText));

        Playlists.CollectionChanged += RecountResultToolbar;
        Albums.CollectionChanged += RecountResultToolbar;
        Artists.CollectionChanged += RecountResultToolbar;
    }

    /// <summary>
    /// 歌曲页签工具栏左侧那行。
    /// </summary>
    /// <remarks>
    /// <b>与页头的 <see cref="StatusText"/> 分工</b>：页头那行说的是「搜了什么、搜到没有」，
    /// 综合页签也在用它（综合页签没有工具栏，所以那行不能挪走）；这里只说「有几首」。
    /// 两边都不报条数，就不会同屏出现两个数字。
    /// </remarks>
    public string TrackCountText => Formats.TrackCount(Results.Count);

    /// <summary>
    /// 「歌单 / 专辑 / 歌手」三个页签工具栏左侧那行。
    /// </summary>
    /// <remarks>
    /// <b>与页头的 <see cref="StatusText"/> 分工</b>：那行不再报任何页签的条数
    /// （<c>AppendNextPageAsync</c> 里的理由），这里只说「有几个」。两边都不报，就不会同屏出现两个数字。
    /// </remarks>
    public string ResultCountText => Formats.ResultCount(SelectedCategoryCount, SelectedCategoryUnit);

    /// <summary>计数单位随页签走 —— 工具栏只有一条，换页签换的是单位，不是再摆一条控件。</summary>
    private string SelectedCategoryUnit => SelectedCategory switch
    {
        2 => "个歌单",
        3 => "张专辑",
        4 => "位歌手",
        _ => "",
    };

    /// <summary>这三个页签上方那条工具栏要不要显示。「综合」与「单曲」各有各的排法，不用它。</summary>
    public bool ShowsResultToolbar => SelectedCategory is 2 or 3 or 4;

    /// <summary>
    /// 行列表还是封面卡片。**状态不在这里** —— 见 <see cref="ViewModeService"/>：
    /// 那是个单例，收藏的两页也在用同一个开关（`view-mode.json`），本页只是把它透给 XAML。
    /// </summary>
    public ViewModeService ViewMode { get; }

    public ObservableCollection<SearchResultSection> OverviewSections { get; } = [];
    public ObservableCollection<object> OverviewItems { get; } = [];
    public ObservableCollection<Track> Results { get; } = [];
    public ObservableCollection<Playlist> Playlists { get; } = [];
    public ObservableCollection<Album> Albums { get; } = [];
    public ObservableCollection<Artist> Artists { get; } = [];
    public ObservableCollection<SearchHotWord> HotWords { get; } = [];
    public ObservableCollection<string> SearchHistory { get; } = [];

    // 仅由显式启动的本机性能场景使用，不请求搜索 API，也不写搜索历史。
    internal void PopulatePerformanceSample(int count)
    {
        _searchCancellation?.Cancel();
        _resettingCategory = true;
        try
        {
            Keyword = "列表性能样本";
            SelectedCategory = 1;
            HasSearch = true;
            HasMore = false;
            Results.Clear();
            for (var i = 0; i < count; i++)
                Results.Add(new Track
                {
                    Id = -(i + 1L), Title = $"性能样本 {i + 1} · 一首较长的曲目名称",
                    ArtistText = "歌手 A / 歌手 B", AlbumName = "性能验证专辑",
                    Duration = TimeSpan.FromSeconds(180 + i % 120),
                    AvailableQualities = [AudioQuality.Lossless, AudioQuality.High, AudioQuality.Standard],
                    RequiresVip = i % 3 == 0,
                });
            StatusText = $"本机性能场景：{count:N0} 首，无网络搜索请求";
        }
        finally { _resettingCategory = false; }
    }

    [ObservableProperty] public partial string Keyword { get; set; } = "";

    /// <summary>页签名。顺序与下标的含义绑死在 <see cref="SelectedCategory"/> 上。</summary>
    private static readonly string[] CategoryHeaders = ["综合", "单曲", "歌单", "专辑", "歌手"];

    public IReadOnlyList<string> Categories => CategoryHeaders;

    /// <summary>当前页签。<c>0</c> 是「综合」，它一次性取全，不分页。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverviewTab))]
    [NotifyPropertyChangedFor(nameof(IsTrackTab))]
    [NotifyPropertyChangedFor(nameof(IsPlaylistTab))]
    [NotifyPropertyChangedFor(nameof(IsAlbumTab))]
    [NotifyPropertyChangedFor(nameof(IsArtistTab))]
    [NotifyPropertyChangedFor(nameof(ShowsResultToolbar))]
    [NotifyPropertyChangedFor(nameof(ResultCountText))]
    public partial int SelectedCategory { get; set; }

    // 五个页签的内容都留在可视树上，只切可见性。
    //
    // ★ 这样做不会让未选中的列表跟着翻页：Collapsed 的元素不进布局，容器不会被实现，
    //   而自动翻页正是挂在 ContainerContentChanging 上的（见 docs/list-paging.md）。
    //   附带的好处是来回切页签时滚动位置不再丢 —— 原来的 Pivot 会整个卸载内容。
    public bool IsOverviewTab => SelectedCategory == 0;

    public bool IsTrackTab => SelectedCategory == 1;

    public bool IsPlaylistTab => SelectedCategory == 2;

    public bool IsAlbumTab => SelectedCategory == 3;

    public bool IsArtistTab => SelectedCategory == 4;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEnd))]
    [NotifyPropertyChangedFor(nameof(ShowRetry))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEnd))]
    [NotifyPropertyChangedFor(nameof(ShowRetry))]
    public partial bool HasMore { get; set; }

    /// <summary>上一次加载下一页失败了。页脚据此让出「重试」入口。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEnd))]
    [NotifyPropertyChangedFor(nameof(ShowRetry))]
    public partial bool LoadFailed { get; set; }

    [ObservableProperty] public partial bool HasSearch { get; set; }
    [ObservableProperty] public partial bool HistoryIsEmpty { get; set; }
    [ObservableProperty] public partial string StatusText { get; set; } = "输入关键词，或选择热搜词";
    [ObservableProperty] public partial string HotWordsStatus { get; set; } = "搜索热榜";
    [ObservableProperty] public partial IReadOnlyList<string> Suggestions { get; set; } = [];
    [ObservableProperty] public partial Track? CurrentTrack { get; set; }

    /// <summary>
    /// 当前页签的条数。各页签是各自独立的集合，结束文案要看当前那个。
    /// </summary>
    /// <remarks>「综合」不分页，所以恒为 0 —— 它的结束文案不显示。</remarks>
    private int SelectedCategoryCount => SelectedCategory switch
    {
        1 => Results.Count,
        2 => Playlists.Count,
        3 => Albums.Count,
        4 => Artists.Count,
        _ => 0,
    };

    /// <summary>已经取完，且当前页签非空。页脚据此显示「没有更多了哦~」。</summary>
    public bool ShowEnd =>
        HasSearch && !IsBusy && !HasMore && !LoadFailed && SelectedCategoryCount > 0;

    /// <summary>翻页失败，且确实还有下一页可拉。页脚据此显示「重试」。</summary>
    public bool ShowRetry => LoadFailed && HasMore && !IsBusy;

    partial void OnSelectedCategoryChanged(int value)
    {
        OnPropertyChanged(nameof(ShowEnd));
        OnPropertyChanged(nameof(ShowRetry));

        if (HasSearch && !_resettingCategory && value >= 0) _ = SearchCoreAsync(_searchedKeyword);
    }

    public async Task EnsureHotWordsAsync()
    {
        if (_hotWordsLoading || HotWords.Count > 0) return;
        _hotWordsLoading = true;
        HotWordsStatus = "正在加载搜索热榜…";
        try
        {
            var words = await _api.GetSearchHotWordsAsync();
            foreach (var word in words) HotWords.Add(word);
            HotWordsStatus = words.Count == 0 ? "暂无热搜词" : "搜索热榜";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "搜索热榜加载失败");
            HotWordsStatus = "搜索热榜加载失败，点击重试";
        }
        finally { _hotWordsLoading = false; }
    }

    public void ClearSuggestions()
    {
        _suggestionCancellation?.Cancel();
        Suggestions = [];
    }

    public async Task UpdateSuggestionsAsync(string text)
    {
        _suggestionCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource();
        _suggestionCancellation = cancellation;
        Suggestions = [];
        try
        {
            var keyword = text.Trim();
            if (keyword.Length == 0) return;
            await Task.Delay(250, cancellation.Token);
            var words = await _api.GetSearchSuggestionsAsync(keyword, cancellation.Token);
            if (!cancellation.IsCancellationRequested) Suggestions = words;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) { _logger.LogDebug(ex, "联想词加载失败"); }
        finally
        {
            if (ReferenceEquals(_suggestionCancellation, cancellation)) _suggestionCancellation = null;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task SearchAsync()
    {
        var keyword = Keyword.Trim();
        if (keyword.Length == 0) return Task.CompletedTask;
        RememberSearch(keyword);
        _resettingCategory = true;
        try { SelectedCategory = 0; }
        finally { _resettingCategory = false; }
        return SearchCoreAsync(keyword);
    }

    [RelayCommand]
    private void OpenCategory(SearchResultCategory category) => SelectedCategory = (int)category;

    private void RememberSearch(string keyword)
    {
        for (var i = SearchHistory.Count - 1; i >= 0; i--)
        {
            if (string.Equals(SearchHistory[i], keyword, StringComparison.OrdinalIgnoreCase)) SearchHistory.RemoveAt(i);
        }
        SearchHistory.Insert(0, keyword);
        while (SearchHistory.Count > 20) SearchHistory.RemoveAt(SearchHistory.Count - 1);
        HistoryIsEmpty = false;
        _ = _historyStore.SaveAsync(SearchHistory.ToArray());
    }

    [RelayCommand]
    private Task ClearSearchHistoryAsync()
    {
        SearchHistory.Clear();
        HistoryIsEmpty = true;
        return _historyStore.SaveAsync([]);
    }

    private async Task SearchCoreAsync(string keyword)
    {
        _searchCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource();
        _searchCancellation = cancellation;
        ClearSuggestions();
        OverviewSections.Clear();
        OverviewItems.Clear();
        Results.Clear(); Playlists.Clear(); Albums.Clear(); Artists.Clear();
        HasMore = false;
        LoadFailed = false;
        CurrentTrack = null;
        _searchedKeyword = keyword;
        HasSearch = keyword.Length > 0;
        var category = SelectedCategory;
        IsBusy = HasSearch;
        var cursor = new PagedCursor(PagingConvention.ZeroBased);
        _cursor = cursor;
        try
        {
            if (!HasSearch)
            {
                StatusText = "输入关键词，或选择热搜词";
                await EnsureHotWordsAsync();
                return;
            }
            StatusText = "搜索中…";
            if (category == 0)
            {
                var sections = await _api.SearchComprehensiveAsync(keyword, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                foreach (var section in sections)
                {
                    OverviewSections.Add(section);
                    OverviewItems.Add(new ListSectionHeader { Title = section.Title, Category = section.Category });
                    for (var i = 0; i < section.Tracks.Count; i++)
                        OverviewItems.Add(new TrackRow { Source = section.Tracks[i], Ordinal = i + 1 });
                    foreach (var item in section.Playlists) OverviewItems.Add(item);
                    foreach (var item in section.Albums) OverviewItems.Add(item);
                    foreach (var item in section.Artists) OverviewItems.Add(item);
                }
                StatusText = sections.Count == 0 ? "没有找到结果" : $"「{keyword}」的综合搜索结果";
            }
            else await AppendNextPageAsync(keyword, category, cursor, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!cancellation.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "搜索失败：{Keyword}", keyword);
                StatusText = $"搜索失败：{ex.Message}";
            }
        }
        finally
        {
            if (ReferenceEquals(_searchCancellation, cancellation))
            {
                IsBusy = false;
                _searchCancellation = null;
            }
        }
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (SelectedCategory == 0 || _cursor is null || IsBusy || !HasMore) return;
        using var cancellation = new CancellationTokenSource();
        _searchCancellation = cancellation;
        IsBusy = true;
        LoadFailed = false;
        try { await AppendNextPageAsync(_searchedKeyword, SelectedCategory, _cursor, cancellation.Token); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!cancellation.IsCancellationRequested)
            {
                StatusText = $"加载失败：{ex.Message}";
                LoadFailed = true;
            }

            _logger.LogWarning(ex, "加载搜索下一页失败");
        }
        finally
        {
            if (ReferenceEquals(_searchCancellation, cancellation))
            {
                IsBusy = false;
                _searchCancellation = null;
            }
        }
    }

    /// <summary>
    /// 刷新：重跑一次**已经提交的**关键词。
    /// </summary>
    /// <remarks>
    /// <b>不能复用 <c>SearchCommand</c></b>：那个读的是输入框里的 <c>Keyword</c>，用户可能改了词还没提交，
    /// 而刷新应当重放上一次那次搜索 —— 拿半截输入去搜是另一回事。页签也保持不动。
    /// </remarks>
    [RelayCommand]
    private async Task ReloadAsync()
    {
        if (!HasSearch || _searchedKeyword.Length == 0)
        {
            return;
        }

        await SearchCoreAsync(_searchedKeyword);
    }

    [RelayCommand]
    private async Task PlayAsync(Track? track)
    {
        if (track is null) return;
        CurrentTrack = track;

        // 加到队尾并立即播放。综合页签与歌曲页签共用这一处，改的时候别只顾一个。
        await _coordinator.EnqueueAndPlayAsync(track);
    }

    private async Task AppendNextPageAsync(string keyword, int category, PagedCursor cursor, CancellationToken token)
    {
        var count = category switch
        {
            2 => await AppendAsync(Playlists, _api.SearchPlaylistsAsync(keyword, cursor, token), token),
            3 => await AppendAsync(Albums, _api.SearchAlbumsAsync(keyword, cursor, token), token),
            4 => await AppendAsync(Artists, _api.SearchArtistsAsync(keyword, cursor, token), token),
            _ => await AppendAsync(Results, _api.SearchAsync(keyword, cursor, token), token),
        };
        token.ThrowIfCancellationRequested();
        HasMore = !cursor.Exhausted;
        // 「（滚动加载）」已去掉 —— 理由同 PagedList 里那一处。
        // **条数一律不在这行报**：单曲页签报在 TrackCountText，歌单/专辑/歌手报在 ResultCountText，
        // 页头再报一次就是同屏两个数字。这行只说「搜的是什么、搜到没有」。
        // （综合页签也用它，而综合页签压根没有工具栏 —— 所以这行不能挪进工具栏。）
        StatusText = count == 0 ? "没有找到结果" : $"「{keyword}」的搜索结果";
    }

    private static async Task<int> AppendAsync<T>(ObservableCollection<T> collection, Task<PagedResult<T>> request, CancellationToken token)
    {
        var page = await request;
        token.ThrowIfCancellationRequested();
        foreach (var item in page.Items) collection.Add(item);
        return collection.Count;
    }
}
