using Bodian.Core.Api;
using Bodian.Core.Api.Paging;
using Bodian.Core.Tests.Support;
using Xunit;

namespace Bodian.Core.Tests;

// 构造响应来自文档 §2.2 的字段契约；歌手作品另使用真实 fixture 回放。
public sealed class SearchApiTests : IDisposable
{
    private readonly ReplayHandler _handler = new();
    private readonly BodianSession _session = BodianSession.CreateAnonymous();
    private readonly BodianHttpTransport _transport;
    private readonly BodianApi _api;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public SearchApiTests()
    {
        var session = _session;
        var device = new FakeDeviceIdentity();
        _transport = new(_handler, new BodianTransportOptions(), session, device, FixedTimeProvider.Golden);
        _api = new(_transport, session, device);
    }
    public void Dispose() => _transport.Dispose();
    private void Respond(string data) => _handler.Responder = _ => ReplayHandler.Json("{\"code\":200,\"data\":" + data + "}");
    private static PagedCursor Cursor() => new(PagingConvention.ZeroBased, 30);
    private void AssertPublicRequest(string path, bool paged = true)
    {
        Assert.Contains(path, _handler.LastRequest.Url);
        Assert.DoesNotContain("sign=", _handler.LastRequest.Url);
        Assert.DoesNotContain("timestamp=", _handler.LastRequest.Url);
        if (paged)
        {
            Assert.Contains("pn=0", _handler.LastRequest.Url);
            Assert.Contains("rn=30", _handler.LastRequest.Url);
        }
    }

    [Fact]
    public async Task PlaylistSearch_PreservesSnakeCaseCountAndSource()
    {
        Respond("""{"total":1,"resultList":[{"id":"2867496601","name":"周杰伦","source":4,"musicnum":177,"pic":"https://example.com/cover.jpg"}]}""");
        var page = await _api.SearchPlaylistsAsync("周杰伦", Cursor(), Ct);
        var playlist = Assert.Single(page.Items);
        Assert.Equal(2867496601, playlist.Id);
        Assert.Equal(4, playlist.SourceType);
        Assert.Equal(177, playlist.MusicCount);
        Assert.NotNull(playlist.CoverImage);
        AssertPublicRequest("search/playlist/list");
        Respond("""{"list":[]}""");
        _session.Set("50303440", "test-token");
        await _api.GetPlaylistTracksAsync(playlist.Id, playlist.SourceType, new(PagingConvention.OneBased), Ct);
        Assert.Contains("source=4", _handler.LastRequest.Url);
    }

    [Theory]
    [InlineData("albumId")]
    [InlineData("id")]
    public async Task AlbumSearch_AcceptsBothDocumentedIds(string idField)
    {
        Respond("{\"resultList\":[{\"" + idField + "\":1293,\"name\":\"叶惠美\",\"artist\":\"周杰伦\",\"musicCount\":11}]}");
        var album = Assert.Single((await _api.SearchAlbumsAsync("叶惠美", Cursor(), Ct)).Items);
        Assert.Equal(1293, album.Id);
        Assert.Equal("周杰伦", album.ArtistText);
        Assert.Equal(11, album.MusicCount);
        AssertPublicRequest("search/album/list");
    }

    [Fact]
    public async Task ArtistSearch_MapsCounts()
    {
        Respond("""{"resultList":[{"artistId":336,"name":"周杰伦","songNum":300,"albumNum":47}]}""");
        var artist = Assert.Single((await _api.SearchArtistsAsync("周杰伦", Cursor(), Ct)).Items);
        Assert.Equal(336, artist.Id);
        Assert.Equal(300, artist.SongCount);
        Assert.Equal(47, artist.AlbumCount);
        AssertPublicRequest("search/artist/list");
    }

    [Theory]
    [InlineData("playlist")]
    [InlineData("album")]
    [InlineData("artist")]
    public async Task ShortPageAndIncorrectTotal_DoNotStopPagination(string category)
    {
        Respond("""{"total":0,"resultList":[{"id":1,"albumId":1,"artistId":1,"name":"test"}]}""");
        var cursor = Cursor();
        async Task Fetch()
        {
            switch (category)
            {
                case "playlist": await _api.SearchPlaylistsAsync("test", cursor, Ct); break;
                case "album": await _api.SearchAlbumsAsync("test", cursor, Ct); break;
                default: await _api.SearchArtistsAsync("test", cursor, Ct); break;
            }
        }
        await Fetch();
        Assert.False(cursor.Exhausted);
        Assert.Equal(1, cursor.PageNumber);
        Respond("""{"total":999,"resultList":[]}""");
        await Fetch();
        Assert.Contains("pn=1", _handler.LastRequest.Url);
        Assert.True(cursor.Exhausted);
        var requests = _handler.Requests.Count;
        await Fetch();
        Assert.Equal(requests, _handler.Requests.Count);
    }

    [Fact]
    public async Task Suggestions_NoTotalRequired_FiltersEmptyAndDuplicateWords()
    {
        Respond("""{"resultList":[{"relword":"周杰伦"},{"relword":""},{"relword":"周杰伦"},{"relword":"周杰伦晴天"},{}]}""");
        var words = await _api.GetSearchSuggestionsAsync("周", Ct);
        Assert.Equal(new[] { "周杰伦", "周杰伦晴天" }, words);
        AssertPublicRequest("search/tip/v2/list", false);
        Assert.Contains("keyword=", _handler.LastRequest.Url);
        Assert.DoesNotContain("pn=", _handler.LastRequest.Url);
    }

    [Fact]
    public async Task HotWords_SortedByRank_IgnoresOtherOperationalContent()
    {
        Respond("""{"hotTopic":[],"searchFind":[],"kingKong":[],"globalJumpInfo":{},"hotWord":[{"key":"晴天","sort":2,"source":4},{"key":"周杰伦","sort":1},{"key":"","sort":0}]}""");
        var words = await _api.GetSearchHotWordsAsync(Ct);
        Assert.Equal(new[] { "周杰伦", "晴天" }, words.Select(word => word.Keyword));
        Assert.Equal(new[] { 1, 2 }, words.Select(word => word.Rank));
        AssertPublicRequest("search/topic/word/list", false);
        Assert.DoesNotContain("keyword=", _handler.LastRequest.Url);
    }

    [Fact]
    public async Task MissingData_ReturnsEmptyLists()
    {
        _handler.Responder = _ => ReplayHandler.Json("""{"code":200,"data":{}}""");
        Assert.Empty((await _api.SearchAlbumsAsync("test", Cursor(), Ct)).Items);
        Assert.Empty((await _api.SearchPlaylistsAsync("test", Cursor(), Ct)).Items);
        Assert.Empty((await _api.SearchArtistsAsync("test", Cursor(), Ct)).Items);
        Assert.Empty(await _api.GetSearchSuggestionsAsync("test", Ct));
        Assert.Empty(await _api.GetSearchHotWordsAsync(Ct));
    }

    [Fact]
    public async Task BlankKeywords_SendNoRequest()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _api.SearchAlbumsAsync(" ", Cursor(), Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => _api.SearchPlaylistsAsync(" ", Cursor(), Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => _api.SearchArtistsAsync(" ", Cursor(), Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => _api.GetSearchSuggestionsAsync(" ", Ct));
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task ArtistTracks_RealFixture_StartsAtZero()
    {
        _handler.Responder = _ => ReplayHandler.Json(Fixtures.Read("artist-336-music.json"));
        var page = await _api.GetArtistTracksAsync(336, Cursor(), Ct);
        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, track => Assert.True(track.Id > 0));
        AssertPublicRequest("service/artist/music/336");
    }

    [Fact]
    public async Task ArtistAlbums_RealFixture_StartsAtZero()
    {
        _handler.Responder = _ => ReplayHandler.Json(Fixtures.Read("artist-336-album.json"));
        var page = await _api.GetArtistAlbumsAsync(336, Cursor(), Ct);
        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, album => Assert.True(album.Id > 0));
        AssertPublicRequest("service/artist/album/336");
    }

    [Fact]
    public async Task CancelledSearch_DoesNotAdvanceCursor()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var cursor = Cursor();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _api.SearchArtistsAsync("test", cursor, cancellation.Token));
        Assert.Equal(0, cursor.Offset);
        Assert.False(cursor.Exhausted);
    }
    [Fact]
    public async Task Comprehensive_RealResponse_PreservesSectionsAndPreviewSizes()
    {
        _handler.Responder = _ => ReplayHandler.Json(Fixtures.Read("search-comprehensive-anon.json"));
        var sections = await _api.SearchComprehensiveAsync("周杰伦", Ct);
        Assert.Equal(new[] { Bodian.Core.Models.SearchResultCategory.Tracks, Bodian.Core.Models.SearchResultCategory.Playlists,
            Bodian.Core.Models.SearchResultCategory.Artists, Bodian.Core.Models.SearchResultCategory.Albums }, sections.Select(section => section.Category));
        Assert.Equal(new[] { 30, 5, 3, 5 }, sections.Select(section => section.Count));
        Assert.All(sections.Single(section => section.IsPlaylists).Playlists, playlist => Assert.Equal(4, playlist.SourceType));
        Assert.All(sections.Single(section => section.IsAlbums).Albums, album => Assert.True(album.Id > 0));
        Assert.All(sections.Single(section => section.IsTracks).Tracks, track => Assert.True(track.Id > 0));
        Assert.Single(_handler.Requests);
        AssertPublicRequest("search/comprehensive/v2/list", false);
        Assert.DoesNotContain("pn=", _handler.LastRequest.Url);
        Assert.DoesNotContain("rn=", _handler.LastRequest.Url);
    }

    [Fact]
    public async Task Comprehensive_OnlyShowsReturnedMusicSections()
    {
        Respond("""{"content":[{"interestpage":[{"id":9}]},{"userpage":[{"id":5,"payRights":{}}]},{"artistpage":[{"artistId":336,"name":"周杰伦"}],"total":54},{"musicpage":[],"total":99},{"songlistpage":[]},{"albumpage":[]}]}""");
        var sections = await _api.SearchComprehensiveAsync("周杰伦", Ct);
        var section = Assert.Single(sections);
        Assert.True(section.IsArtists);
        Assert.Equal(336, Assert.Single(section.Artists).Id);
        Assert.Empty(section.Tracks);
        Assert.Empty(section.Albums);
        Assert.Empty(section.Playlists);
        Assert.Single(_handler.Requests);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"content\":[]}")]
    [InlineData("{\"content\":null}")]
    public async Task Comprehensive_MissingContent_ReturnsNoSections(string data)
    {
        Respond(data);
        Assert.Empty(await _api.SearchComprehensiveAsync("test", Ct));
    }

    [Fact]
    public async Task Comprehensive_BlankKeyword_SendsNothing()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _api.SearchComprehensiveAsync(" ", Ct));
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task Comprehensive_CancelledRequest_DoesNotReturnStalePreview()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _api.SearchComprehensiveAsync("test", cancellation.Token));
    }

}
