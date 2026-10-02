using Bodian.Core.Api;
using Bodian.Core.Api.Paging;
using Bodian.Core.Models;
using Bodian.Core.Tests.Support;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 业务门面。响应全部来自 <c>fixtures/</c> 或内联，零真实网络。
/// </summary>
public sealed class BodianApiTests : IDisposable
{
    private const string FullRightResponse =
        """{"code":200,"msg":"success","data":{"status":4}}""";

    private const string DeniedResponse =
        """{"code":200,"msg":"success","data":{"status":7}}""";

    private readonly ReplayHandler _handler = new();
    private readonly BodianSession _session = BodianSession.CreateAnonymous();
    private readonly BodianHttpTransport _transport;
    private readonly BodianApi _api;

    public BodianApiTests()
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

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    // ── 搜索 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Search_MapsRealResponse()
    {
        _handler.Responder = _ => ReplayHandler.Json(Fixtures.Read("search-music-list-anon.json"));

        var page = await _api.SearchAsync("晴天", new PagedCursor(PagingConvention.ZeroBased, 30), Ct);

        Assert.NotEmpty(page.Items);

        var first = page.Items[0];

        Assert.True(first.Id > 0);
        Assert.False(string.IsNullOrWhiteSpace(first.Title));
        Assert.True(first.Duration > TimeSpan.Zero);
        Assert.NotEmpty(first.AvailableQualities);
    }

    /// <summary>
    /// 搜索列表里的付费曲要被标出来。
    /// </summary>
    /// <remarks>
    /// 这份 fixture 里 5 条全是付费曲（<c>feeType.vip = "1"</c>），所以断言是「全都是」。
    /// <b>换 fixture 时这条会挂，那是提醒你样本变了</b>，不是代码坏了。
    /// </remarks>
    [Fact]
    public async Task Search_MarksPaidTracks()
    {
        _handler.Responder = _ => ReplayHandler.Json(Fixtures.Read("search-music-list-anon.json"));

        var page = await _api.SearchAsync("anything", new PagedCursor(PagingConvention.ZeroBased, 30), Ct);

        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, track => Assert.True(track.RequiresVip));
    }

    /// <summary>专辑与封面要一路映射到领域模型 —— SMTC 与播放条都要用。</summary>
    [Fact]
    public async Task Search_MapsAlbumAndCover()
    {
        _handler.Responder = _ => ReplayHandler.Json(Fixtures.Read("search-music-list-anon.json"));

        var page = await _api.SearchAsync("anything", new PagedCursor(PagingConvention.ZeroBased, 30), Ct);

        var first = page.Items[0];

        Assert.False(string.IsNullOrWhiteSpace(first.AlbumName));
        Assert.NotNull(first.CoverImage);
        Assert.StartsWith("https://", first.CoverImage.ToString());
    }

    /// <summary>搜索的列表字段是 <c>resultList</c>，页号从 0 开始。</summary>
    [Fact]
    public async Task Search_RequestShape()
    {
        _handler.Responder = _ => ReplayHandler.Json(Fixtures.Read("search-music-list-anon.json"));

        await _api.SearchAsync("晴天", new PagedCursor(PagingConvention.ZeroBased, 30), Ct);

        var sent = Assert.Single(_handler.Requests);

        Assert.Contains("search/music/list", sent.Url);
        Assert.Contains("keyword=", sent.Url);
        Assert.Contains("correct=1", sent.Url);
        Assert.Contains("pn=0", sent.Url);
        Assert.Contains("rn=30", sent.Url);
    }

    /// <summary>
    /// 游标推进。<c>pn</c> 是**页号**不是偏移量（<c>offset ÷ pageSize</c>），所以第二页是
    /// <c>pn=1</c> 而不是 <c>pn=30</c> —— 这里确实踩过一次。
    /// </summary>
    [Fact]
    public async Task Search_AdvancesCursorBetweenPages()
    {
        _handler.Responder = _ => ReplayHandler.Json(Fixtures.Read("search-music-list-anon.json"));

        var cursor = new PagedCursor(PagingConvention.ZeroBased, 30);

        await _api.SearchAsync("晴天", cursor, Ct);
        await _api.SearchAsync("晴天", cursor, Ct);

        Assert.Contains("pn=0", _handler.Requests[0].Url);
        Assert.Contains("pn=1", _handler.Requests[1].Url);
        Assert.Contains("rn=30", _handler.Requests[1].Url);
    }

    [Fact]
    public async Task Search_BlankKeyword_SendsNothing()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _api.SearchAsync("   ", new PagedCursor(PagingConvention.ZeroBased), Ct));

        Assert.Empty(_handler.Requests);
    }

    // ── 曲目详情 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTrack_MapsRealResponse()
    {
        _handler.Responder = _ => ReplayHandler.Json(Fixtures.Read("music-info-228908.json"));

        var track = await _api.GetTrackAsync(228908, Ct);

        Assert.NotNull(track);
        Assert.Equal(228908, track.Id);
        Assert.False(string.IsNullOrWhiteSpace(track.ArtistText));
        Assert.Equal(30149, track.CommentCount);
    }

    /// <summary>id 非法时**零请求**就报错，不要把坏 id 发出去。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetTrack_InvalidId_SendsNothing(long musicId)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _api.GetTrackAsync(musicId, Ct));

        Assert.Empty(_handler.Requests);
    }

    // ── 播放授权 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 只能试听时，地址来自 <c>checkRight</c> 的 <c>audition</c>，
    /// <b>不会去请求 audioUrl</b>（匿名下那只会拿到 20018）。
    /// </summary>
    [Fact]
    public async Task Resolve_Audition_ComesFromCheckRight()
    {
        _handler.Responder = _ => ReplayHandler.Json(Fixtures.Read("checkright-228908.json"));

        var result = await _api.ResolvePlaybackAsync(SampleTrack(), Ct);

        var audition = Assert.IsType<PlaybackResolution.AuditionOnly>(result);

        Assert.Equal(TimeSpan.Zero, audition.Start);
        Assert.Equal(TimeSpan.FromSeconds(29), audition.End);
        Assert.Equal("mp3", audition.Source.Format);
        Assert.StartsWith("https://", audition.Source.Url.ToString());
        Assert.Null(audition.Source.RequestedQuality);

        var sent = Assert.Single(_handler.Requests);
        Assert.Contains("checkRight", sent.Url);
    }

    /// <summary>试听信息缺失时不能当成可播。</summary>
    [Fact]
    public async Task Resolve_AuditionWithoutDetail_IsDenied()
    {
        _handler.Responder = _ => ReplayHandler.Json("""{"code":200,"msg":"success","data":{"status":3}}""");

        var result = await _api.ResolvePlaybackAsync(SampleTrack(), Ct);

        var denied = Assert.IsType<PlaybackResolution.Denied>(result);
        Assert.Equal(PlaybackDenialReason.AuditionUnavailable, denied.Reason);
    }

    [Fact]
    public async Task Resolve_DeniedAnonymous_AsksForLogin()
    {
        _handler.Responder = _ => ReplayHandler.Json(DeniedResponse);

        var result = await _api.ResolvePlaybackAsync(SampleTrack(), Ct);

        var denied = Assert.IsType<PlaybackResolution.Denied>(result);
        Assert.Equal(PlaybackDenialReason.NotAuthenticated, denied.Reason);

        // 没权限就不要再去要地址。
        Assert.Single(_handler.Requests);
    }

    [Fact]
    public async Task Resolve_DeniedAuthenticated_IsNoPermission()
    {
        _session.Set("50303440", "tok_secret");
        _handler.Responder = _ => ReplayHandler.Json(DeniedResponse);

        var result = await _api.ResolvePlaybackAsync(SampleTrack(), Ct);

        var denied = Assert.IsType<PlaybackResolution.Denied>(result);
        Assert.Equal(PlaybackDenialReason.NoPermission, denied.Reason);
    }

    /// <summary>正常路径：无损档，服务端照给，未降级。</summary>
    [Fact]
    public async Task Resolve_Playable_Lossless()
    {
        Router(FullRightResponse, Fixtures.Read("audiourl-lossless-flac.json"));

        var result = await _api.ResolvePlaybackAsync(SampleTrack(), Ct);

        var playable = Assert.IsType<PlaybackResolution.Playable>(result);

        Assert.Equal(AudioQuality.Lossless, playable.Source.RequestedQuality);
        Assert.Equal("flac", playable.Source.Format);
        Assert.Equal(2000, playable.Source.BitrateKbps);
        Assert.False(playable.Source.WasDowngraded);
        Assert.StartsWith("https://", playable.Source.Url.ToString());
    }

    /// <summary>服务端静默降级时必须如实标记 —— 这是 P2 最容易骗过自己的地方。</summary>
    [Fact]
    public async Task Resolve_Downgraded_IsFlagged()
    {
        Router(FullRightResponse, Fixtures.Read("audiourl-formatflac-downgraded.json"));

        var result = await _api.ResolvePlaybackAsync(SampleTrack(), Ct);

        var playable = Assert.IsType<PlaybackResolution.Playable>(result);

        Assert.Equal(AudioQuality.Lossless, playable.Source.RequestedQuality);
        Assert.Equal("mp3", playable.Source.Format);
        Assert.Equal(320, playable.Source.BitrateKbps);
        Assert.True(playable.Source.WasDowngraded);
    }

    /// <summary>
    /// <c>20018</c>（没有解锁付费歌曲）：<c>checkRight</c> 说可播但取地址被拒，按无权限处理。
    /// </summary>
    [Fact]
    public async Task Resolve_NotPlayable_IsDeniedAsNoPermission()
    {
        Router(FullRightResponse, Fixtures.Read("audiourl-anon-20018.json"));

        var result = await _api.ResolvePlaybackAsync(SampleTrack(), Ct);

        var denied = Assert.IsType<PlaybackResolution.Denied>(result);
        Assert.Equal(PlaybackDenialReason.NoPermission, denied.Reason);
        Assert.Equal(2, _handler.Requests.Count);
    }

    /// <summary>没有可播档位时连 audioUrl 都不发。</summary>
    [Fact]
    public async Task Resolve_NoUsableQuality_SendsNoAudioUrlRequest()
    {
        Router(FullRightResponse, Fixtures.Read("audiourl-lossless-flac.json"));

        var track = SampleTrack() with { AvailableQualities = [] };

        var result = await _api.ResolvePlaybackAsync(track, Ct);

        var denied = Assert.IsType<PlaybackResolution.Denied>(result);
        Assert.Equal(PlaybackDenialReason.NoUsableQuality, denied.Reason);

        var sent = Assert.Single(_handler.Requests);
        Assert.Contains("checkRight", sent.Url);
    }

    /// <summary>
    /// <b>回归：请求里绝不能出现 <c>format</c>。</b> 带上它会让服务端静默降级到 320k mp3，
    /// 而业务码仍是 200。
    /// </summary>
    [Fact]
    public async Task Resolve_AudioUrlRequest_NeverCarriesFormat()
    {
        Router(FullRightResponse, Fixtures.Read("audiourl-lossless-flac.json"));

        await _api.ResolvePlaybackAsync(SampleTrack(), Ct);

        var audioUrlRequest = Assert.Single(_handler.Requests, r => r.Url.Contains("audioUrl", StringComparison.Ordinal));

        Assert.DoesNotContain("format=", audioUrlRequest.Url);
        Assert.DoesNotContain("format", audioUrlRequest.Body ?? "");
        Assert.Contains("br=2000kflac", audioUrlRequest.Url);
        Assert.Contains("devId=", audioUrlRequest.Url);
    }

    /// <summary><c>checkRight</c> 每首歌只调一次，且请求形态是 GET + JSON body。</summary>
    [Fact]
    public async Task Resolve_CheckRight_IsSentOnceWithJsonBody()
    {
        Router(FullRightResponse, Fixtures.Read("audiourl-lossless-flac.json"));

        await _api.ResolvePlaybackAsync(SampleTrack(), Ct);

        var checkRight = Assert.Single(_handler.Requests, r => r.Url.Contains("checkRight", StringComparison.Ordinal));

        Assert.Equal("GET", checkRight.Method);
        Assert.Equal("""{"musicId":228908,"freeSign":""}""", checkRight.Body);
    }

    /// <summary>服务端两个地址都给时优先 https。</summary>
    [Fact]
    public async Task Resolve_PrefersHttps()
    {
        Router(FullRightResponse, Fixtures.Read("audiourl-lossless-flac.json"));

        var result = await _api.ResolvePlaybackAsync(SampleTrack(), Ct);

        var playable = Assert.IsType<PlaybackResolution.Playable>(result);

        Assert.Equal("https", playable.Source.Url.Scheme);
    }

    /// <summary>两个地址都缺失 → 不能播，而不是交给播放器一个空地址。</summary>
    [Fact]
    public async Task Resolve_WithoutAnyUrl_IsDenied()
    {
        Router(FullRightResponse, """{"code":200,"msg":"success","data":{"format":"flac","bitrate":2000}}""");

        var result = await _api.ResolvePlaybackAsync(SampleTrack(), Ct);

        var denied = Assert.IsType<PlaybackResolution.Denied>(result);
        Assert.Equal(PlaybackDenialReason.NoStreamUrl, denied.Reason);
    }

    /// <summary>非 http(s) 的地址一律拒绝。</summary>
    [Fact]
    public async Task Resolve_RejectsNonHttpUrl()
    {
        Router(
            FullRightResponse,
            """{"code":200,"msg":"success","data":{"audioHttpsUrl":"javascript:alert(1)","format":"flac","bitrate":2000}}""");

        var result = await _api.ResolvePlaybackAsync(SampleTrack(), Ct);

        var denied = Assert.IsType<PlaybackResolution.Denied>(result);
        Assert.Equal(PlaybackDenialReason.NoStreamUrl, denied.Reason);
    }

    // ── PlaybackPolicy ──────────────────────────────────────────────────────

    /// <summary>试听**不得**被当成完整歌曲持久化。</summary>
    [Fact]
    public async Task Policy_Audition_MayNotBePersistedAsCompleteTrack()
    {
        _handler.Responder = _ => ReplayHandler.Json(Fixtures.Read("checkright-228908.json"));

        var policy = PlaybackPolicy.For(await _api.ResolvePlaybackAsync(SampleTrack(), Ct));

        Assert.True(policy.IsAudition);
        Assert.False(policy.IsCompleteTrack);
        Assert.False(policy.MayPersistAsCompleteTrack);
        Assert.Equal(TimeSpan.FromSeconds(29), policy.StopAt);
        Assert.False(policy.ShouldStopAt(TimeSpan.FromSeconds(28)));
        Assert.True(policy.ShouldStopAt(TimeSpan.FromSeconds(29)));
    }

    [Fact]
    public async Task Policy_Playable_MayBePersistedAsCompleteTrack()
    {
        Router(FullRightResponse, Fixtures.Read("audiourl-lossless-flac.json"));

        var policy = PlaybackPolicy.For(await _api.ResolvePlaybackAsync(SampleTrack(), Ct));

        Assert.True(policy.IsCompleteTrack);
        Assert.True(policy.MayPersistAsCompleteTrack);
        Assert.Null(policy.StopAt);
        Assert.False(policy.ShouldStopAt(TimeSpan.FromHours(1)));
    }

    [Fact]
    public async Task Policy_Denied_IsNotPersistable()
    {
        _handler.Responder = _ => ReplayHandler.Json(DeniedResponse);

        var policy = PlaybackPolicy.For(await _api.ResolvePlaybackAsync(SampleTrack(), Ct));

        Assert.False(policy.IsCompleteTrack);
        Assert.False(policy.IsAudition);
        Assert.False(policy.MayPersistAsCompleteTrack);
    }

    // ── helper ──────────────────────────────────────────────────────────────

    private void Router(string checkRightResponse, string audioUrlResponse)
    {
        _handler.Responder = request =>
            request.RequestUri!.OriginalString.Contains("audioUrl", StringComparison.Ordinal)
                ? ReplayHandler.Json(audioUrlResponse)
                : ReplayHandler.Json(checkRightResponse);
    }

    private static Track SampleTrack() => new()
    {
        Id = 228908,
        Title = "晴天",
        ArtistText = "周杰伦",
        AvailableQualities = [AudioQuality.Lossless, AudioQuality.High, AudioQuality.Standard],
    };
}
