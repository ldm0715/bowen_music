using Bodian.Core.Api;
using Bodian.Core.Api.Paging;
using Bodian.Core.Lyrics;
using Bodian.Core.Models;
using Bodian.Core.Models.Home;
using Bodian.Core.Models.Lyrics;

namespace Bodian.Core.Tests.Support;

internal sealed class PlaybackApiStub : IBodianApi
    {
        public Task<string> GetLyricAsync(long musicId, int lrcx = 1, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<LyricDocument> GetLyricsAsync(Track track, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<SongCommentPage> GetSongCommentRepliesAsync(long musicId, long parentId, int page = 1,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<long?> PublishSongCommentAsync(long musicId, string content, long parentId = 0, long replyId = 0,
            bool anonymous = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetSongCommentLikeAsync(long musicId, long commentId, bool liked, long parentId = 0,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SongCommentPage> GetSongCommentsAsync(long musicId, SongCommentSort sort, int page = 1,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SearchResultSection>> SearchComprehensiveAsync(string keyword, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PagedResult<Album>> SearchAlbumsAsync(string keyword, PagedCursor cursor, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PagedResult<Playlist>> SearchPlaylistsAsync(string keyword, PagedCursor cursor, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PagedResult<Artist>> SearchArtistsAsync(string keyword, PagedCursor cursor, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> GetSearchSuggestionsAsync(string keyword, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SearchHotWord>> GetSearchHotWordsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PagedResult<Track>> GetArtistTracksAsync(long artistId, PagedCursor cursor, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PagedResult<Album>> GetArtistAlbumsAsync(long artistId, PagedCursor cursor, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Artist?> GetArtistInfoAsync(long artistId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PagedResult<Track>> SearchAsync(
            string keyword,
            PagedCursor cursor,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Func<long, CancellationToken, Task<Track?>> GetTrack { get; set; } = (_, _) => throw new NotSupportedException();
        public Task<Track?> GetTrackAsync(long musicId, CancellationToken cancellationToken = default)
            => GetTrack(musicId, cancellationToken);

        public Func<Track, AudioQuality, CancellationToken, Task<PlaybackResolution>> Resolve { get; set; } =
            (_, _, _) => throw new NotSupportedException();
        public Task<PlaybackResolution> ResolvePlaybackAsync(Track track, CancellationToken cancellationToken = default)
            => Resolve(track, AudioQuality.Lossless, cancellationToken);
        public Task<PlaybackResolution> ResolvePlaybackAsync(Track track, AudioQuality quality, CancellationToken cancellationToken = default)
            => Resolve(track, quality, cancellationToken);

        public Func<CancellationToken, Task<IReadOnlyList<Playlist>>> GetCreatedPlaylists { get; set; } =
            _ => throw new NotSupportedException();
        public Task<IReadOnlyList<Playlist>> GetCreatedPlaylistsAsync(
            CancellationToken cancellationToken = default)
            => GetCreatedPlaylists(cancellationToken);

        public Func<CancellationToken, Task<Playlist?>> GetLikedPlaylist { get; set; } =
            _ => throw new NotSupportedException();
        public Task<Playlist?> GetLikedPlaylistAsync(CancellationToken cancellationToken = default)
            => GetLikedPlaylist(cancellationToken);

        public Func<long, int, PagedCursor, CancellationToken, Task<PagedResult<Track>>> GetPlaylistTracks { get; set; } =
            (_, _, _, _) => throw new NotSupportedException();
        public Task<PagedResult<Track>> GetPlaylistTracksAsync(
            long playlistId,
            int source,
            PagedCursor cursor,
            CancellationToken cancellationToken = default)
            => GetPlaylistTracks(playlistId, source, cursor, cancellationToken);

        public Func<long, IReadOnlyList<long>, CancellationToken, Task> AddPlaylistMusic { get; set; } =
            (_, _, _) => throw new NotSupportedException();
        public Task AddPlaylistMusicAsync(long playlistId, IReadOnlyList<long> musicIds,
            CancellationToken cancellationToken = default)
            => AddPlaylistMusic(playlistId, musicIds, cancellationToken);

        public Func<long, IReadOnlyList<long>, CancellationToken, Task> RemovePlaylistMusic { get; set; } =
            (_, _, _) => throw new NotSupportedException();
        public Task RemovePlaylistMusicAsync(long playlistId, IReadOnlyList<long> musicIds,
            CancellationToken cancellationToken = default)
            => RemovePlaylistMusic(playlistId, musicIds, cancellationToken);

        public Func<long, CancellationToken, Task<ShareOutcome>> ReportShare { get; set; } =
            (_, _) => throw new NotSupportedException();
        public Task<ShareOutcome> ReportTrackShareAsync(long musicId, CancellationToken cancellationToken = default)
            => ReportShare(musicId, cancellationToken);

        public Task<PagedResult<Track>> GetPurchasedSinglesAsync(
            PagedCursor cursor,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PagedResult<Album>> GetPurchasedAlbumsAsync(
            PagedCursor cursor,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PagedResult<Album>> GetCollectedAlbumsAsync(
            PagedCursor cursor,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PagedResult<Playlist>> GetCollectedPlaylistsAsync(
            PagedCursor cursor,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Func<CancellationToken, Task<IReadOnlyList<Artist>>> GetFollowedArtists { get; set; } =
            _ => throw new NotSupportedException();
        public Task<IReadOnlyList<Artist>> GetFollowedArtistsAsync(CancellationToken cancellationToken = default)
            => GetFollowedArtists(cancellationToken);

        public Func<long, bool, CancellationToken, Task> SetArtistFollowed { get; set; } =
            (_, _, _) => throw new NotSupportedException();
        public Task SetArtistFollowedAsync(long artistId, bool followed, CancellationToken cancellationToken = default)
            => SetArtistFollowed(artistId, followed, cancellationToken);

        public Task<bool?> IsAlbumCollectedAsync(long albumId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Func<long, bool, CancellationToken, Task> SetAlbumCollected { get; set; } =
            (_, _, _) => throw new NotSupportedException();
        public Task SetAlbumCollectedAsync(long albumId, bool collected, CancellationToken cancellationToken = default)
            => SetAlbumCollected(albumId, collected, cancellationToken);

        public Func<long, int, CancellationToken, Task<Playlist?>> GetPlaylistInfo { get; set; } =
            (_, _, _) => throw new NotSupportedException();
        public Task<Playlist?> GetPlaylistInfoAsync(long playlistId, int source,
            CancellationToken cancellationToken = default)
            => GetPlaylistInfo(playlistId, source, cancellationToken);

        public Task SetPlaylistCollectedAsync(long playlistId, int source, bool collected,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<HomeModule>> GetHomeModulesAsync(
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<HomeFeed?> GetHomeModuleAsync(
            HomeModule module,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<AiPlaylist?> GetAiPlaylistAsync(
            int index,
            string passRecName,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Album?> GetAlbumAsync(long albumId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PagedResult<Track>> GetAlbumTracksAsync(
            long albumId,
            PagedCursor cursor,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PagedResult<Album>> GetMusicLibraryAlbumsAsync(
            string pTypeId,
            string cTypeId,
            MusicLibSort sort,
            PagedCursor cursor,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<MusicCategoryGroup>> GetMusicLibraryAsync(
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<CategoryGroup>> GetCategoriesAsync(
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PagedResult<Playlist>> GetCategoryPlaylistsAsync(
            long categoryId,
            PagedCursor cursor,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<BangSection>> GetBangSectionsAsync(
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PagedResult<Track>> GetBangTracksAsync(
            long bangId,
            PagedCursor cursor,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
