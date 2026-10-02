namespace Bodian.Core.Models;

// 分类值对应搜索页 tab：0 为综合，其余是四个分页分类。
public enum SearchResultCategory { Tracks = 1, Playlists = 2, Albums = 3, Artists = 4 }

public sealed record SearchResultSection
{
    public required SearchResultCategory Category { get; init; }
    public IReadOnlyList<Track> Tracks { get; init; } = [];
    public IReadOnlyList<Playlist> Playlists { get; init; } = [];
    public IReadOnlyList<Album> Albums { get; init; } = [];
    public IReadOnlyList<Artist> Artists { get; init; } = [];
    public bool IsTracks => Category == SearchResultCategory.Tracks;
    public bool IsPlaylists => Category == SearchResultCategory.Playlists;
    public bool IsAlbums => Category == SearchResultCategory.Albums;
    public bool IsArtists => Category == SearchResultCategory.Artists;
    public string Title => Category switch
    {
        SearchResultCategory.Tracks => "单曲",
        SearchResultCategory.Playlists => "歌单",
        SearchResultCategory.Albums => "专辑",
        SearchResultCategory.Artists => "歌手",
        _ => "",
    };
    public int Count => Tracks.Count + Playlists.Count + Albums.Count + Artists.Count;
}
