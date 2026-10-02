namespace Bodian.Core.Models;

public sealed record Artist
{
    public required long Id { get; init; }
    public required string Name { get; init; }
    public Uri? CoverImage { get; init; }
    public int SongCount { get; init; }
    public int AlbumCount { get; init; }
    public string Summary => $"{SongCount} 首歌曲 · {AlbumCount} 张专辑";
}

public sealed record SearchHotWord(string Keyword, int Rank);
