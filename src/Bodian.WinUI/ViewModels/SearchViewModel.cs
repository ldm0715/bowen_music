using System.Collections.ObjectModel;
using Bodian.Core.Api;
using Bodian.Core.Api.Paging;
using Bodian.Core.Models;
using Bodian.WinUI.Playback;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 搜索页。
/// </summary>
/// <remarks>
/// 队列来源就是这里的结果列表：点第 N 行 → 整页入队并从第 N 首开始，
/// 所以「下一首」在页内有效。自动翻页拉取留到 P7。
/// </remarks>
public sealed partial class SearchViewModel : ObservableObject
{
    private readonly IBodianApi _api;
    private readonly IBodianLogin _login;
    private readonly PlaybackCoordinator _coordinator;
    private readonly ILogger<SearchViewModel> _logger;

    private PagedCursor? _cursor;

    public SearchViewModel(
        IBodianApi api,
        IBodianLogin login,
        PlaybackCoordinator coordinator,
        ILogger<SearchViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(login);
        ArgumentNullException.ThrowIfNull(coordinator);

        _api = api;
        _login = login;
        _coordinator = coordinator;
        _logger = logger ?? NullLogger<SearchViewModel>.Instance;
    }

    /// <summary>账号显示名。没有昵称就退回 uid。</summary>
    public string AccountText => _login.Nickname ?? _login.Account?.Uid ?? "已登录";

    /// <summary>账号头像。没有（或老凭据文件里没存）时为 <c>null</c>，界面显示占位。</summary>
    public ImageSource? Avatar
    {
        get
        {
            var uri = _login.Account?.Avatar;

            // BitmapImage 会自己异步加载；地址失效时图是空的，不影响布局。
            return uri is null ? null : new BitmapImage(uri);
        }
    }

    /// <summary>是否会员。**只用于展示**，播放权限一律以服务端 checkRight 为准。</summary>
    public bool IsVip => _login.Account?.IsVip == true;

    /// <summary>
    /// 退出登录。
    /// </summary>
    /// <remarks>
    /// 这里只管清会话，跳转由宿主负责 —— 登出会让 <c>IBodianLogin.AccountChanged</c> 触发，
    /// 主窗口收到后把页面切回登录页。不在这里重复导航，避免两处同时切页。
    /// </remarks>
    [RelayCommand]
    private void SignOut() => _login.SignOut();

    public ObservableCollection<Track> Results { get; } = [];

    [ObservableProperty]
    public partial string Keyword { get; set; } = "";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool HasMore { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "输入关键词开始搜索";

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
            : $"找到 {Results.Count} 首{(HasMore ? "（还有更多）" : "")}";
    }
}
