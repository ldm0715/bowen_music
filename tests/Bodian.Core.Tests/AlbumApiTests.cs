using System.Net.Http;
using Bodian.Core.Api;
using Bodian.Core.Api.Paging;
using Bodian.Core.Tests.Support;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 已购与收藏的专辑。响应全部来自 <c>fixtures/</c>，零真实网络。
/// </summary>
/// <remarks>
/// <b>这三个 fixture 的证据强度不一样，别一视同仁</b>：
/// <list type="bullet">
/// <item><c>purchased-singles.json</c> —— 条目是真的（曲目对象来自本机缓存里的真实响应），
/// 只把 <c>payInfo</c> 按 <c>SongData</c> 的真实形态去掉。</item>
/// <item><c>purchased-albums.json</c> —— **条目是照解包出来的键构造的，没有真实样本**。</item>
/// <item><c>collected-albums.json</c> —— 同上，而且<b>连数组键与 source 取值都是推断</b>。</item>
/// </list>
/// 所以这些测试守的是「解析逻辑与信封形状一致」，**不能证明服务端真的这么回**。
/// 见 <c>reverse/findings/06-library-api.md</c> §12。
/// </remarks>
public sealed class AlbumApiTests : IDisposable
{
    private const string Uid = "50303440";

    private readonly ReplayHandler _handler = new();
    private readonly BodianSession _session = BodianSession.CreateAnonymous();
    private readonly BodianHttpTransport _transport;
    private readonly BodianApi _api;

    public AlbumApiTests()
    {
        _transport = new BodianHttpTransport(
            _handler,
            new BodianTransportOptions(),
            _session,
            new FakeDeviceIdentity(),
            FixedTimeProvider.Golden);

        _api = new BodianApi(_transport, _session, new FakeDeviceIdentity());

        _session.Set(Uid, "test-token");
    }

    public void Dispose() => _transport.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private void RespondWith(string fixture) =>
        _handler.Responder = _ => ReplayHandler.Json(Fixtures.Read(fixture));

    private static PagedCursor NewCursor() => new(PagingConvention.OneBased, 30);

    // ── 已购单曲 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task PurchasedSingles_MapsRealEnvelope()
    {
        RespondWith("purchased-singles.json");

        var page = await _api.GetPurchasedSinglesAsync(NewCursor(), Ct);

        Assert.Equal(3, page.Items.Count);

        var first = page.Items[0];

        Assert.True(first.Id > 0);
        Assert.False(string.IsNullOrWhiteSpace(first.Title));
        Assert.True(first.Duration > TimeSpan.Zero);
    }

    /// <summary>
    /// 已购单曲的响应里**没有 <c>payInfo</c>**，所以付费标记必须是「不显示」而不是崩掉。
    /// </summary>
    /// <remarks>
    /// 曲目对象的 <c>fromJson</c>（<c>SongData</c>）根本不读这个键 —— 它是通用曲目字段，
    /// 不是购买记录字段。fixture 按真实形态去掉了 <c>payInfo</c>，这条守的就是那个分支。
    /// </remarks>
    [Fact]
    public async Task PurchasedSingles_WithoutPayInfo_HasNoPayFlags()
    {
        RespondWith("purchased-singles.json");

        var page = await _api.GetPurchasedSinglesAsync(NewCursor(), Ct);

        Assert.All(page.Items, track =>
        {
            Assert.False(track.RequiresVip);
            Assert.False(track.RequiresPurchase);
        });
    }

    /// <summary>总数键是 <c>size</c>，不是 <c>total</c>。</summary>
    [Fact]
    public async Task PurchasedSingles_ReadsSizeAsTotal()
    {
        RespondWith("purchased-singles.json");

        var page = await _api.GetPurchasedSinglesAsync(NewCursor(), Ct);

        Assert.Equal(3, page.Total);
    }

    [Fact]
    public async Task PurchasedSingles_SendsOneBasedPaging()
    {
        RespondWith("purchased-singles.json");

        await _api.GetPurchasedSinglesAsync(NewCursor(), Ct);

        var url = _handler.LastRequest.Url;

        Assert.Contains("ucenter/pay/album/music/purchasedList?", url);
        Assert.Contains("pn=1", url);
        Assert.Contains("rn=30", url);
    }

    // ── 已购专辑 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task PurchasedAlbums_MapsAlbumList()
    {
        RespondWith("purchased-albums.json");

        var page = await _api.GetPurchasedAlbumsAsync(NewCursor(), Ct);

        Assert.Equal(2, page.Total);

        var first = Assert.Single(page.Items.Take(1));

        Assert.Equal(1293, first.Id);
        Assert.Equal("叶惠美", first.Name);
        Assert.Equal("周杰伦", first.ArtistText);
        Assert.Equal(11, first.MusicCount);
        Assert.NotNull(first.CoverImage);
    }

    [Fact]
    public async Task PurchasedAlbums_SendsOneBasedPaging()
    {
        RespondWith("purchased-albums.json");

        await _api.GetPurchasedAlbumsAsync(NewCursor(), Ct);

        Assert.Contains("ucenter/pay/album/purchasedList?pn=1&rn=30", _handler.LastRequest.Url);
    }

    // ── 收藏的专辑 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 打的是 <c>service/collect/4/list</c> —— 移动端的「收藏歌单」端点，本页的数据源。
    /// </summary>
    /// <remarks>
    /// 官方桌面端自己那条路（<c>service/collect/6/list</c>）已弃用：实测它回 200 但
    /// <c>data</c> 是空对象。桌面端二进制里的 <c>service/collect/2</c> 实测 **404**，
    /// 是 Dart 对象池相邻常量粘连的假阳性。
    /// </remarks>
    [Fact]
    public async Task CollectedAlbums_UsesMobileCollectPlaylistEndpoint()
    {
        RespondWith("collected-albums.json");

        await _api.GetCollectedAlbumsAsync(NewCursor(), Ct);

        Assert.Single(_handler.Requests);
        Assert.Contains("service/collect/4/list?", _handler.LastRequest.Url);
    }

    /// <summary>读的是 <c>playLists</c> 键，不是别的。</summary>
    [Fact]
    public async Task CollectedAlbums_ReadsPlayListsKey()
    {
        RespondWith("collected-albums.json");

        var page = await _api.GetCollectedAlbumsAsync(NewCursor(), Ct);

        Assert.Equal(2, page.Total);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal("仙剑奇侠传三电视剧原声带", page.Items[0].Name);
    }

    [Fact]
    public async Task CollectedAlbums_SendsUserIdAndFromUid()
    {
        RespondWith("collected-albums.json");

        await _api.GetCollectedAlbumsAsync(NewCursor(), Ct);

        var url = _handler.LastRequest.Url;

        Assert.Contains($"userId={Uid}", url);
        Assert.Contains($"fromUid={Uid}", url);
        Assert.Contains("pn=1", url);
        Assert.Contains("rn=30", url);
    }

    /// <summary>
    /// 桌面端的归一化形状：主键是 <c>id</c> 不是 <c>albumId</c>，艺人在 <c>creatorName</c> 里。
    /// </summary>
    /// <remarks>
    /// 这个形状不是猜的 —— 它取自官方桌面端自己的缓存 <c>hist/histAlbumList.json</c>，
    /// 那里收藏专辑的 <c>sourceType</c> 就是 <c>6</c>。
    /// </remarks>
    [Fact]
    public async Task CollectedAlbums_MapsDesktopNormalizedShape()
    {
        RespondWith("collected-albums.json");

        var page = await _api.GetCollectedAlbumsAsync(NewCursor(), Ct);

        Assert.Equal(2, page.Items.Count);

        var first = page.Items[0];

        Assert.Equal(62694324, first.Id);
        Assert.Equal("仙剑奇侠传三电视剧原声带", first.Name);
        Assert.Equal("麦振鸿", first.ArtistText);
        Assert.Equal(12, first.MusicCount);
        Assert.NotNull(first.CoverImage);
    }

    /// <summary>
    /// 条目是**专辑形状**（<c>albumId</c> + <c>artist</c>）时也要能解析。
    /// </summary>
    /// <remarks>
    /// 桌面端有 <c>parseJsonFromAlbumToSongList</c> —— 它把专辑 JSON **解析成**歌单模型，
    /// 所以线上回的原始条目大概长这样，而 <c>histAlbumList.json</c> 里那个歌单形状是转换后的结果。
    /// 两种都要收，见 <c>AlbumDto.EffectiveId</c>。
    /// </remarks>
    [Fact]
    public async Task CollectedAlbums_AcceptsAlbumShapedItems()
    {
        _handler.Responder = _ => ReplayHandler.Json(
            """
            {"code":200,"msg":"success","data":{"total":1,
              "playLists":[{"albumId":1293,"name":"叶惠美","pic":"https://img4.kuwo.cn/a.jpg",
                            "artist":"周杰伦","musicCount":11}]}}
            """);

        var page = await _api.GetCollectedAlbumsAsync(NewCursor(), Ct);

        var album = Assert.Single(page.Items);

        Assert.Equal(1293, album.Id);
        Assert.Equal("叶惠美", album.Name);
        Assert.Equal("周杰伦", album.ArtistText);
    }

    /// <summary>
    /// 响应里**没有 <c>favAlbumList</c> 键**时返回空列表 —— 那不等于「没有收藏专辑」。
    /// </summary>
    /// <remarks>
    /// 这条守的是「形状变了」与「确实为空」在 API 层的区别：两者都返回空列表，
    /// 但前者会打进一条 Warning（服务端形状可能变了），界面上的文案也把两种可能都说了。
    /// </remarks>
    [Fact]
    public async Task CollectedAlbums_MissingKey_IsEmpty()
    {
        _handler.Responder = _ => ReplayHandler.Json(
            """{"code":200,"msg":"success","data":{"total":5,"albums":[{"id":1}]}}""");

        var page = await _api.GetCollectedAlbumsAsync(NewCursor(), Ct);

        Assert.Empty(page.Items);
    }

    /// <summary>返回空列表时游标到底，不该再请求下一页。</summary>
    [Fact]
    public async Task EmptyAlbumList_ExhaustsCursor()
    {
        _handler.Responder = _ => ReplayHandler.Json(
            """{"code":200,"msg":"success","data":{"total":0,"albumList":[]}}""");

        var cursor = NewCursor();
        var page = await _api.GetCollectedAlbumsAsync(cursor, Ct);

        Assert.Empty(page.Items);
        Assert.True(cursor.Exhausted);
    }

    // ── 前置条件 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnonymousSession_Throws()
    {
        _session.Clear();

        await Assert.ThrowsAsync<BodianNotSignedInException>(
            () => _api.GetPurchasedSinglesAsync(NewCursor(), Ct));
        await Assert.ThrowsAsync<BodianNotSignedInException>(
            () => _api.GetPurchasedAlbumsAsync(NewCursor(), Ct));
        await Assert.ThrowsAsync<BodianNotSignedInException>(
            () => _api.GetCollectedAlbumsAsync(NewCursor(), Ct));
    }

    [Fact]
    public async Task NullCursor_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _api.GetPurchasedSinglesAsync(null!, Ct));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _api.GetPurchasedAlbumsAsync(null!, Ct));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _api.GetCollectedAlbumsAsync(null!, Ct));
    }
}
