using Bodian.Core.Api;
using Bodian.Core.Api.Paging;
using Bodian.Core.Tests.Support;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 曲库（歌单）门面。响应全部来自 <c>fixtures/</c>，零真实网络。
/// </summary>
/// <remarks>
/// <para>
/// <b>这些 fixture 的来源要说清楚</b>：条目的字段与类型取自官方 PC 客户端本机缓存里
/// 真实下发的对象（已脱敏），**信封键**（<c>playLists</c> / <c>list</c> / <c>total</c>）
/// 来自反编译 Dart 的解析代码。见 <c>reverse/findings/06-library-api.md</c> §11。
/// 它们**不是**一次捕获的完整响应 —— 这是本轮唯一的证据缺口。
/// </para>
/// <para>
/// <b>账号曲库</b>那几个接口要求登录，所以这里的会话默认是**登录态**的（基类默认是匿名）；
/// 公开歌单的曲目匿名也能取，那两条用例自己把会话清掉来验，见 <c>AnonymousSession_*</c>。
/// </para>
/// </remarks>
public sealed class PlaylistApiTests : IDisposable
{
    private const string Uid = "50303440";

    /// <summary>账号歌单（自建与「我喜欢」）的 <c>source</c>。</summary>
    private const int AccountSource = 5;

    private readonly ReplayHandler _handler = new();
    private readonly BodianSession _session = BodianSession.CreateAnonymous();
    private readonly BodianHttpTransport _transport;
    private readonly BodianApi _api;

    public PlaylistApiTests()
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

    // ── 自建歌单 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreatedPlaylists_MapsRealEnvelope()
    {
        RespondWith("playlists-userCreate.json");

        var playlists = await _api.GetCreatedPlaylistsAsync(Ct);

        Assert.Equal(3, playlists.Count);

        var first = playlists[0];

        Assert.True(first.Id > 0);
        Assert.False(string.IsNullOrWhiteSpace(first.Name));
        Assert.Equal(11, first.MusicCount);
        Assert.NotNull(first.CoverImage);
    }

    /// <summary>
    /// 信封是 <c>{playLists, total}</c>，不是裸数组。
    /// </summary>
    /// <remarks>
    /// 这条守着「数组键读错」：读成裸数组或读成 <c>list</c> 都会得到空列表而不是异常，
    /// 界面上表现为「侧栏一个歌单都没有」——那是本项目最难查的一类现象。
    /// </remarks>
    [Fact]
    public async Task CreatedPlaylists_ReadsThePlayListsKey()
    {
        RespondWith("playlists-userCreate.json");

        var playlists = await _api.GetCreatedPlaylistsAsync(Ct);

        Assert.NotEmpty(playlists);
    }

    [Fact]
    public async Task CreatedPlaylists_SendsUserId()
    {
        RespondWith("playlists-userCreate.json");

        await _api.GetCreatedPlaylistsAsync(Ct);

        Assert.Contains($"service/playlist/userCreate?userId={Uid}", _handler.LastRequest.Url);
    }

    /// <summary>隐私标记来自 <c>isPrivate</c>，服务端给的是数字不是布尔。</summary>
    [Fact]
    public async Task CreatedPlaylists_MapsPrivateFlag()
    {
        const string json = """{"code":200,"msg":"success","data":{"total":2,"playLists":[{"id":1,"name":"私密","isPrivate":1},{"id":2,"name":"公开","isPrivate":0}]}}""";

        _handler.Responder = _ => ReplayHandler.Json(json);

        var playlists = await _api.GetCreatedPlaylistsAsync(Ct);

        Assert.True(playlists[0].IsPrivate);
        Assert.False(playlists[1].IsPrivate);
    }

    // ── 我喜欢 ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task LikedPlaylist_MapsSingleObject()
    {
        RespondWith("playlist-fond.json");

        var playlist = await _api.GetLikedPlaylistAsync(Ct);

        Assert.NotNull(playlist);
        Assert.Equal(99980832, playlist.Id);
        Assert.Equal(5, playlist.MusicCount);
    }

    /// <summary>
    /// 账号没有红心歌单时响应里没有 <c>id</c> —— 那是正常结果，返回 <c>null</c> 而不是空壳。
    /// </summary>
    [Fact]
    public async Task LikedPlaylist_WithoutId_IsNull()
    {
        _handler.Responder = _ => ReplayHandler.Json("""{"code":200,"msg":"success","data":{}}""");

        Assert.Null(await _api.GetLikedPlaylistAsync(Ct));
    }

    [Fact]
    public async Task LikedPlaylist_SendsUserId()
    {
        RespondWith("playlist-fond.json");

        await _api.GetLikedPlaylistAsync(Ct);

        Assert.Contains($"service/playlist/fond?userId={Uid}", _handler.LastRequest.Url);
    }

    // ── 歌单曲目 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task PlaylistTracks_MapsRealEnvelope()
    {
        RespondWith("playlist-tracks.json");

        var page = await _api.GetPlaylistTracksAsync(
            99980832,
            AccountSource,
            new PagedCursor(PagingConvention.OneBased, 30),
            Ct);

        Assert.NotEmpty(page.Items);

        var first = page.Items[0];

        Assert.True(first.Id > 0);
        Assert.False(string.IsNullOrWhiteSpace(first.Title));
    }

    /// <summary>
    /// 自建歌单与「我喜欢」共用 <c>source=5</c>，且页号从 1 开始。
    /// </summary>
    [Fact]
    public async Task PlaylistTracks_SendsSourceFiveAndOneBasedPaging()
    {
        RespondWith("playlist-tracks.json");

        await _api.GetPlaylistTracksAsync(99980832, AccountSource, new PagedCursor(PagingConvention.OneBased, 30), Ct);

        var url = _handler.LastRequest.Url;

        Assert.Contains("service/playlist/99980832/musicList?", url);
        Assert.Contains("pn=1", url);
        Assert.Contains("rn=30", url);
        Assert.Contains("source=5", url);
    }

    [Fact]
    public async Task PlaylistTracks_SecondPageAdvancesPageNumber()
    {
        RespondWith("playlist-tracks.json");

        var cursor = new PagedCursor(PagingConvention.OneBased, 30);

        await _api.GetPlaylistTracksAsync(99980832, AccountSource, cursor, Ct);
        await _api.GetPlaylistTracksAsync(99980832, AccountSource, cursor, Ct);

        Assert.Contains("pn=2", _handler.LastRequest.Url);
    }

    /// <summary>响应里给的是空列表时游标就到底了，不该再请求下一页。</summary>
    [Fact]
    public async Task PlaylistTracks_EmptyPage_ExhaustsCursor()
    {
        _handler.Responder = _ => ReplayHandler.Json(
            """{"code":200,"msg":"success","data":{"total":0,"list":[]}}""");

        var cursor = new PagedCursor(PagingConvention.OneBased, 30);
        var page = await _api.GetPlaylistTracksAsync(99980832, AccountSource, cursor, Ct);

        Assert.Empty(page.Items);
        Assert.True(cursor.Exhausted);
    }

    // ── 前置条件 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 未登录时这三个接口都抛，**不返回空列表**。
    /// </summary>
    /// <remarks>
    /// 返回空会让「侧栏一片空白」看起来像服务端没数据，而真正的原因是没有会话。
    /// 第三个用的是 <c>source=5</c>（账号歌单）—— 公开歌单不在此列，见下面那条用例。
    /// </remarks>
    [Fact]
    public async Task AnonymousSession_Throws()
    {
        _session.Clear();

        await Assert.ThrowsAsync<BodianNotSignedInException>(() => _api.GetCreatedPlaylistsAsync(Ct));
        await Assert.ThrowsAsync<BodianNotSignedInException>(() => _api.GetLikedPlaylistAsync(Ct));
        await Assert.ThrowsAsync<BodianNotSignedInException>(
            () => _api.GetPlaylistTracksAsync(1, AccountSource, new PagedCursor(PagingConvention.OneBased), Ct));
    }

    /// <summary>
    /// <b>公开歌单的曲目匿名就能取</b> —— 这是匿名浏览的前提之一。
    /// </summary>
    /// <remarks>
    /// 2026-10-08 探针实测（本机无会话，即 <c>uid=-1</c>）：
    /// <c>source=4</c> 返回 134 首、<c>source=13</c> 返回 27 首，都能取；
    /// 只有 <c>source=5</c>（账号歌单）拿不到。见 <c>reverse/findings/06-library-api.md</c>。
    /// <para>
    /// 早先这里对所有 <c>source</c> 一律拦下，后果是发现页里点进任何歌单都显示「加载失败」——
    /// 挡掉的是服务端本来就允许的东西。
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(4)]
    [InlineData(13)]
    public async Task AnonymousSession_CanStillFetchPublicPlaylistTracks(int source)
    {
        _session.Clear();
        RespondWith("playlist-tracks.json");

        var page = await _api.GetPlaylistTracksAsync(
            99980832, source, new PagedCursor(PagingConvention.OneBased, 30), Ct);

        Assert.NotEmpty(page.Items);
    }

    /// <summary>账号歌单（<c>source=5</c>）匿名取不到，仍然要拦 —— 拦下来是为了不让界面显示成「这个歌单是空的」。</summary>
    [Fact]
    public async Task AnonymousSession_StillRefusesAccountPlaylistTracks()
    {
        _session.Clear();

        await Assert.ThrowsAsync<BodianNotSignedInException>(
            () => _api.GetPlaylistTracksAsync(1, AccountSource, new PagedCursor(PagingConvention.OneBased), Ct));
    }

    [Fact]
    public async Task PlaylistTracks_NonPositiveId_Throws()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _api.GetPlaylistTracksAsync(0, AccountSource, new PagedCursor(PagingConvention.OneBased), Ct));
    }

    [Fact]
    public async Task PlaylistTracks_NullCursor_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _api.GetPlaylistTracksAsync(1, AccountSource, null!, Ct));
    }

    // ── 歌单详情（service/playlist/info/{id}）────────────────────────────────
    //
    // 样本是两个真机响应：playlist-info-collected.json（已收藏）与
    // playlist-info-not-collected.json（未收藏）。详情返回的**扁平对象有 15 个字段**，
    // 头部要用的创建者、简介、播放数、收藏数全在里面。

    /// <summary>已收藏的那份：15 个字段逐项落在模型上。</summary>
    [Fact]
    public async Task PlaylistInfo_MapsTheFullRealResponse()
    {
        RespondWith("playlist-info-collected.json");

        var info = await _api.GetPlaylistInfoAsync(2867496601, 4, Ct);

        Assert.NotNull(info);
        Assert.Equal(2867496601, info.Id);
        Assert.Equal("终于等到周杰伦，说好不哭你今天哭了吗？", info.Name);
        Assert.Equal(177, info.MusicCount);
        Assert.Equal(4, info.SourceType);

        // 头部专属字段：列表来源一个都给不出来。
        Assert.Equal(182253281, info.CreatorId);
        Assert.Equal("adbcfdc", info.CreatorName);
        Assert.NotNull(info.CreatorCover);
        Assert.Equal(5657990, info.PlayCount);
        Assert.Equal(21315, info.CollectedCount);
        Assert.True(info.HasDescription);
        Assert.True(info.HasCreator);

        // 个人态：collectTime 存在即已收藏，**不是 isFond**。
        Assert.True(info.IsCollected);
    }

    /// <summary>未收藏的那份：<c>collectTime</c> 整个键不出现。</summary>
    [Fact]
    public async Task PlaylistInfo_NotCollectedForTheOtherRealResponse()
    {
        RespondWith("playlist-info-not-collected.json");

        var info = await _api.GetPlaylistInfoAsync(3676986117, 4, Ct);

        Assert.NotNull(info);
        Assert.False(info.IsCollected);
        Assert.Equal(576246173, info.CreatorId);

        // praise 与 collectedCnt 在样本里相等，映射优先取语义正确的那个。
        Assert.Equal(737, info.CollectedCount);
    }

    [Fact]
    public async Task PlaylistInfo_NonPositiveId_Throws()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _api.GetPlaylistInfoAsync(0, 4, Ct));
    }
}
