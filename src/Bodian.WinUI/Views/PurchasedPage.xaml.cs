using Bodian.Core.Models;
using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Views;

/// <summary>
/// 「已购音乐」页。侧栏的一个根页。
/// </summary>
public sealed partial class PurchasedPage : Page, INavigationAware
{
    private readonly Func<Album, AlbumDetailPage> _albumDetailFactory;
    private readonly INavigationService _navigation;

    public PurchasedPage(
        PurchasedViewModel viewModel,
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

    public PurchasedViewModel ViewModel { get; }

    public void OnNavigatedTo() => _ = ViewModel.EnsureLoadedAsync();

    /// <summary>不需要收尾：两个列表都是懒加载的，没有常驻订阅要摘。</summary>
    public void OnNavigatedFrom()
    {
    }

    private void OnTrackInvoked(object? sender, Track track) => ViewModel.PlayCommand.Execute(track);

    /// <summary>点专辑行 → 专辑详情。<b>压栈不用换根</b>：侧栏该继续高亮「已购音乐」。</summary>
    private void OnAlbumInvoked(object? sender, Album album) =>
        _navigation.Navigate(_albumDetailFactory(album));
}
