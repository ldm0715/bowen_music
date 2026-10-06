using Bodian.Core.Models;
using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Views;

/// <summary>
/// 「收藏的专辑」页。侧栏的一个根页。
/// </summary>
/// <remarks>
/// 数据源是移动端的收藏歌单端点（<c>service/collect/4/list</c>），
/// 这一点已由用户实测确认。页面保留「重新加载」按钮 ——
/// 拿不到数据时用户至少能自己重试一次，而不是只能看着一句说明。
/// </remarks>
public sealed partial class CollectedAlbumsPage : Page, INavigationAware
{
    private readonly Func<Album, AlbumDetailPage> _albumDetailFactory;
    private readonly INavigationService _navigation;

    public CollectedAlbumsPage(
        CollectedAlbumsViewModel viewModel,
        INavigationService navigation,
        Func<Album, AlbumDetailPage> albumDetailFactory)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(albumDetailFactory);

        ViewModel = viewModel;
        _navigation = navigation;
        _albumDetailFactory = albumDetailFactory;

        InitializeComponent();
    }

    public CollectedAlbumsViewModel ViewModel { get; }

    public void OnNavigatedTo() => _ = ViewModel.EnsureLoadedAsync();

    /// <summary>不需要收尾：列表是懒加载的，没有常驻订阅要摘。</summary>
    public void OnNavigatedFrom()
    {
    }

    /// <summary>点专辑行 → 专辑详情。压栈，侧栏继续高亮「收藏的专辑」。</summary>
    private void OnAlbumInvoked(object? sender, Album album) =>
        _navigation.Navigate(_albumDetailFactory(album));

    /// <summary>
    /// 卡片网格里的点击。与列表行是同一件事，只是事件参数形状不同，转发给上面那个。
    /// </summary>
    private void OnAlbumClick(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is Album album)
        {
            OnAlbumInvoked(sender, album);
        }
    }
}
