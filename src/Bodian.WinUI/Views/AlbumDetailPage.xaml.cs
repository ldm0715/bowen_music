using Bodian.Core.Models;
using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
using Microsoft.UI.Xaml;
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
    private readonly INavigationService _navigation;
    private readonly Func<Artist, ArtistDetailPage> _artistFactory;

    public AlbumDetailPage(
        AlbumDetailViewModel viewModel,
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

    public AlbumDetailViewModel ViewModel { get; }

    public void OnNavigatedTo() => _ = ViewModel.EnsureLoadedAsync();

    /// <summary>不需要收尾：两次请求都是懒加载的，没有常驻订阅要摘。</summary>
    public void OnNavigatedFrom()
    {
    }

    private void OnTrackInvoked(object? sender, Track track) => _ = ViewModel.PlayAsync(track);

    /// <summary>
    /// 点头部的歌手进歌手页。
    /// </summary>
    /// <remarks>
    /// <b>不带计数</b>：专辑这边只知道歌手名与头像，歌曲数、专辑数、粉丝数都要靠
    /// 歌手页自己去取（<c>GetArtistInfoAsync</c>），取到之前头部那行元信息是不显示的。
    /// </remarks>
    private void OnArtistClick(object sender, RoutedEventArgs e)
    {
        var album = ViewModel.Album;

        if (album.ArtistId <= 0)
        {
            return;
        }

        _navigation.Navigate(_artistFactory(new Artist
        {
            Id = album.ArtistId,
            Name = album.ArtistText,
            CoverImage = album.ArtistCover,
        }));
    }
}
