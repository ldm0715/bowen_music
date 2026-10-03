using Bodian.Core.Api;
using Bodian.Core.Api.Paging;
using Bodian.Core.Tests.Support;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 歌手详情。响应来自真实捕获的 fixture（<c>fixtures/artist-336.json</c>，零真实网络）。
/// </summary>
/// <remarks>
/// 别名、粉丝数、简介<b>只有这条接口会给</b> —— 搜索结果的歌手条目里没有这几个字段，
/// 所以歌手页必须单独拉一次。
/// </remarks>
public sealed class ArtistInfoApiTests : IDisposable
{
    /// <summary>实测样本用的歌手：周杰伦。</summary>
    private const long ArtistId = 336;

    private readonly ReplayHandler _handler = new();
    private readonly BodianSession _session = BodianSession.CreateAnonymous();
    private readonly BodianHttpTransport _transport;
    private readonly BodianApi _api;

    public ArtistInfoApiTests()
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

    [Fact]
    public async Task ArtistInfo_MapsRealResponse()
    {
        RespondWith("artist-336.json");

        var artist = await _api.GetArtistInfoAsync(ArtistId, Ct);

        Assert.NotNull(artist);
        Assert.Equal(ArtistId, artist.Id);
        Assert.Equal("周杰伦", artist.Name);
        Assert.Equal("Jay Chou", artist.AliasName);
        Assert.Equal(4848318, artist.FansCount);
        Assert.Equal(1708, artist.SongCount);
        Assert.Equal(47, artist.AlbumCount);
        Assert.NotNull(artist.CoverImage);

        Assert.True(artist.HasAlias);
        Assert.True(artist.HasFans);
    }

    /// <summary>
    /// 简介实测是整篇人物生平（周杰伦那条 12466 字符），界面上必须单独滚动。
    /// </summary>
    [Fact]
    public async Task ArtistInfo_DescriptionIsVeryLong()
    {
        RespondWith("artist-336.json");

        var artist = await _api.GetArtistInfoAsync(ArtistId, Ct);

        Assert.NotNull(artist);
        Assert.True(artist.HasDescription);
        Assert.True(artist.Description.Length > 5000);
    }

    /// <summary>
    /// id 走路径、<b>不带 query</b>，且请求不带签名。
    /// </summary>
    /// <remarks>
    /// 多一个 <c>?</c> 会让服务端回 400；把 id 塞进 query 也是错的。
    /// </remarks>
    [Fact]
    public async Task ArtistInfo_SendsIdInPathWithoutQuery()
    {
        RespondWith("artist-336.json");

        await _api.GetArtistInfoAsync(ArtistId, Ct);

        Assert.StartsWith("https://bd-api.kuwo.cn/api/service/artist/336?", _handler.LastRequest.Url);
        Assert.Equal(1, _handler.LastRequest.Url.Count(character => character == '?'));
    }

    /// <summary>这条不要求登录 —— 与歌手曲目/专辑一致。</summary>
    [Fact]
    public async Task ArtistInfo_WorksWithoutLogin()
    {
        _session.Clear();
        RespondWith("artist-336.json");

        Assert.NotNull(await _api.GetArtistInfoAsync(ArtistId, Ct));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task ArtistInfo_NonPositiveId_Throws(long artistId)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _api.GetArtistInfoAsync(artistId, Ct));
    }

    /// <summary>
    /// 服务端没给名字时退回占位，而不是空串。
    /// </summary>
    /// <remarks>
    /// 详情页头部留一片空白比一个明显是占位的词更难排查。
    /// </remarks>
    [Fact]
    public async Task ArtistInfo_MissingName_FallsBackToPlaceholder()
    {
        _handler.Responder = _ => ReplayHandler.Json(
            """{"code":200,"msg":"success","data":{"artistInfo":{"aliasName":"X"}}}""");

        var artist = await _api.GetArtistInfoAsync(ArtistId, Ct);

        Assert.NotNull(artist);
        Assert.Equal("(未命名歌手)", artist.Name);

        // 缺的字段走默认值，不是 null：粉丝数为 0 → 界面据此不显示这一段。
        Assert.Equal(0, artist.FansCount);
        Assert.False(artist.HasFans);
        Assert.False(artist.HasDescription);

        // 给了的字段照常映射。
        Assert.Equal("X", artist.AliasName);
        Assert.True(artist.HasAlias);
    }

    /// <summary>
    /// 响应里没有 <c>artistInfo</c> 时返回 <c>null</c>，不抛。
    /// </summary>
    [Fact]
    public async Task ArtistInfo_EmptyData_ReturnsNull()
    {
        _handler.Responder = _ => ReplayHandler.Json(
            """{"code":200,"msg":"success","data":{}}""");

        Assert.Null(await _api.GetArtistInfoAsync(ArtistId, Ct));
    }

    // ── 歌手专辑 / 歌手歌曲的分页基数 ───────────────────────────────────────

    /// <summary>
    /// 歌手专辑从 <b>第 1 页</b>开始（<c>pn=1</c>），不是第 0 页。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>这个 bug 藏得很深，断言必须覆盖第二页。</b> 服务端把 <c>pn=0</c> 当成第 1 页处理，
    /// 不报错 —— 按 0 基发请求时首屏拿到的确实是对的；等游标推进发出 <c>pn=1</c>，
    /// 拿回来的还是第 1 页，追加进去正好把整个列表翻倍（实测 28 张专辑显示成 56 张，每个歌手都如此）。
    /// </para>
    /// <para>只断言首屏 <c>pn=1</c> 也够用：写错成 0 基时首屏是 <c>pn=0</c>，这里就会红。</para>
    /// </remarks>
    [Fact]
    public async Task ArtistAlbums_CountsPagesFromOne()
    {
        RespondWith("artist-336-album.json");

        var cursor = new PagedCursor(PagingConvention.OneBased, 5);

        await _api.GetArtistAlbumsAsync(ArtistId, cursor, Ct);

        Assert.Contains("service/artist/album/336?", _handler.LastRequest.Url);
        Assert.Contains("?pn=1&", _handler.LastRequest.Url);

        await _api.GetArtistAlbumsAsync(ArtistId, cursor, Ct);

        Assert.Contains("?pn=2&", _handler.LastRequest.Url);
    }

    /// <summary>歌手歌曲同专辑：从 <c>pn=1</c> 起。</summary>
    [Fact]
    public async Task ArtistTracks_CountsPagesFromOne()
    {
        RespondWith("artist-336-music.json");

        var cursor = new PagedCursor(PagingConvention.OneBased, 5);

        await _api.GetArtistTracksAsync(ArtistId, cursor, Ct);

        Assert.Contains("service/artist/music/336?", _handler.LastRequest.Url);
        Assert.Contains("?pn=1&", _handler.LastRequest.Url);

        await _api.GetArtistTracksAsync(ArtistId, cursor, Ct);

        Assert.Contains("?pn=2&", _handler.LastRequest.Url);
    }

    /// <summary>
    /// 越界页号回的是 <c>data: {}</c>，按空列表处理 —— 不能让它被判成「还有下一页」。
    /// </summary>
    /// <remarks>实测 <c>artist/album/336</c> 配 <c>rn=30</c> 的第 3 页（总 49 张）。</remarks>
    [Fact]
    public async Task ArtistAlbums_PastTheEnd_ReturnsEmptyPage()
    {
        _handler.Responder = _ => ReplayHandler.Json(
            """{"code":200,"msg":"success","data":{}}""");

        var page = await _api.GetArtistAlbumsAsync(
            ArtistId, new PagedCursor(PagingConvention.OneBased, 30), Ct);

        Assert.Empty(page.Items);
    }
}
