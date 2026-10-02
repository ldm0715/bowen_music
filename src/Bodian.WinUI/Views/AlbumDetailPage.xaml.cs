using Bodian.Core.Models;
using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Views;

/// <summary>
/// 专辑详情页：简介 + 曲目。
/// </summary>
/// <remarks>
/// <b>压在栈上的详情页，不是根</b>：从「已购音乐」或「收藏的专辑」点进来时，
/// 侧栏该继续高亮原来那一项。
/// </remarks>
public sealed partial class AlbumDetailPage : Page, INavigationAware
{
    public AlbumDetailPage(AlbumDetailViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        ViewModel = viewModel;

        InitializeComponent();
    }

    public AlbumDetailViewModel ViewModel { get; }

    public void OnNavigatedTo() => _ = ViewModel.EnsureLoadedAsync();

    /// <summary>不需要收尾：两次请求都是懒加载的，没有常驻订阅要摘。</summary>
    public void OnNavigatedFrom()
    {
    }

    private void OnTrackInvoked(object? sender, Track track) => _ = ViewModel.PlayAsync(track);
}
