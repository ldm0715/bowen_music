using System.Text.Json;
using Bodian.Core.Api;
using Bodian.Core.Api.Paging;
using Bodian.Core.Tests.Support;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 收藏歌单 / 关注歌手的读写报文形状。零真实网络。
/// </summary>
/// <remarks>
/// 路径、body 与 <c>op</c> 方向来自 <c>reverse/findings/13-collect-playlist-follow-artist.md</c>
/// （静态反汇编 + 真机往返双证）。这里断言的是**发出去的报文**，不是服务端行为。
/// </remarks>
public sealed class CollectionApiTests : IDisposable
{
    private const string Uid = "36828743";
    private const long PlaylistId = 3676986117;

    private readonly ReplayHandler _handler = new();
    private readonly BodianSession _session = BodianSession.CreateAnonymous();
    private readonly BodianHttpTransport _transport;
    private readonly BodianApi _api;

    public CollectionApiTests()
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

    private static PagedCursor NewCursor() => new(PagingConvention.OneBased);

    // ── 收藏歌单写入 ────────────────────────────────────────────────────────

    /// <summary>收藏：<c>op=1</c>，报文里**没有 token**。</summary>
    [Fact]
    public async Task CollectPlaylist_PostsOp1WithoutToken()
    {
        _session.Set(Uid, "test-token");
        _handler.Responder = _ => ReplayHandler.Json("""{"code":200,"msg":"success","data":{}}""");

        await _api.SetPlaylistCollectedAsync(PlaylistId, 4, collected: true, Ct);

        var request = _handler.LastRequest;
        Assert.Equal("POST", request.Method);
        Assert.Contains("service/collect?", request.Url, StringComparison.Ordinal);

        var body = JsonDocument.Parse(request.Body!).RootElement;
        Assert.Equal(4, body.GetProperty("source").GetInt32());
        Assert.Equal(1, body.GetProperty("op").GetInt32());
        Assert.Equal(PlaylistId, body.GetProperty("sourceId")[0].GetInt64());
        Assert.Equal(long.Parse(Uid), body.GetProperty("uid").GetInt64());
        Assert.False(body.TryGetProperty("token", out _));
    }

    /// <summary>取消收藏：<c>op=2</c>。</summary>
    [Fact]
    public async Task CollectPlaylist_UncollectUsesOp2()
    {
        _session.Set(Uid, "test-token");
        _handler.Responder = _ => ReplayHandler.Json("""{"code":200,"msg":"success","data":{}}""");

        await _api.SetPlaylistCollectedAsync(PlaylistId, 4, collected: false, Ct);

        var body = JsonDocument.Parse(_handler.LastRequest.Body!).RootElement;
        Assert.Equal(2, body.GetProperty("op").GetInt32());
    }

    /// <summary><c>sourceId</c> 必须是数组 —— 传标量服务端回 400（findings/01 §4）。</summary>
    [Fact]
    public async Task CollectPlaylist_SendsSourceIdAsArray()
    {
        _session.Set(Uid, "test-token");
        _handler.Responder = _ => ReplayHandler.Json("""{"code":200,"msg":"success","data":{}}""");

        await _api.SetPlaylistCollectedAsync(PlaylistId, 4, collected: true, Ct);

        var body = JsonDocument.Parse(_handler.LastRequest.Body!).RootElement;
        Assert.Equal(JsonValueKind.Array, body.GetProperty("sourceId").ValueKind);
    }

    // ── 关注歌手写入 ────────────────────────────────────────────────────────

    /// <summary>关注：<c>source=7</c>，报文里**多一个 token**。</summary>
    [Fact]
    public async Task FollowArtist_PostsSource7WithToken()
    {
        _session.Set(Uid, "test-token");
        _handler.Responder = _ => ReplayHandler.Json("""{"code":200,"msg":"success","data":{}}""");

        await _api.SetArtistFollowedAsync(artistId: 1306, followed: true, Ct);

        var body = JsonDocument.Parse(_handler.LastRequest.Body!).RootElement;
        Assert.Equal(7, body.GetProperty("source").GetInt32());
        Assert.Equal(1, body.GetProperty("op").GetInt32());
        Assert.Equal(1306, body.GetProperty("sourceId")[0].GetInt64());
        Assert.Equal("test-token", body.GetProperty("token").GetString());
    }

    [Fact]
    public async Task FollowArtist_UnfollowUsesOp2()
    {
        _session.Set(Uid, "test-token");
        _handler.Responder = _ => ReplayHandler.Json("""{"code":200,"msg":"success","data":{}}""");

        await _api.SetArtistFollowedAsync(artistId: 1306, followed: false, Ct);

        var body = JsonDocument.Parse(_handler.LastRequest.Body!).RootElement;
        Assert.Equal(2, body.GetProperty("op").GetInt32());
    }

    [Fact]
    public async Task Writes_AreSignedAndRequireLogin()
    {
        _session.Set(Uid, "test-token");
        _handler.Responder = _ => ReplayHandler.Json("""{"code":200,"msg":"success","data":{}}""");

        await _api.SetPlaylistCollectedAsync(PlaylistId, 4, collected: true, Ct);

        Assert.Contains("sign=", _handler.LastRequest.Url, StringComparison.Ordinal);
        Assert.Contains("timestamp=", _handler.LastRequest.Url, StringComparison.Ordinal);

        var anonymous = BodianSession.CreateAnonymous();
        using var transport = new BodianHttpTransport(
            _handler, new BodianTransportOptions(), anonymous, new FakeDeviceIdentity(), FixedTimeProvider.Golden);
        var api = new BodianApi(transport, anonymous, new FakeDeviceIdentity());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => api.SetPlaylistCollectedAsync(PlaylistId, 4, collected: true, Ct));
    }

    // ── 歌单详情：元数据 + 收藏态 ──────────────────────────────────────────
    //
    // 详情响应把头部要用的东西与「是否已收藏」放在同一份 JSON 里，
    // 所以读接口只有一个：GetPlaylistInfoAsync。

    /// <summary>详情里有 <c>collectTime</c> = 已收藏。</summary>
    [Fact]
    public async Task PlaylistInfo_IsCollectedWhenCollectTimePresent()
    {
        _session.Set(Uid, "test-token");
        _handler.Responder = _ => ReplayHandler.Json(
            """{"code":200,"data":{"id":2867496601,"name":"x","collectTime":"2026-10-03 10:45:12"}}""");

        var info = await _api.GetPlaylistInfoAsync(2867496601, 4, Ct);

        Assert.NotNull(info);
        Assert.True(info.IsCollected);
        Assert.Contains("service/playlist/info/2867496601?", _handler.LastRequest.Url, StringComparison.Ordinal);
        Assert.Contains("source=4", _handler.LastRequest.Url, StringComparison.Ordinal);
    }

    /// <summary>没有这个键 = 未收藏。<b>不是 <c>isFond</c></b>，见 findings/13 §3.3。</summary>
    [Fact]
    public async Task PlaylistInfo_NotCollectedWhenCollectTimeAbsent()
    {
        _session.Set(Uid, "test-token");
        _handler.Responder = _ => ReplayHandler.Json(
            """{"code":200,"data":{"id":3676986117,"name":"x","isFond":0}}""");

        var info = await _api.GetPlaylistInfoAsync(3676986117, 4, Ct);

        Assert.NotNull(info);
        Assert.False(info.IsCollected);
    }

    /// <summary>
    /// 匿名**照发请求** —— 与它取代的「查收藏态」相反。
    /// </summary>
    /// <remarks>
    /// 头部元数据（创建者、简介、播放数）是公开信息，匿名用户也该看得到；
    /// 匿名时只是 <c>collectTime</c> 不出现，<c>IsCollected</c> 落成 false。
    /// </remarks>
    [Fact]
    public async Task PlaylistInfo_IsStillFetchedWhenAnonymous()
    {
        _handler.Responder = _ => ReplayHandler.Json(
            """{"code":200,"data":{"id":2867496601,"name":"x","creatorId":182253281,"playNum":5657990}}""");

        var info = await _api.GetPlaylistInfoAsync(2867496601, 4, Ct);

        Assert.NotNull(info);
        Assert.False(info.IsCollected);
        Assert.Equal(182253281, info.CreatorId);
        Assert.Equal(5657990, info.PlayCount);
        Assert.NotEmpty(_handler.Requests);
    }

    /// <summary>
    /// <c>data</c> 是空对象时返回 <c>null</c>，**不抛**。
    /// </summary>
    /// <remarks>
    /// <c>source</c> 填错与「这个来源没有这个歌单」服务端都回同一个空对象、都是 <c>code 200</c>，
    /// 所以只能当「查不到」处理（文档 2.2）。
    /// </remarks>
    [Fact]
    public async Task PlaylistInfo_NullWhenDataIsEmptyObject()
    {
        _session.Set(Uid, "test-token");
        _handler.Responder = _ => ReplayHandler.Json("""{"code":200,"msg":"success","data":{}}""");

        Assert.Null(await _api.GetPlaylistInfoAsync(PlaylistId, 4, Ct));
    }

    /// <summary><c>source</c> 原样进 query —— 发现页的歌单不是 4，写死会让详情直接变空。</summary>
    [Fact]
    public async Task PlaylistInfo_SendsTheCallerSource()
    {
        _session.Set(Uid, "test-token");
        _handler.Responder = _ => ReplayHandler.Json("""{"code":200,"data":{"id":1,"name":"x"}}""");

        await _api.GetPlaylistInfoAsync(1, 13, Ct);

        Assert.Contains("source=13", _handler.LastRequest.Url, StringComparison.Ordinal);
    }

    // ── 收藏歌单列表：混合列表按 sourceType 过滤 ────────────────────────────

    /// <summary>只收 <c>sourceType == 4</c> 的歌单，同一条响应里的专辑（6）要滤掉。</summary>
    [Fact]
    public async Task CollectedPlaylists_KeepsOnlySourceType4()
    {
        _session.Set(Uid, "test-token");
        _handler.Responder = _ => ReplayHandler.Json(
            """
            {"code":200,"data":{"total":3,"playLists":[
              {"id":2867496601,"name":"周杰伦歌单","musicCount":177,"sourceType":4},
              {"albumId":36994,"name":"仙剑原声带","musicCount":14,"sourceType":6},
              {"id":3676986117,"name":"热梗合集","musicCount":133,"sourceType":4}]}}
            """);

        var page = await _api.GetCollectedPlaylistsAsync(NewCursor(), Ct);

        Assert.Equal(2, page.Items.Count);
        Assert.Equal([2867496601L, 3676986117L], page.Items.Select(p => p.Id));
        Assert.Contains("service/collect/4/list?", _handler.LastRequest.Url, StringComparison.Ordinal);
    }

    /// <summary>响应里没有 <c>playLists</c> 键时返回空列表，不抛。</summary>
    [Fact]
    public async Task CollectedPlaylists_MissingKey_IsEmpty()
    {
        _session.Set(Uid, "test-token");
        _handler.Responder = _ => ReplayHandler.Json("""{"code":200,"data":{}}""");

        var page = await _api.GetCollectedPlaylistsAsync(NewCursor(), Ct);

        Assert.Empty(page.Items);
    }

    // ── 关注歌手列表 ────────────────────────────────────────────────────────

    [Fact]
    public async Task FollowedArtists_MapsArtistList()
    {
        _session.Set(Uid, "test-token");
        _handler.Responder = _ => ReplayHandler.Json(
            """
            {"code":200,"data":{"total":1,"artistList":[
              {"id":1306,"name":"王心凌","aliasName":"Cyndi Wang",
               "pic":"https://img3.kuwo.cn/x.jpg","fansCnt":159778,"musicCnt":583,"albumCnt":43}]}}
            """);

        var artists = await _api.GetFollowedArtistsAsync(Ct);

        var artist = Assert.Single(artists);
        Assert.Equal(1306, artist.Id);
        Assert.Equal("王心凌", artist.Name);
        Assert.Equal("Cyndi Wang", artist.AliasName);
        Assert.Equal(159778, artist.FansCount);
        Assert.Equal(583, artist.SongCount);
        Assert.Contains("service/collect/7/list?", _handler.LastRequest.Url, StringComparison.Ordinal);
        Assert.Contains("rn=400", _handler.LastRequest.Url, StringComparison.Ordinal);
    }

    // ── 收藏专辑：source 是 6，不是 4 ────────────────────────────────────────

    /// <summary>专辑用 <c>source=6</c>（歌单是 4），同样不带 token。</summary>
    [Fact]
    public async Task CollectAlbum_UsesSource6()
    {
        _session.Set(Uid, "test-token");
        _handler.Responder = _ => ReplayHandler.Json("""{"code":200,"msg":"success","data":{}}""");

        await _api.SetAlbumCollectedAsync(albumId: 1293, collected: true, Ct);

        var body = JsonDocument.Parse(_handler.LastRequest.Body!).RootElement;
        Assert.Equal(6, body.GetProperty("source").GetInt32());
        Assert.Equal(1, body.GetProperty("op").GetInt32());
        Assert.Equal(1293, body.GetProperty("sourceId")[0].GetInt64());
        Assert.False(body.TryGetProperty("token", out _));
    }

    [Fact]
    public async Task UncollectAlbum_UsesOp2()
    {
        _session.Set(Uid, "test-token");
        _handler.Responder = _ => ReplayHandler.Json("""{"code":200,"msg":"success","data":{}}""");

        await _api.SetAlbumCollectedAsync(albumId: 1293, collected: false, Ct);

        var body = JsonDocument.Parse(_handler.LastRequest.Body!).RootElement;
        Assert.Equal(2, body.GetProperty("op").GetInt32());
    }

    /// <summary>专辑的收藏态走 <c>multipleState?source=6</c>（详情里没有标志）。</summary>
    [Fact]
    public async Task IsAlbumCollected_ReadsMultipleState()
    {
        _session.Set(Uid, "test-token");
        _handler.Responder = _ => ReplayHandler.Json(
            """{"code":200,"data":{"result":[{"id":1293,"collect":true}]}}""");

        var collected = await _api.IsAlbumCollectedAsync(1293, Ct);

        Assert.True(collected);
        Assert.Contains("service/collect/multipleState?", _handler.LastRequest.Url, StringComparison.Ordinal);
        Assert.Contains("source=6", _handler.LastRequest.Url, StringComparison.Ordinal);
        Assert.Contains("sourceIds=1293", _handler.LastRequest.Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IsAlbumCollected_FalseWhenNotCollected()
    {
        _session.Set(Uid, "test-token");
        _handler.Responder = _ => ReplayHandler.Json(
            """{"code":200,"data":{"result":[{"id":1293,"collect":false}]}}""");

        Assert.False(await _api.IsAlbumCollectedAsync(1293, Ct));
    }

    /// <summary>响应里没回这个 id 时按「无法判定」处理，不要当成未收藏。</summary>
    [Fact]
    public async Task IsAlbumCollected_NullWhenIdMissingFromResult()
    {
        _session.Set(Uid, "test-token");
        _handler.Responder = _ => ReplayHandler.Json("""{"code":200,"data":{"result":[]}}""");

        Assert.Null(await _api.IsAlbumCollectedAsync(1293, Ct));
    }

    [Fact]
    public async Task IsAlbumCollected_NullWhenAnonymous()
    {
        _handler.Responder = _ => ReplayHandler.Json("""{"code":200,"data":{}}""");

        Assert.Null(await _api.IsAlbumCollectedAsync(1293, Ct));
        Assert.Empty(_handler.Requests);
    }
}
