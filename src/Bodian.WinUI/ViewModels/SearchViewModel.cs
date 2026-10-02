using System.Collections.ObjectModel;
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
        ISearchHistoryStore historyStore, ILogger<SearchViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(historyStore);
        _historyStore = historyStore;
        foreach (var keyword in historyStore.Load()) SearchHistory.Add(keyword);
        HistoryIsEmpty = SearchHistory.Count == 0;
        _api = api;
        _coordinator = coordinator;
        _logger = logger ?? NullLogger<SearchViewModel>.Instance;
    }

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
    [ObservableProperty] public partial int SelectedCategory { get; set; }
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial bool HasMore { get; set; }
    [ObservableProperty] public partial bool HasSearch { get; set; }
    [ObservableProperty] public partial bool HistoryIsEmpty { get; set; }
    [ObservableProperty] public partial string StatusText { get; set; } = "输入关键词，或选择热搜词";
    [ObservableProperty] public partial string HotWordsStatus { get; set; } = "搜索热榜";
    [ObservableProperty] public partial IReadOnlyList<string> Suggestions { get; set; } = [];
    [ObservableProperty] public partial Track? CurrentTrack { get; set; }

    partial void OnSelectedCategoryChanged(int value)
    {
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
        try { await AppendNextPageAsync(_searchedKeyword, SelectedCategory, _cursor, cancellation.Token); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!cancellation.IsCancellationRequested) StatusText = $"加载失败：{ex.Message}";
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

    [RelayCommand]
    private async Task PlayAsync(Track? track)
    {
        if (track is null) return;
        var queue = SelectedCategory == 0
            ? OverviewSections.SelectMany(section => section.Tracks).ToArray()
            : Results.ToArray();
        var index = Array.IndexOf(queue, track);
        if (index < 0) return;
        CurrentTrack = track;
        await _coordinator.PlayFromAsync(queue, index);
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
        StatusText = count == 0 ? "没有找到结果" : $"「{keyword}」已加载 {count} 条{(HasMore ? "（还有更多）" : "")}";
    }

    private static async Task<int> AppendAsync<T>(ObservableCollection<T> collection, Task<PagedResult<T>> request, CancellationToken token)
    {
        var page = await request;
        token.ThrowIfCancellationRequested();
        foreach (var item in page.Items) collection.Add(item);
        return collection.Count;
    }
}
