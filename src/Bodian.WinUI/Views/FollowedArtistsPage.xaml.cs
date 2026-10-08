using Bodian.Core.Models;
using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Views;

/// <summary>
/// 「关注的歌手」页。侧栏「我的音乐」组下的一个根页。
/// </summary>
/// <remarks>
/// 数据来自 <c>service/collect/7/list</c>，一次全量、没有分页，见
/// <see cref="FollowedArtistsViewModel"/> 与 <c>docs/collect-follow.md</c>。
/// </remarks>
public sealed partial class FollowedArtistsPage : Page, INavigationAware, IAccountScopedView
{
    private readonly Func<Artist, ArtistDetailPage> _artistFactory;
    private readonly INavigationService _navigation;

    /// <summary>
    /// 本页刚打开过歌手详情。详情页里能取消关注，所以回来时要重拉一次列表。
    /// </summary>
    /// <remarks>
    /// 根页实例被 <see cref="NavigationService"/> 缓存（同一个页面实例反复显示），
    /// 所以这个标志在「压栈进详情 → 返回」这条路上一定还在。
    /// </remarks>
    private bool _detailOpened;

    public FollowedArtistsPage(
        FollowedArtistsViewModel viewModel,
        INavigationService navigation,
        Func<Artist, ArtistDetailPage> artistFactory)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(artistFactory);

        ViewModel = viewModel;
        _navigation = navigation;
        _artistFactory = artistFactory;

        InitializeComponent();
    }

    public FollowedArtistsViewModel ViewModel { get; }

    public void OnNavigatedTo()
    {
        if (_detailOpened)
        {
            _detailOpened = false;
            _ = ViewModel.RefreshAsync();
            return;
        }

        _ = ViewModel.EnsureLoadedAsync();
    }

    /// <summary>换账号了：这一页是上一个账号关注的歌手。</summary>
    public Task OnAccountSwitchedAsync() => ViewModel.RefreshAsync();

    /// <summary>不需要收尾：列表是懒加载的，没有常驻订阅要摘。</summary>
    public void OnNavigatedFrom()
    {
    }

    /// <summary>
    /// 行列表与卡片网格里的点击。两个控件的事件参数形状一样，共用这一个处理函数。
    /// </summary>
    private void OnArtistClick(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is not Artist artist)
        {
            return;
        }

        _detailOpened = true;
        _navigation.Navigate(_artistFactory(artist));
    }
}
