using System.Text.Json.Serialization;

namespace Bodian.Core.Api.Dto;

// 来源：docs/bodian-api-reference.md §2.2，2026-10-02 实测字段。
internal sealed class SearchPayload<T>
{
    [JsonPropertyName("total")] public int? Total { get; init; }
    [JsonPropertyName("resultList")] public T[]? ResultList { get; init; }
}

// 搜索歌单是 snake_case；不能复用详情的 PlaylistDto。
internal sealed class SearchPlaylistDto
{
    [JsonPropertyName("id")] public long Id { get; init; }
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("pic")] public string? Pic { get; init; }
    [JsonPropertyName("musicnum")] public int MusicCount { get; init; }
    [JsonPropertyName("source")] public int Source { get; init; }
}

internal sealed class SearchArtistDto
{
    [JsonPropertyName("artistId")] public long Id { get; init; }
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("pic")] public string? Pic { get; init; }
    [JsonPropertyName("songNum")] public int SongCount { get; init; }
    [JsonPropertyName("albumNum")] public int AlbumCount { get; init; }
}

internal sealed class SearchTipDto
{
    [JsonPropertyName("relword")] public string? Word { get; init; }
}

internal sealed class SearchTopicsPayload
{
    [JsonPropertyName("hotWord")] public SearchHotWordDto[]? HotWords { get; init; }
}

internal sealed class SearchHotWordDto
{
    [JsonPropertyName("key")] public string? Keyword { get; init; }
    [JsonPropertyName("sort")] public int Rank { get; init; }
}

// 实测 content 的每个对象直接以分类名为数组键，并非 type + resultList。
internal sealed class ComprehensiveSearchPayload
{
    [JsonPropertyName("content")] public ComprehensiveSearchSectionDto[]? Content { get; init; }
}

internal sealed class ComprehensiveSearchSectionDto
{
    [JsonPropertyName("musicpage")] public TrackDto[]? Tracks { get; init; }
    [JsonPropertyName("songlistpage")] public SearchPlaylistDto[]? Playlists { get; init; }
    [JsonPropertyName("albumpage")] public AlbumDto[]? Albums { get; init; }
    [JsonPropertyName("artistpage")] public SearchArtistDto[]? Artists { get; init; }
}
