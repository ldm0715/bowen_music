using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Views;

/// <summary>
/// 一个榜的完整榜单（实测 100 首），从排行榜页的「更多」进来。
/// </summary>
/// <remarks>
/// 它是**压在栈上的详情页**，不是根：侧栏该继续高亮「排行榜」。
/// 这也是导航栈「栈底即根」那条规则在这里的用处。
/// </remarks>
public sealed partial class BangDetailPage : Page, INavigationAware
{
    public BangDetailPage(BangDetailViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        ViewModel = viewModel;

        InitializeComponent();
    }

    public BangDetailViewModel ViewModel { get; }

    /// <summary>首次进入才拉；从别处退回来时保留已翻的页。</summary>
    public void OnNavigatedTo() => _ = ViewModel.EnsureLoadedAsync();

    /// <summary>不需要收尾：取曲目是懒加载的，没有常驻订阅要摘。</summary>
    public void OnNavigatedFrom()
    {
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is RankedTrack entry)
        {
            _ = ViewModel.PlayAsync(entry);
        }
    }
}
