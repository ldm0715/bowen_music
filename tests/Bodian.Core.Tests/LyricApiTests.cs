using System.Text;
using Bodian.Core.Api;
using Bodian.Core.Lyrics;
using Bodian.Core.Models;
using Bodian.Core.Models.Lyrics;
using Bodian.Core.Tests.Support;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 歌词取词链路：请求形态、顶层 <c>lrcx</c> 回显、空串回退、解析衔接。零真实网络。
/// </summary>
/// <remarks>
/// <c>fixtures/lyric-response-228908-lrcx1.json</c> 是**由 <c>lyric-228908-lrcx1.lrc</c> 合成**的
/// （信封 + 顶层 <c>lrcx</c> + Base64），没有为它额外发过探测请求。
/// </remarks>
public sealed class LyricApiTests : IDisposable
{
    private const long MusicId = 228908;

    private static readonly string EmptyResponse =
        """{"code":200,"msg":"success","lrcx":0,"data":{"content":""}}""";

    private readonly ReplayHandler _handler = new();
    private readonly BodianSession _session = BodianSession.CreateAnonymous();
    private readonly BodianHttpTransport _transport;
    private readonly BodianApi _api;

    public LyricApiTests()
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

    /// <summary>把歌词文本包成服务端那个信封。Base64 不含需转义的字符，直接拼字符串。</summary>
    private static string Envelope(string lyricText, int lrcx)
    {
        var content = Convert.ToBase64String(Encoding.UTF8.GetBytes(lyricText));

        return $"{{\"code\":200,\"msg\":\"success\",\"lrcx\":{lrcx},"
             + $"\"data\":{{\"content\":\"{content}\"}}}}";
    }

    /// <summary>从请求里解出它实际要的是哪一版歌词。</summary>
    private static int RequestedLrcx(CapturedRequest request)
    {
        var url = request.Url;
        var q = Uri.UnescapeDataString(url[(url.IndexOf("q=", StringComparison.Ordinal) + 2)..]);
        var payload = Encoding.UTF8.GetString(Convert.FromBase64String(q));

        return payload.Contains("lrcx=1", StringComparison.Ordinal) ? 1 : 0;
    }

    private static Track TrackWithoutLyricInfo() => new() { Id = MusicId, Title = "晴天" };

    // ── 请求形态 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 歌词站是绝对地址、不走 <c>/api</c> 前缀、**不签名**。
    /// </summary>
    /// <remarks>
    /// 这条一挂就是「把歌词请求发到主站去了」，症状是 404 或空响应，很难从错误码上看出来。
    /// </remarks>
    [Fact]
    public async Task GetLyricAsync_UsesTheAbsoluteUnsignedLyricEndpoint()
    {
        _handler.Responder = _ => ReplayHandler.Json(Fixtures.Read("lyric-response-228908-lrcx1.json"));

        await _api.GetLyricAsync(MusicId, BodianLyricPayload.WordByWord, Ct);

        var request = Assert.Single(_handler.Requests);

        Assert.Equal("GET", request.Method);
        Assert.StartsWith("https://mlyric.kuwo.cn/mobi.s?f=bodian&q=", request.Url, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/", request.Url, StringComparison.Ordinal);
        Assert.DoesNotContain("sign=", request.Url, StringComparison.Ordinal);

        // rid 是**波点的 musicId**，payload 里必须带上请求的版式。
        var q = Uri.UnescapeDataString(request.Url[(request.Url.IndexOf("q=", StringComparison.Ordinal) + 2)..]);
        var payload = Encoding.UTF8.GetString(Convert.FromBase64String(q));

        Assert.Equal("type=lyric&req=2&lrcx=1&rid=228908&songname=&artist=&corp=kuwo&fromchannel=bodian", payload);
    }

    /// <summary>顶层 <c>lrcx</c> 不在 <c>data</c> 里，只能由传输层从信封上取。</summary>
    [Fact]
    public async Task SendAbsoluteAsync_ReadsTheTopLevelLrcx()
    {
        _handler.Responder = _ => ReplayHandler.Json(Fixtures.Read("lyric-response-228908-lrcx1.json"));

        var envelope = await _transport.SendAbsoluteAsync(
            BodianLyricPayload.BuildRequestUri(MusicId, BodianLyricPayload.WordByWord),
            BodianJsonContext.Default.LyricContentDto,
            Ct);

        Assert.True(envelope.IsSuccess);
        Assert.Equal(1, envelope.Lrcx);
        Assert.NotNull(envelope.Data?.Content);
    }

    /// <summary>其余端点没有这个字段，取到的是 <c>null</c> 而不是 0。</summary>
    [Fact]
    public async Task ParseEnvelope_WithoutLrcx_LeavesItNull()
    {
        _handler.Responder = _ => ReplayHandler.Json(Fixtures.Read("music-info-228908.json"));

        var envelope = await _transport.SendAsync(
            new BodianRequest
            {
                Path = Endpoints.MusicInfo,
                Query = [new KeyValuePair<string, string>("musicId", "228908")],
                Signed = true,
            },
            BodianJsonContext.Default.TrackDto,
            Ct);

        Assert.True(envelope.IsSuccess);
        Assert.Null(envelope.Lrcx);
    }

    // ── 取词与解析 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetLyricAsync_DecodesTheRealFixture()
    {
        _handler.Responder = _ => ReplayHandler.Json(Fixtures.Read("lyric-response-228908-lrcx1.json"));

        var text = await _api.GetLyricAsync(MusicId, BodianLyricPayload.WordByWord, Ct);

        Assert.StartsWith("[kuwo:127]", text, StringComparison.Ordinal);
        Assert.Contains("[ti:", text, StringComparison.Ordinal);

        // 只解一层 Base64：解完就该是明文歌词，不是又一段 Base64 或二进制。
        Assert.DoesNotContain('\0', text);
    }

    [Fact]
    public async Task GetLyricAsync_EmptyContent_IsAnEmptyStringNotAnError()
    {
        _handler.Responder = _ => ReplayHandler.Json(EmptyResponse);

        var text = await _api.GetLyricAsync(MusicId, BodianLyricPayload.WordByWord, Ct);

        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public async Task GetLyricsAsync_ParsesIntoWordByWordDocument()
    {
        _handler.Responder = _ => ReplayHandler.Json(Fixtures.Read("lyric-response-228908-lrcx1.json"));

        var document = await _api.GetLyricsAsync(TrackWithoutLyricInfo(), Ct);

        Assert.Equal(LyricKind.WordByWord, document.Kind);
        Assert.Equal(63, document.Lines.Count);
        Assert.Single(_handler.Requests);
    }

    /// <summary>
    /// 没有歌词轨信息时先赌逐字版，拿到空串再退逐行版。
    /// </summary>
    /// <remarks>
    /// 这是「不要无脑写死 lrcx=1」那条结论的落地：没有逐字轨的歌会拿到空串，
    /// 而服务端**不做逐行回退**，退版必须由客户端发起。
    /// </remarks>
    [Fact]
    public async Task GetLyricsAsync_FallsBackToLineByLine_WhenWordTrackIsUnknown()
    {
        _handler.Responder = _ => ReplayHandler.Json(
            RequestedLrcx(_handler.LastRequest) == 1
                ? EmptyResponse
                : Envelope(Fixtures.Read("lyric-228908-lrcx0.lrc"), 0));

        var document = await _api.GetLyricsAsync(TrackWithoutLyricInfo(), Ct);

        Assert.Equal(LyricKind.LineByLine, document.Kind);
        Assert.Equal(63, document.Lines.Count);

        Assert.Equal(2, _handler.Requests.Count);
        Assert.Equal(1, RequestedLrcx(_handler.Requests[0]));
        Assert.Equal(0, RequestedLrcx(_handler.Requests[1]));
    }

    /// <summary>
    /// 轨信息说「有逐字轨」但歌词站回了空串时，仍然退一次逐行版。
    /// </summary>
    /// <remarks>
    /// 这多半是 <c>lrc_info</c> 与歌词站不一致（轨信息过期）。这种时候多要一次、
    /// 拿到逐行歌词，比守着「已知有逐字轨就不退」给用户一片空白划算。
    /// <para>
    /// 该退的判据是**「问了逐字版且拿到空串」**，不是「轨信息是未知的」。
    /// 与 <see cref="GetLyricsAsync_WithKnownLineOnlyTrack_RequestsLineByLine"/> 一起看：
    /// 已知只有逐行轨时**一次请求都不多要**。
    /// </para>
    /// </remarks>
    [Fact]
    public async Task GetLyricsAsync_WithKnownWordTrack_ReturningEmpty_FallsBackOnce()
    {
        _handler.Responder = _ => ReplayHandler.Json(
            RequestedLrcx(_handler.LastRequest) == 1
                ? EmptyResponse
                : Envelope(Fixtures.Read("lyric-228908-lrcx0.lrc"), 0));

        var track = TrackWithoutLyricInfo() with
        {
            Lyrics = new TrackLyricInfo(HasLineByLine: true, HasWordByWord: true),
        };

        var document = await _api.GetLyricsAsync(track, Ct);

        Assert.Equal(LyricKind.LineByLine, document.Kind);
        Assert.Equal(2, _handler.Requests.Count);
    }

    /// <summary>已知没有逐字轨时直接要逐行版，一次到位。</summary>
    [Fact]
    public async Task GetLyricsAsync_WithKnownLineOnlyTrack_RequestsLineByLine()
    {
        _handler.Responder = _ => ReplayHandler.Json(Envelope(Fixtures.Read("lyric-228908-lrcx0.lrc"), 0));

        var track = TrackWithoutLyricInfo() with
        {
            Lyrics = new TrackLyricInfo(HasLineByLine: true, HasWordByWord: false),
        };

        var document = await _api.GetLyricsAsync(track, Ct);

        Assert.Equal(LyricKind.LineByLine, document.Kind);
        Assert.Single(_handler.Requests);
        Assert.Equal(0, RequestedLrcx(_handler.Requests[0]));
    }
}
