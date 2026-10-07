using Bodian.Core.Models;
using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System.ComponentModel;

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
    public void OnNavigatedTo()
    {
        if (Application.Current.Resources["BodianNowPlaying"] is PlayerViewModel player)
        {
            _player = player;
            player.PropertyChanged += OnPlayerChanged;
            SyncOverviewPlaying();
        }
        ViewModel.OverviewItems.CollectionChanged += OnOverviewChanged;
    }
    public void OnNavigatedFrom()
    {
        if (_player is not null) _player.PropertyChanged -= OnPlayerChanged;
        _player = null;
        ViewModel.OverviewItems.CollectionChanged -= OnOverviewChanged;
        ViewModel.ClearSuggestions();
    }
    private PlayerViewModel? _player;
    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs args)
    { if (args.PropertyName == nameof(PlayerViewModel.CurrentTrackId)) SyncOverviewPlaying(); }
    private void OnOverviewChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs args)
    {
        if (args.NewItems is not null)
            foreach (var item in args.NewItems.OfType<TrackRow>()) item.IsCurrent = item.Source.Id == _player?.CurrentTrackId;
    }
    private void SyncOverviewPlaying()
    {
        foreach (var row in ViewModel.OverviewItems.OfType<TrackRow>()) row.IsCurrent = row.Source.Id == _player?.CurrentTrackId;
    }
    private void OnOverviewRowPointerEntered(object sender, PointerRoutedEventArgs args)
    { if (sender is FrameworkElement { DataContext: TrackRow row }) row.IsPointerOver = true; }
    private void OnOverviewRowPointerExited(object sender, PointerRoutedEventArgs args)
    { if (sender is FrameworkElement { DataContext: TrackRow row }) row.IsPointerOver = false; }
    private void OnOverviewContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue && args.Item is TrackRow row)
        {
            row.IsPointerOver = false;
            row.IsMenuOpen = false;
        }
    }
    private void OnOverviewItemClick(object sender, ItemClickEventArgs args)
    {
        switch (args.ClickedItem)
        {
            case TrackRow row:
                CoverDropAnimation.Request((sender as ListViewBase)?.ContainerFromItem(row) as FrameworkElement, row.Source.Id);
                ViewModel.PlayCommand.Execute(row.Source);
                break;
            case Playlist playlist: _navigation.Navigate(_playlistFactory(playlist, playlist.SourceType)); break;
            case Album album: _navigation.Navigate(_albumFactory(album)); break;
            case Artist artist: _navigation.Navigate(_artistFactory(artist)); break;
        }
    }
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
