using System.Collections.ObjectModel;
using Bodian.Core.Api;
using Bodian.Core.Api.Paging;
using Bodian.Core.Models;
using Bodian.WinUI.Playback;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 搜索页。
/// </summary>
/// <remarks>
/// <para>
/// 队列来源就是这里的结果列表：点第 N 行 → 整页入队并从第 N 首开始，
/// 所以「下一首」在页内有效。自动翻页拉取留到后续。
/// </para>
/// <para>
/// <b>它是单例</b>，因为它同时服务两个地方：内容区顶部那个常驻搜索框（关键词）与
/// 搜索页（结果）。两者必须是同一个实例，否则框里输了词、页上却没有结果。
/// 顺带的好处是搜索结果不会因为页面重建而丢。
/// </para>
/// <para>
/// <b>账号信息（昵称/头像/会员/退出登录）不在这里</b>，已经挪到侧栏底部的
/// <see cref="AccountViewModel"/> —— 那些信息在搜索页上只在搜索页可见，而侧栏是常驻的。
/// </para>
/// </remarks>
public sealed partial class SearchViewModel : ObservableObject
{
    private readonly IBodianApi _api;
    private readonly PlaybackCoordinator _coordinator;
    private readonly ILogger<SearchViewModel> _logger;

    private PagedCursor? _cursor;

    public SearchViewModel(
        IBodianApi api,
        PlaybackCoordinator coordinator,
        ILogger<SearchViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(coordinator);

        _api = api;
        _coordinator = coordinator;
        _logger = logger ?? NullLogger<SearchViewModel>.Instance;
    }

    public ObservableCollection<Track> Results { get; } = [];

    [ObservableProperty]
    public partial string Keyword { get; set; } = "";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool HasMore { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "在顶部的搜索框里输入关键词";

    [ObservableProperty]
    public partial Track? CurrentTrack { get; set; }

    [RelayCommand]
    private async Task SearchAsync()
    {
        var keyword = Keyword.Trim();

        if (keyword.Length == 0)
        {
            return;
        }

        IsBusy = true;
        StatusText = "搜索中…";
        Results.Clear();
        HasMore = false;
        CurrentTrack = null;

        _cursor = new PagedCursor(PagingConvention.ZeroBased);

        try
        {
            await AppendNextPageAsync(keyword);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "搜索失败：{Keyword}", keyword);
            StatusText = $"搜索失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        var keyword = Keyword.Trim();

        if (keyword.Length == 0 || _cursor is null || IsBusy)
        {
            return;
        }

        IsBusy = true;

        try
        {
            await AppendNextPageAsync(keyword);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载下一页失败：{Keyword}", keyword);
            StatusText = $"加载失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task PlayAsync(Track? track)
    {
        if (track is null)
        {
            return;
        }

        var index = Results.IndexOf(track);

        if (index < 0)
        {
            return;
        }

        CurrentTrack = track;

        await _coordinator.PlayFromAsync([.. Results], index);
    }

    private async Task AppendNextPageAsync(string keyword)
    {
        if (_cursor is null)
        {
            return;
        }

        var page = await _api.SearchAsync(keyword, _cursor, CancellationToken.None);

        foreach (var track in page.Items)
        {
            Results.Add(track);
        }

        // 判断「还有没有下一页」只能看游标，不能信响应里的 total ——
        // 服务端会漏算不可用条目，实测出现过标称 121 首、第一页只回 99 首的情况。
        HasMore = !_cursor.Exhausted && page.Items.Count > 0;

        StatusText = Results.Count == 0
            ? "没有找到结果"
            : $"「{keyword}」找到 {Results.Count} 首{(HasMore ? "（还有更多）" : "")}";
    }
}
