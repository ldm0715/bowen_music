using Bodian.Core.Api;
using Bodian.Core.Api.Paging;
using Bodian.Core.Lyrics;
using Bodian.Core.Models;
using Bodian.Core.Models.Account;
using Bodian.Core.Models.Home;
using Bodian.Core.Models.Lyrics;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 歌词缓存。用计数用的假门面，不碰传输层。
/// </summary>
public sealed class LyricRepositoryTests
{
    private static readonly LyricDocument OneLine = BodianLyricParser.Parse("[00:01.00]一句歌词");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Track Track(long id) => new() { Id = id, Title = $"曲目 {id}" };

    [Fact]
    public async Task GetAsync_SecondCallForTheSameTrack_DoesNotAskAgain()
    {
        var api = new CountingApi();
        var repository = new LyricRepository(api);

        var first = await repository.GetAsync(Track(1), Ct);
        var second = await repository.GetAsync(Track(1), Ct);

        Assert.Same(first, second);
        Assert.Equal(1, api.Calls);
    }

    /// <summary>
    /// 空结果也要缓存。
    /// </summary>
    /// <remarks>
    /// 「这首歌没有歌词」是稳定结论。不缓存的话每播一次都要空跑一趟请求，
    /// 而歌单里没歌词的歌往往不止一首。
    /// </remarks>
    [Fact]
    public async Task GetAsync_CachesEmptyDocumentsToo()
    {
        var api = new CountingApi { Responder = _ => LyricDocument.Empty };
        var repository = new LyricRepository(api);

        await repository.GetAsync(Track(1), Ct);
        await repository.GetAsync(Track(1), Ct);

        Assert.Equal(1, api.Calls);
    }

    [Fact]
    public async Task GetAsync_DifferentTracks_AreCachedSeparately()
    {
        var api = new CountingApi { Responder = _ => OneLine };
        var repository = new LyricRepository(api);

        await repository.GetAsync(Track(1), Ct);
        await repository.GetAsync(Track(2), Ct);
        await repository.GetAsync(Track(1), Ct);

        Assert.Equal(2, api.Calls);
    }

    [Fact]
    public async Task GetAsync_BeyondCapacity_EvictsTheLeastRecentlyUsed()
    {
        var api = new CountingApi { Responder = _ => OneLine };
        var repository = new LyricRepository(api, capacity: 2);

        await repository.GetAsync(Track(1), Ct);
        await repository.GetAsync(Track(2), Ct);

        // 摸一下 1，让 2 变成最久未用。
        await repository.GetAsync(Track(1), Ct);

        await repository.GetAsync(Track(3), Ct);

        Assert.Equal(3, api.Calls);

        await repository.GetAsync(Track(1), Ct);   // 还在
        Assert.Equal(3, api.Calls);

        await repository.GetAsync(Track(2), Ct);   // 被挤掉了
        Assert.Equal(4, api.Calls);
    }

    [Fact]
    public async Task Clear_ForcesTheNextCallToAskAgain()
    {
        var api = new CountingApi { Responder = _ => OneLine };
        var repository = new LyricRepository(api);

        await repository.GetAsync(Track(1), Ct);
        repository.Clear();
        await repository.GetAsync(Track(1), Ct);

        Assert.Equal(2, api.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsNonPositiveCapacity(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LyricRepository(new CountingApi(), capacity));
    }

    /// <summary>只数调用次数的最小门面；其余成员不该被用到。</summary>
    private sealed class CountingApi : IBodianApi
    {
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
        public Task AddPlaylistMusicAsync(long playlistId, IReadOnlyList<long> musicIds,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RemovePlaylistMusicAsync(long playlistId, IReadOnlyList<long> musicIds,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ShareOutcome> ReportTrackShareAsync(long musicId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> GetSearchSuggestionsAsync(string keyword, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SearchHotWord>> GetSearchHotWordsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PagedResult<Track>> GetArtistTracksAsync(long artistId, PagedCursor cursor, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PagedResult<Album>> GetArtistAlbumsAsync(long artistId, PagedCursor cursor, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Artist?> GetArtistInfoAsync(long artistId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PagedResult<Playlist>> GetCollectedPlaylistsAsync(
            PagedCursor cursor,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<Artist>> GetFollowedArtistsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Playlist?> GetPlaylistInfoAsync(long playlistId, int source,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SetPlaylistCollectedAsync(long playlistId, int source, bool collected,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SetArtistFollowedAsync(long artistId, bool followed,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool?> IsAlbumCollectedAsync(long albumId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SetAlbumCollectedAsync(long albumId, bool collected,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public int Calls { get; private set; }

        public Func<Track, LyricDocument> Responder { get; init; } = _ => LyricDocument.Empty;

        public Task<LyricDocument> GetLyricsAsync(Track track, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Responder(track));
        }

        public Task<MvInfo?> GetMvInfoAsync(long musicId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<string> GetLyricAsync(long musicId, int lrcx, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PagedResult<Track>> SearchAsync(
            string keyword,
            PagedCursor cursor,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Track?> GetTrackAsync(long musicId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PlaybackResolution> ResolvePlaybackAsync(
            Track track,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<Playlist>> GetCreatedPlaylistsAsync(
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<long> CreatePlaylistAsync(string name, bool isPrivate,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task UpdatePlaylistAsync(long playlistId, string name, string description, string pic,
            IReadOnlyList<int> categoryIds, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<string> UploadPlaylistCoverAsync(long playlistId, byte[] imageBytes, string fileName,
            string contentType, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task DeletePlaylistAsync(long playlistId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Playlist?> GetLikedPlaylistAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PagedResult<Track>> GetPlaylistTracksAsync(
            long playlistId,
            int source,
            PagedCursor cursor,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

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

        public Task<AccountMetadata?> GetAccountMetadataAsync(
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<AccountPlayData?> GetAccountPlayDataAsync(
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<AccountVipInfo?> GetAccountVipInfoAsync(
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
