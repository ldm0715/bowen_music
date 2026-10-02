using Bodian.Core.Models;
using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Views;

/// <summary>
/// 乐库的一个大类：子类 + 专辑列表。
/// </summary>
/// <remarks>
/// <b>压在栈上的详情页，不是根</b>：侧栏该继续高亮「乐库」。
/// </remarks>
public sealed partial class LibraryCategoryPage : Page
{
    private readonly INavigationService _navigation;
    private readonly Func<Album, AlbumDetailPage> _albumDetailFactory;

    public LibraryCategoryPage(
        LibraryCategoryViewModel viewModel,
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

        // 两个 RadioButton 的初始选中由代码设：XAML 里写 IsChecked 会在绑定生效前
        // 触发一次 Checked，白跑一次请求。
        CuratedRadio.IsChecked = ViewModel.SelectedSort == MusicLibSort.Curated;
        NewestRadio.IsChecked = ViewModel.SelectedSort == MusicLibSort.Newest;
    }

    public LibraryCategoryViewModel ViewModel { get; }

    /// <summary>切排序。选中的是新的那个才是真变化，重拉由 ViewModel 的 partial 方法负责。</summary>
    private void OnSortChanged(object sender, RoutedEventArgs e)
    {
        var sort = ReferenceEquals(sender, NewestRadio) ? MusicLibSort.Newest : MusicLibSort.Curated;

        if (ViewModel.SelectedSort != sort)
        {
            ViewModel.SelectedSort = sort;
        }
    }

    /// <summary>点专辑 → 专辑详情。压栈，侧栏继续高亮「乐库」。</summary>
    private void OnAlbumClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Album album)
        {
            _navigation.Navigate(_albumDetailFactory(album));
        }
    }
}
