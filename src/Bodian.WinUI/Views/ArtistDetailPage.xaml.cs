using Bodian.Core.Models;
using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Views;

public sealed partial class ArtistDetailPage : Page, INavigationAware, INavigationIdentity
{
    private readonly INavigationService _navigation;
    private readonly Func<Album, AlbumDetailPage> _albumFactory;
    public ArtistDetailPage(ArtistDetailViewModel viewModel, INavigationService navigation, Func<Album, AlbumDetailPage> albumFactory)
    {
        ViewModel = viewModel;
        _navigation = navigation;
        _albumFactory = albumFactory;
        InitializeComponent();
    }
    public ArtistDetailViewModel ViewModel { get; }
    public object NavigationIdentity => ("artist", ViewModel.Artist.Id);
    public void OnNavigatedTo() => _ = ViewModel.EnsureLoadedAsync();
    public void OnNavigatedFrom() { }
    private void OnTrackInvoked(object? sender, Track track) => _ = ViewModel.PlayAsync(track);
    private void OnAlbumClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Album album) _navigation.Navigate(_albumFactory(album));
    }
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is Pivot { SelectedIndex: 1 }) _ = ViewModel.Albums.EnsureLoadedAsync();
    }
}
