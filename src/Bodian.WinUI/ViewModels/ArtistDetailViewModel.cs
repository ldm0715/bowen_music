using Bodian.Core.Api;
using Bodian.Core.Api.Paging;
using Bodian.Core.Models;
using Bodian.WinUI.Playback;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.WinUI.ViewModels;

public sealed class ArtistDetailViewModel
{
    private readonly PlaybackCoordinator _coordinator;
    public ArtistDetailViewModel(IBodianApi api, PlaybackCoordinator coordinator, Artist artist)
    {
        Artist = artist;
        _coordinator = coordinator;
        Tracks = new((cursor, token) => api.GetArtistTracksAsync(artist.Id, cursor, token),
            NullLogger.Instance, "歌手歌曲", "暂无歌曲", PagingConvention.ZeroBased);
        Albums = new((cursor, token) => api.GetArtistAlbumsAsync(artist.Id, cursor, token),
            NullLogger.Instance, "歌手专辑", "暂无专辑", PagingConvention.ZeroBased);
    }
    public Artist Artist { get; }
    public PagedList<Track> Tracks { get; }
    public PagedList<Album> Albums { get; }
    public Task EnsureLoadedAsync() => Tracks.EnsureLoadedAsync();
    public async Task PlayAsync(Track track)
    {
        var index = Tracks.Items.IndexOf(track);
        if (index >= 0) await _coordinator.PlayFromAsync([.. Tracks.Items], index);
    }
}
