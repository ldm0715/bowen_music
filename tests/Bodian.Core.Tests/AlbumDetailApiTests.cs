using Bodian.Core.Api;
using Bodian.Core.Api.Paging;
using Bodian.Core.Models;
using Bodian.Core.Tests.Support;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 专辑详情与乐库。响应来自真实捕获的 fixture，零真实网络。
/// </summary>
public sealed class AlbumDetailApiTests : IDisposable
{
    /// <summary>实测样本用的专辑：周杰伦《叶惠美》。</summary>
    private const long AlbumId = 1293;

    private readonly ReplayHandler _handler = new();
    private readonly BodianSession _session = BodianSession.CreateAnonymous();
    private readonly BodianHttpTransport _transport;
    private readonly BodianApi _api;

    public AlbumDetailApiTests()
    {
        _transport = new BodianHttpTransport(
            _handler,
            new BodianTransportOptions(),
            _session,
            new FakeDeviceIdentity(),
            FixedTimeProvider.Golden);

        _api = new BodianApi(_transport, _session, new FakeDeviceIdentity());
    }

    public void Dispose() => _transport.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private void RespondWith(string fixture) =>
        _handler.Responder = _ => ReplayHandler.Json(Fixtures.Read(fixture));

    // ── 专辑详情 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Album_MapsRealResponse()
    {
        RespondWith("album-1293.json");

        var album = await _api.GetAlbumAsync(AlbumId, Ct);

        Assert.NotNull(album);
        Assert.Equal(AlbumId, album.Id);
        Assert.Equal("叶惠美", album.Name);
        Assert.Equal("周杰伦", album.ArtistText);
        Assert.Equal("2003-07-31", album.ReleaseDate);
        Assert.Equal(11, album.MusicCount);
        Assert.NotNull(album.CoverImage);

        // 简介实测是整篇企划文案（几千字），界面上要折叠 —— 这里只确认它真的取到了。
        Assert.True(album.HasDescription);
        Assert.True(album.Description.Length > 500);
    }

    /// <summary>
    /// 详情里**不含曲目** —— 曲目要另外取。
    /// </summary>
    /// <remarks>
    /// 这条守的是「以为一个请求就够了」：详情响应的 <c>albumInfo</c> 里只有元数据。
    /// </remarks>
    [Fact]
    public async Task Album_HasNoTracks()
    {
        RespondWith("album-1293.json");

        var album = await _api.GetAlbumAsync(AlbumId, Ct);

        Assert.NotNull(album);

        // Album 模型里压根没有曲目字段 —— 这条断言靠的是它只能靠 GetAlbumTracksAsync 拿曲目。
        Assert.Equal(11, album.MusicCount);
    }

    /// <summary>
    /// <c>id</c> 与 <c>albumId</c> 同值，取任一都对；两个都是 0 才算解析失败。
    /// </summary>
    [Fact]
    public async Task Album_ReadsIdFromEitherKey()
    {
        RespondWith("album-1293.json");

        Assert.Equal(AlbumId, (await _api.GetAlbumAsync(AlbumId, Ct))!.Id);
    }

    [Fact]
    public async Task Album_SendsIdInPathWithoutQuery()
    {
        RespondWith("album-1293.json");

        await _api.GetAlbumAsync(AlbumId, Ct);

        // ★ 只数一个 ? —— 把 id 写进路径之后再由传输层拼 query 会多出一个 ?，
        //   服务端回 400。这条断言才看得见多出来的字符。
        Assert.StartsWith("https://bd-api.kuwo.cn/api/service/album/1293?", _handler.LastRequest.Url);
        Assert.Equal(1, _handler.LastRequest.Url.Count(character => character == '?'));
    }

    // ── 专辑曲目 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AlbumTracks_MapsRealResponse()
    {
        RespondWith("album-1293-tracks.json");

        var page = await _api.GetAlbumTracksAsync(AlbumId, new PagedCursor(PagingConvention.ZeroBased, 5), Ct);

        Assert.NotEmpty(page.Items);
        Assert.Equal(11, page.Total);
        Assert.False(string.IsNullOrWhiteSpace(page.Items[0].Title));
    }

    /// <summary>
    /// 专辑曲目的数组键是 <c>resultList</c> —— **与歌单的 <c>list</c> 不同**。
    /// </summary>
    /// <remarks>
    /// 读错会得到空列表而不是异常，界面上表现为「这张专辑是空的」。
    /// </remarks>
    [Fact]
    public async Task AlbumTracks_ReadsResultListKey()
    {
        RespondWith("album-1293-tracks.json");

        var page = await _api.GetAlbumTracksAsync(AlbumId, new PagedCursor(PagingConvention.ZeroBased, 5), Ct);

        Assert.NotEmpty(page.Items);
    }

    /// <summary>专辑曲目的页号从 0 开始（与搜索一致，与歌单的从 1 不同）。</summary>
    [Fact]
    public async Task AlbumTracks_SendsZeroBasedPaging()
    {
        RespondWith("album-1293-tracks.json");

        await _api.GetAlbumTracksAsync(AlbumId, new PagedCursor(PagingConvention.ZeroBased, 5), Ct);

        Assert.Contains("service/album/music/1293?", _handler.LastRequest.Url);
        Assert.Contains("pn=0", _handler.LastRequest.Url);
    }

    [Fact]
    public async Task AlbumTracks_NonPositiveId_Throws()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _api.GetAlbumTracksAsync(0, new PagedCursor(PagingConvention.ZeroBased), Ct));
    }

    [Fact]
    public async Task Album_NonPositiveId_Throws()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _api.GetAlbumAsync(0, Ct));
    }

    // ── 乐库（MusicLib）────────────────────────────────────────────────────

    /// <summary>
    /// 乐库是 <c>play/music/library/navigation</c>，<b>不是 <c>service/category/list</c></b>。
    /// </summary>
    /// <remarks>
    /// 两者是不同功能：前者是官方按曲风分类的**专辑**库，后者是用户创建的**歌单**广场。
    /// 本项目一度搞混过，这条守着别再搞混。
    /// </remarks>
    [Fact]
    public async Task MusicLibrary_UsesTheMusicLibEndpoint()
    {
        RespondWith("musiclib-navigation.json");

        await _api.GetMusicLibraryAsync(Ct);

        Assert.Contains("play/music/library/navigation?", _handler.LastRequest.Url);
        Assert.DoesNotContain("service/category", _handler.LastRequest.Url);
    }

    [Fact]
    public async Task MusicLibrary_MapsRealResponse()
    {
        RespondWith("musiclib-navigation.json");

        var groups = await _api.GetMusicLibraryAsync(Ct);

        // 实测 15 个大类。
        Assert.Equal(15, groups.Count);

        var pop = groups.First(group => group.Name == "流行");

        Assert.Equal("2005", pop.Id);
        Assert.True(pop.AlbumCount > 0);
        Assert.False(string.IsNullOrWhiteSpace(pop.Description));
        Assert.NotNull(pop.CoverImage);

        // internalTitle 里带换行（形如 "POP\nMUSIC"），映射时要压成一行。
        Assert.DoesNotContain('\n', pop.Title);
    }

    /// <summary>用户点名要看的那几个分类都在。</summary>
    [Theory]
    [InlineData("流行")]
    [InlineData("嘻哈")]
    [InlineData("民谣")]
    [InlineData("摇滚")]
    [InlineData("爵士")]
    [InlineData("古典")]
    [InlineData("轻音乐")]
    [InlineData("ACG音乐")]
    public async Task MusicLibrary_HasTheExpectedCategories(string name)
    {
        RespondWith("musiclib-navigation.json");

        var groups = await _api.GetMusicLibraryAsync(Ct);

        Assert.Contains(groups, group => group.Name == name);
    }

    /// <summary>每个大类都带子类，子类又各带一张代表专辑。</summary>
    [Fact]
    public async Task MusicLibrary_ChildrenCarryASampleAlbum()
    {
        RespondWith("musiclib-navigation.json");

        var pop = (await _api.GetMusicLibraryAsync(Ct)).First(group => group.Name == "流行");
        var child = pop.Children.First(c => c.Name == "国语流行");

        Assert.Equal("8101", child.Id);
        Assert.False(string.IsNullOrWhiteSpace(child.Title));
        Assert.False(string.IsNullOrWhiteSpace(child.Description));

        Assert.NotNull(child.SampleAlbum);
        Assert.True(child.SampleAlbum.Id > 0);
        Assert.NotNull(child.SampleAlbumCover);
        Assert.False(string.IsNullOrWhiteSpace(child.SampleAlbumText));
    }

    /// <summary>没有样本专辑时，卡片小字退回子类标题，而不是空串。</summary>
    [Fact]
    public async Task MusicLibrary_ChildWithoutSampleAlbum_FallsBackToTitle()
    {
        _handler.Responder = _ => ReplayHandler.Json(
            """
            {"code":200,"msg":"success","data":[
              {"id":"2005","ptypeName":"流行","childList":[
                {"id":"8101","name":"国语流行","longDescTitle":"[ 国语流行 ]"}]}]}
            """);

        var child = (await _api.GetMusicLibraryAsync(Ct))[0].Children[0];

        Assert.Null(child.SampleAlbum);
        Assert.Null(child.SampleAlbumCover);
        Assert.Equal("[ 国语流行 ]", child.SampleAlbumText);
    }

    // ── 乐库子类专辑列表 ───────────────────────────────────────────────────

    /// <summary>
    /// <c>sort</c> 必须是**字符串** <c>"1"</c>/<c>"2"</c>，不是数字。
    /// </summary>
    /// <remarks>
    /// <b>这是整条链路卡最久的一处。</b> 传数字（哪怕 <c>"0"</c>）服务端回
    /// <c>200</c> 但 <c>data</c> 是空对象 —— 不报错、看起来像"这个子类没有专辑"。
    /// 这条守着两个枚举值到线上字符串的换算。
    /// </remarks>
    [Theory]
    [InlineData(MusicLibSort.Curated, "1")]
    [InlineData(MusicLibSort.Newest, "2")]
    public void Sort_MapsToTheServerString(MusicLibSort sort, string expected)
    {
        Assert.Equal(expected, sort.ToRequestValue());
    }

    [Fact]
    public async Task LibraryAlbums_SendsAllFourParameters()
    {
        RespondWith("musiclib-albums-8101.json");

        await _api.GetMusicLibraryAlbumsAsync(
            "2005",
            "8101",
            MusicLibSort.Curated,
            new PagedCursor(PagingConvention.OneBased, 20),
            Ct);

        var url = _handler.LastRequest.Url;

        Assert.Contains("play/music/library/albums?", url);
        Assert.Contains("pTypeId=2005", url);

        // cTypeId 是**子类的 id**，不是它的 ptypeId 字段（那是 3401）。
        Assert.Contains("cTypeId=8101", url);
        Assert.Contains("pn=1", url);
        Assert.Contains("rn=20", url);

        // ★ 是字符串 "1" 而不是裸掉的数字 —— 两者在 URL 上长得一样，
        //   所以这条真正守的是「别改回数字 type」：一旦改成 int，调用点会编译不过。
        Assert.Contains("sort=1", url);
    }

    [Fact]
    public async Task LibraryAlbums_MapsRealResponse()
    {
        RespondWith("musiclib-albums-8101.json");

        var page = await _api.GetMusicLibraryAlbumsAsync(
            "2005",
            "8101",
            MusicLibSort.Curated,
            new PagedCursor(PagingConvention.OneBased, 20),
            Ct);

        // 实测国语流行 5177 张。
        Assert.Equal(5177, page.Total);
        Assert.Equal(20, page.Items.Count);

        var first = page.Items[0];

        Assert.True(first.Id > 0);
        Assert.False(string.IsNullOrWhiteSpace(first.Name));
        Assert.False(string.IsNullOrWhiteSpace(first.ArtistText));
        Assert.NotNull(first.CoverImage);
    }

    /// <summary>两个排序都能取到数据（各是一套不同的排序结果）。</summary>
    [Theory]
    [InlineData(MusicLibSort.Curated)]
    [InlineData(MusicLibSort.Newest)]
    public async Task LibraryAlbums_BothSortsReturnData(MusicLibSort sort)
    {
        RespondWith("musiclib-albums-8101.json");

        var page = await _api.GetMusicLibraryAlbumsAsync(
            "2005",
            "8101",
            sort,
            new PagedCursor(PagingConvention.OneBased, 20),
            Ct);

        Assert.NotEmpty(page.Items);
    }

    /// <summary>第一页空就到底，不该再请求下一页。</summary>
    [Fact]
    public async Task LibraryAlbums_EmptyPage_ExhaustsCursor()
    {
        _handler.Responder = _ => ReplayHandler.Json(
            """{"code":200,"msg":"success","data":{"total":0,"list":[]}}""");

        var cursor = new PagedCursor(PagingConvention.OneBased, 20);
        var page = await _api.GetMusicLibraryAlbumsAsync("2005", "8101", MusicLibSort.Curated, cursor, Ct);

        Assert.Empty(page.Items);
        Assert.True(cursor.Exhausted);
    }

    /// <summary>两个 id 都不能空 —— 空的会拼出无效 URL 并静默拿到空数据。</summary>
    [Fact]
    public async Task LibraryAlbums_BlankIds_Throw()
    {
        var cursor = new PagedCursor(PagingConvention.OneBased);

        await Assert.ThrowsAsync<ArgumentException>(
            () => _api.GetMusicLibraryAlbumsAsync("", "8101", MusicLibSort.Curated, cursor, Ct));
        await Assert.ThrowsAsync<ArgumentException>(
            () => _api.GetMusicLibraryAlbumsAsync("2005", "", MusicLibSort.Curated, cursor, Ct));
    }

    // ── 乐库分类（歌单广场）─────────────────────────────────────────────────

    [Fact]
    public async Task Categories_MapRealResponse()
    {
        RespondWith("category-list.json");

        var groups = await _api.GetCategoriesAsync(Ct);

        // 实测 6 组：主题 / 流派 / 语言 / 心情 / 场景 / 年代。
        Assert.Equal(6, groups.Count);
        Assert.Contains(groups, group => group.Name == "主题");
        Assert.Contains(groups, group => group.Name == "流派");

        var theme = groups.First(group => group.Name == "主题");

        Assert.Equal(12, theme.SubCategories.Count);
        Assert.Contains(theme.SubCategories, category => category.Name == "网红");
    }

    /// <summary>
    /// 每个子分类都带 id —— 没有 id 的条目取不了歌单，映射时就该滤掉。
    /// </summary>
    [Fact]
    public async Task Categories_AllSubCategoriesHaveIds()
    {
        RespondWith("category-list.json");

        var groups = await _api.GetCategoriesAsync(Ct);

        Assert.All(groups, group => Assert.All(group.SubCategories, category => Assert.True(category.Id > 0)));
        Assert.All(groups, group => Assert.NotEmpty(group.SubCategories));
    }

    [Fact]
    public async Task Categories_SendNoParameters()
    {
        RespondWith("category-list.json");

        await _api.GetCategoriesAsync(Ct);

        Assert.Contains("service/category/list?", _handler.LastRequest.Url);
    }

    [Fact]
    public async Task CategoryPlaylists_MapRealResponse()
    {
        RespondWith("category-77-playlist.json");

        var page = await _api.GetCategoryPlaylistsAsync(77, new PagedCursor(PagingConvention.OneBased, 10), Ct);

        Assert.NotEmpty(page.Items);
        Assert.Equal(2707, page.Total);

        var first = page.Items[0];

        Assert.True(first.Id > 0);
        Assert.False(string.IsNullOrWhiteSpace(first.Name));

        // ★ 实测 sourceType 是 4（公开集合）—— 点进歌单详情要用它当 source，
        //   填错的话曲目列表会是空的（服务端只回空、不报错）。
        Assert.Equal(4, first.SourceType);
    }

    /// <summary>分类歌单的信封是 <c>playLists</c> —— 与收藏歌单同一个，所以复用同一个 DTO。</summary>
    [Fact]
    public async Task CategoryPlaylists_ReadsPlayListsKey()
    {
        RespondWith("category-77-playlist.json");

        var page = await _api.GetCategoryPlaylistsAsync(77, new PagedCursor(PagingConvention.OneBased, 10), Ct);

        Assert.NotEmpty(page.Items);
    }

    [Fact]
    public async Task CategoryPlaylists_NonPositiveId_Throws()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _api.GetCategoryPlaylistsAsync(0, new PagedCursor(PagingConvention.OneBased), Ct));
    }

    /// <summary>乐库与专辑都不要求登录 —— 实测匿名可见。</summary>
    [Fact]
    public async Task Library_WorksWithoutLogin()
    {
        _session.Clear();
        RespondWith("album-1293.json");

        Assert.NotNull(await _api.GetAlbumAsync(AlbumId, Ct));
    }
}
