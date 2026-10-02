using Bodian.Core.Models;
using Bodian.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Controls;

public sealed class ResultTemplateSelector : DataTemplateSelector
{
    public DataTemplate? HeaderTemplate { get; set; }
    public DataTemplate? BangTemplate { get; set; }
    public DataTemplate? HomeSectionTemplate { get; set; }
    public DataTemplate? TrackTemplate { get; set; }
    public DataTemplate? PlaylistTemplate { get; set; }
    public DataTemplate? AlbumTemplate { get; set; }
    public DataTemplate? ArtistTemplate { get; set; }

    protected override DataTemplate SelectTemplateCore(object item) => item switch
    {
        ListSectionHeader => HeaderTemplate!,
        BangItemViewModel => BangTemplate!,
        Bodian.Core.Models.Home.HomeSection => HomeSectionTemplate!,
        TrackRow => TrackTemplate!,
        Playlist => PlaylistTemplate!,
        Album => AlbumTemplate!,
        Artist => ArtistTemplate!,
        _ => base.SelectTemplateCore(item),
    };

    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
        => SelectTemplateCore(item);
}
