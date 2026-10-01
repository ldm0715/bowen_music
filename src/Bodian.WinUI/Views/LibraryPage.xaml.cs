using Bodian.Core.Models;
using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Views;

/// <summary>
/// 乐库首页：15 个大类。侧栏的一个根页。
/// </summary>
/// <remarks>
/// 数据来自 <c>play/music/library/navigation</c>（一次请求带回全部结构），
/// 所以这一页没有懒加载、没有分页。
/// </remarks>
public sealed partial class LibraryPage : Page, INavigationAware
{
    private readonly Func<MusicCategoryGroup, LibraryCategoryPage> _categoryFactory;
    private readonly INavigationService _navigation;

    public LibraryPage(
        LibraryViewModel viewModel,
        INavigationService navigation,
        Func<MusicCategoryGroup, LibraryCategoryPage> categoryFactory)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(categoryFactory);

        ViewModel = viewModel;
        _navigation = navigation;
        _categoryFactory = categoryFactory;

        InitializeComponent();
    }

    public LibraryViewModel ViewModel { get; }

    public void OnNavigatedTo() => _ = ViewModel.EnsureLoadedAsync();

    /// <summary>不需要收尾：一次加载完，没有常驻订阅要摘。</summary>
    public void OnNavigatedFrom()
    {
    }

    /// <summary>
    /// 点一个大类 → 进它的分类页。
    /// </summary>
    /// <remarks>
    /// <b>压栈（<c>Navigate</c>）不是换根</b>：分类页压在「乐库」上面，侧栏该继续高亮「乐库」。
    /// </remarks>
    private void OnGroupClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is MusicCategoryGroup group)
        {
            _navigation.Navigate(_categoryFactory(group));
        }
    }
}
