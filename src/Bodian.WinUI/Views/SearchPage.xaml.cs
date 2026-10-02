using Bodian.Core.Models;
using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;

namespace Bodian.WinUI.Views;

public sealed partial class SearchPage : Page, INavigationAware
{
    private readonly INavigationService _navigation;
    private readonly Func<Playlist, int, PlaylistDetailPage> _playlistFactory;
    private readonly Func<Album, AlbumDetailPage> _albumFactory;
    private readonly Func<Artist, ArtistDetailPage> _artistFactory;

    public SearchPage(SearchViewModel viewModel, INavigationService navigation,
        Func<Playlist, int, PlaylistDetailPage> playlistFactory, Func<Album, AlbumDetailPage> albumFactory,
        Func<Artist, ArtistDetailPage> artistFactory)
    {
        ViewModel = viewModel;
        _navigation = navigation;
        _playlistFactory = playlistFactory;
        _albumFactory = albumFactory;
        _artistFactory = artistFactory;
        InitializeComponent();
    }

    public SearchViewModel ViewModel { get; }
    public void OnNavigatedTo() { }
    public void OnNavigatedFrom() => ViewModel.ClearSuggestions();
    private void OnOverviewMoreClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SearchResultCategory category }) ViewModel.OpenCategoryCommand.Execute(category);
    }

    private void OnTrackInvoked(object? sender, Track track) => ViewModel.PlayCommand.Execute(track);
    private void OnPlaylistClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Playlist playlist) _navigation.Navigate(_playlistFactory(playlist, playlist.SourceType));
    }
    private void OnAlbumClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Album album) _navigation.Navigate(_albumFactory(album));
    }
    private void OnArtistClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Artist artist) _navigation.Navigate(_artistFactory(artist));
    }
}
