using Bodian.Core.Models;
using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
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
    }

    public LibraryCategoryViewModel ViewModel { get; }

    /// <summary>点专辑 → 专辑详情。压栈，侧栏继续高亮「乐库」。</summary>
    private void OnAlbumClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Album album)
        {
            _navigation.Navigate(_albumDetailFactory(album));
        }
    }
}
