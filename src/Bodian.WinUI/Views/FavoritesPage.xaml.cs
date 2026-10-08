using Bodian.Core.Models;
using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Views;

/// <summary>
/// 「我喜欢的」页。侧栏的一个根页。
/// </summary>
public sealed partial class FavoritesPage : Page, INavigationAware, IAccountScopedView
{
    public FavoritesPage(FavoritesViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        ViewModel = viewModel;

        InitializeComponent();
    }

    public FavoritesViewModel ViewModel { get; }

    /// <summary>首次进入才请求，切回来不重拉（翻页游标要留住，见基类的说明）。</summary>
    public void OnNavigatedTo() => _ = ViewModel.EnsureLoadedAsync();

    /// <summary>
    /// 换账号了：这份「我喜欢的」是上一个账号的，必须重拉。
    /// </summary>
    /// <remarks>
    /// 走 <c>ReloadAsync</c> 而不是 <c>EnsureLoadedAsync</c> —— 后者的「加载过就不再拉」守卫
    /// 正是这里要绕开的东西（页面没换、只是账号换了）。与工具栏上那个刷新按钮同一条路。
    /// </remarks>
    public Task OnAccountSwitchedAsync() => ViewModel.ReloadAsync();

    /// <summary>不需要收尾：取曲目是懒加载的，没有常驻订阅要摘。</summary>
    public void OnNavigatedFrom()
    {
    }

    private void OnTrackInvoked(object? sender, Track track) => ViewModel.PlayCommand.Execute(track);
}
