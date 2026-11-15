using Bowen.Core.Api;
using Bowen.Core.Models.AudioMemory;
using Bowen.Core.Tests.Support;
using Xunit;

namespace Bowen.Core.Tests;

/// <summary>
/// 音波回忆的三条读接口与一条写接口。
/// </summary>
/// <remarks>
/// 协议结论来自 2026-11-15 的实测，见 <c>docs/audio-memory.md</c>。
/// 这里钉住：排行按次数降序、月报无数据时返回 <c>null</c>、播放次数字段不被误当成 0。
/// </remarks>
public sealed class AudioMemoryApiTests : IDisposable
{
    private const string Uid = "50303440";

    private readonly ReplayHandler _handler = new();
    private readonly BodianSession _session = BodianSession.CreateAnonymous();
    private readonly BodianHttpTransport _transport;
    private readonly BodianApi _api;

    public AudioMemoryApiTests()
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

    private void SignIn() => _session.Set(Uid, "test-token");

    private void RespondWith(string data) =>
        _handler.Responder = _ => ReplayHandler.Json("{\"code\":200,\"msg\":\"success\",\"data\":" + data + "}");

    // ── 本周最爱 ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task WeeklyRanking_MapsTrackAndPlayCount()
    {
        SignIn();
        RespondWith("""
            {"musicList":[
              {"id":118991,"name":"蓝色风暴","artist":"周杰伦","album":"十一月的萧邦",
               "albumPic120":"https://img.example/cover.webp","duration":287,"playcnt":3,
               "audios":[{"level":"ff","bitrate":"2000","format":"flac","size":"1Mb"}]},
              {"id":118989,"name":"黑色毛衣","artist":"周杰伦","duration":252,"playcnt":2}
            ]}
            """);

        var ranking = await _api.GetWeeklyPlayRankingAsync(Ct);

        Assert.NotNull(ranking);
        Assert.Equal(2, ranking.Count);
        Assert.Equal(118991, ranking[0].Track.Id);
        Assert.Equal("蓝色风暴", ranking[0].Track.Title);
        Assert.Equal("周杰伦", ranking[0].Track.ArtistText);
        Assert.Equal(3, ranking[0].PlayCount);
        Assert.Equal(2, ranking[1].PlayCount);
    }

    [Fact]
    public async Task WeeklyRanking_RequestShape()
    {
        SignIn();
        RespondWith("""{"musicList":[]}""");

        await _api.GetWeeklyPlayRankingAsync(Ct);

        var url = _handler.LastRequest.Url;
        Assert.Contains("ucenter/playdata/play_ranking", url, StringComparison.Ordinal);
        Assert.Contains($"userId={Uid}", url, StringComparison.Ordinal);
        Assert.Contains("type=1", url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WeeklyRanking_MissingPlayCount_IsZero()
    {
        SignIn();
        RespondWith("""{"musicList":[{"id":1,"name":"A"}]}""");

        var ranking = await _api.GetWeeklyPlayRankingAsync(Ct);

        Assert.NotNull(ranking);
        Assert.Equal(0, ranking[0].PlayCount);
    }

    // ── 月报预览 ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Summary_MapsMonthPreview()
    {
        SignIn();
        RespondWith("""
            {"monthReport":{"listenTotal":"175","listenChain":"比上月\n+18909%",
             "pic":"https://img.example/report.webp","monthDesc":"10月听歌"}}
            """);

        var summary = await _api.GetMonthReportSummaryAsync(Ct);

        Assert.NotNull(summary);
        Assert.Equal("10月听歌", summary.MonthDesc);
        Assert.Equal("175", summary.ListenTotalMinutes);
        Assert.Equal("比上月\n+18909%", summary.ListenChain);
        Assert.NotNull(summary.Cover);
    }

    /// <summary>没有任何月报时是 <c>null</c>，不是一份空对象。</summary>
    [Fact]
    public async Task Summary_NoMonthReport_IsNull()
    {
        SignIn();
        RespondWith("""{}""");

        Assert.Null(await _api.GetMonthReportSummaryAsync(Ct));
    }

    // ── 月报详情 ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task MonthReport_MapsFieldsAndTopTracks()
    {
        SignIn();
        RespondWith("""
            {"isUpdate":1,"info":{
              "monthDate":"2026-10-01","playTotal":106,"listenTotal":"175",
              "listenChain":"比上月+18909%","playChain":"比上月+2550%",
              "listenContent":"10-02与音乐共度81分钟","playContent":"听了6次也不够",
              "listenTop1MusicName":"A","songNum":81,"timeRank":43,
              "musicTop10":[
                {"id":3632428,"name":"A","artist":"B","album":"C",
                 "albumPic":"https://img.example/a.jpg","playCount":6}
              ],
              "albumTop10":["https://img.example/a.jpg","not-a-url"],
              "artistName":"田馥甄","artistPic":"https://img.example/artist.png",
              "artistMusicName":"渺小","artistMusicPic":"https://img.example/music.jpg",
              "artistContent":"在10月陪伴你度过22分钟"
            }}
            """);

        var report = await _api.GetMonthReportAsync(new MonthPeriod(2026, 10), Ct);

        Assert.NotNull(report);
        Assert.Equal(2026, report.Year);
        Assert.Equal(10, report.Month);
        Assert.Equal("175", report.ListenTotalMinutes);
        Assert.Equal(106, report.PlayCount);
        Assert.Equal(81, report.SongCount);
        Assert.Equal("A", report.ListenTopTrackTitle);
        Assert.Equal(43, report.TimeRank);
        Assert.Equal("田馥甄", report.ArtistName);
        Assert.NotNull(report.ArtistCover);
        Assert.Single(report.TopTracks);
        Assert.Equal("A", report.TopTracks[0].Title);
        Assert.Equal(6, report.TopTracks[0].PlayCount);
        // 只保留能解析成 URI 的封面，坏值直接丢掉。
        Assert.Single(report.AlbumCovers);
    }

    /// <summary>
    /// 无数据月份返回 <c>{"isUpdate":1}</c>，没有 <c>info</c>。这是唯一的存在性判据。
    /// </summary>
    [Fact]
    public async Task MonthReport_NoInfo_IsNull()
    {
        SignIn();
        RespondWith("""{"isUpdate":1}""");

        Assert.Null(await _api.GetMonthReportAsync(new MonthPeriod(2026, 9), Ct));
    }

    [Fact]
    public async Task MonthReport_RequestShape()
    {
        SignIn();
        RespondWith("""{"isUpdate":1}""");

        await _api.GetMonthReportAsync(new MonthPeriod(2026, 10), Ct);

        Assert.Contains("play/month/report/info", _handler.LastRequest.Url, StringComparison.Ordinal);
        Assert.Contains("date=202610", _handler.LastRequest.Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MonthReport_TransportFailure_ReturnsNull()
    {
        SignIn();
        _handler.Responder = _ => throw new HttpRequestException("boom");

        Assert.Null(await _api.GetMonthReportAsync(new MonthPeriod(2026, 10), Ct));
    }

    // ── 新建月度歌单 ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CreatePlaylist_PostsMonthBody()
    {
        SignIn();
        RespondWith("""{}""");

        var created = await _api.CreateMonthReportPlaylistAsync(new MonthPeriod(2026, 10), Ct);

        Assert.True(created);
        Assert.Equal("POST", _handler.LastRequest.Method);
        Assert.Contains("play/month/report/playlist", _handler.LastRequest.Url, StringComparison.Ordinal);
        Assert.Contains("\"date\":\"202610\"", _handler.LastRequest.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreatePlaylist_Anonymous_Throws()
    {
        await Assert.ThrowsAsync<BodianNotSignedInException>(
            () => _api.CreateMonthReportPlaylistAsync(new MonthPeriod(2026, 10), Ct));
    }

    [Fact]
    public async Task CreatePlaylist_BusinessError_ReturnsFalse()
    {
        SignIn();
        _handler.Responder = _ => ReplayHandler.Json("""{"code":11012,"msg":"会话无效"}""");

        Assert.False(await _api.CreateMonthReportPlaylistAsync(new MonthPeriod(2026, 10), Ct));
    }
}
