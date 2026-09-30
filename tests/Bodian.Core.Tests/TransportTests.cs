using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Bodian.Core.Api;
using Bodian.Core.Tests.Support;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 传输层测试。**全部走回放 handler，零真实网络请求。**
/// </summary>
public sealed class TransportTests : IDisposable
{
    private readonly ReplayHandler _handler = new();
    private readonly BodianSession _session = BodianSession.CreateAnonymous();
    private readonly BodianHttpTransport _transport;
    private readonly BodianTransportOptions _options = new();

    public TransportTests()
    {
        _transport = new BodianHttpTransport(
            _handler,
            _options,
            _session,
            new FakeDeviceIdentity(),
            FixedTimeProvider.Golden);
    }

    public void Dispose() => _transport.Dispose();

    /// <summary>
    /// 发一次请求。默认带测试框架的取消令牌，这样测试被取消时能立刻停下而不是等到超时。
    /// 要测「调用方主动取消」时，直接调 <c>_transport.SendAsync</c> 并传自己的令牌。
    /// </summary>
    private Task<BodianEnvelope<T>> Send<T>(BodianRequest request, JsonTypeInfo<T> typeInfo)
        => _transport.SendAsync(request, typeInfo, TestContext.Current.CancellationToken);

    // ── 请求形态 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Request_CarriesTheFullHeaderSet()
    {
        _handler.Responder = _ => ReplayHandler.Json("""{"code":200,"msg":"success","data":null}""");

        await Send(
            new BodianRequest { Path = "service/music/info", Query = [new("musicId", "228908")] },
            BodianJsonContext.Default.TrackDto);

        var sent = _handler.LastRequest;

        Assert.Equal("GET", sent.Method);
        Assert.Equal("Dart/3.3 (dart:io)", sent.Header("User-Agent"));
        Assert.Equal("win", sent.Header("plat"));
        Assert.Equal("W1", sent.Header("channel"));
        Assert.Equal("1.1.7", sent.Header("ver"));       // 钉死的版本，见 options 的注释
        Assert.Equal("13", sent.Header("svrver"));
        Assert.Equal("application/json", sent.Header("api-ver"));
        Assert.Equal("Windows", sent.Header("brand"));
        Assert.Equal("wifi", sent.Header("net"));
        Assert.Equal("0123456789abcdef0123456789abcdef", sent.Header("devid"));
        Assert.Equal(sent.Header("devid"), sent.Header("qimei36"));   // 两者必须是同一个值
        Assert.Equal("gzip", sent.Header("Accept-Encoding"));
    }

    /// <summary>头只能挂一次——<c>TryAddWithoutValidation</c> 对同名头是追加，重复挂会发两次。</summary>
    [Fact]
    public async Task Request_DoesNotDuplicateHeaders()
    {
        await Send(
            new BodianRequest
            {
                Path = "play/music/v2/checkRight",
                Verb = BodianHttpVerb.Get,
                Query = [new("musicId", "228908")],
                JsonBody = """{"musicId":228908,"freeSign":""}""",
                Signed = true,
            },
            BodianJsonContext.Default.CheckRightDto);

        Assert.Equal(1, _handler.LastRequest.HeaderCount("plat"));
        Assert.Equal(1, _handler.LastRequest.HeaderCount("ver"));
        Assert.Equal(1, _handler.LastRequest.HeaderCount("devid"));
        Assert.Equal(1, _handler.LastRequest.HeaderCount("Content-Type"));
    }

    /// <summary>未登录时 uid / token 进 query，但**不进请求头**。</summary>
    [Fact]
    public async Task Anonymous_UsesMinusOneUid_AndNoIdentityHeaders()
    {
        await Send(
            new BodianRequest { Path = "service/music/info", Query = [new("musicId", "228908")] },
            BodianJsonContext.Default.TrackDto);

        var sent = _handler.LastRequest;

        Assert.Contains("uid=-1", sent.Url);
        Assert.Contains("token=", sent.Url);
        Assert.Null(sent.Header("uid"));
        Assert.Null(sent.Header("token"));
    }

    /// <summary>已登录时**同时**用 query 与请求头传身份（PC 端的实际行为）。</summary>
    [Fact]
    public async Task Authenticated_SendsIdentityBothWays()
    {
        _session.Set("50303440", "tok_secret");

        await Send(
            new BodianRequest { Path = "service/music/info", Query = [new("musicId", "228908")] },
            BodianJsonContext.Default.TrackDto);

        var sent = _handler.LastRequest;

        Assert.Contains("uid=50303440", sent.Url);
        Assert.Contains("token=tok_secret", sent.Url);
        Assert.Equal("50303440", sent.Header("uid"));
        Assert.Equal("tok_secret", sent.Header("token"));
    }

    /// <summary>
    /// <c>checkRight</c> 是 **GET 但必须带 JSON body**，不能退化成普通 GET 丢掉 body。
    /// </summary>
    [Fact]
    public async Task GetWithJsonBody_IsPreserved()
    {
        const string body = """{"musicId":228908,"freeSign":""}""";

        await Send(
            new BodianRequest
            {
                Path = "play/music/v2/checkRight",
                Verb = BodianHttpVerb.Get,
                Query = [new("musicId", "228908")],
                JsonBody = body,
            },
            BodianJsonContext.Default.CheckRightDto);

        var sent = _handler.LastRequest;

        Assert.Equal("GET", sent.Method);
        Assert.Equal(body, sent.Body);                                   // 精确字节，不被重新序列化
        Assert.Equal("application/json", sent.Header("Content-Type"));
    }

    [Fact]
    public async Task Post_CarriesBody()
    {
        const string body = """{"source":6,"sourceId":[228908],"op":1,"uid":50303440}""";

        await Send(
            new BodianRequest { Path = "service/collect", Verb = BodianHttpVerb.Post, JsonBody = body },
            BodianJsonContext.Default.CheckRightDto);

        Assert.Equal("POST", _handler.LastRequest.Method);
        Assert.Equal(body, _handler.LastRequest.Body);
    }

    // ── 签名端到端 ──────────────────────────────────────────────────────────

    /// <summary>
    /// **黄金用例的端到端版本。**
    /// </summary>
    /// <remarks>
    /// 冻结时间戳后，发出的 URL 必须与 <c>fixtures/sign-golden.json</c> 记录的
    /// <c>seedQuery</c> 加算出的 <c>sign</c> **逐字符相同**。
    /// 这一条同时锁住 query 顺序、WHATWG 编码、盐的位置、path 形态、body 参与签名这五件事——
    /// 任何一处改动都会让它红。
    /// <para>
    /// 它是 characterization，不是「已被服务端验证」——见 <c>SignerTests</c> 的说明。
    /// </para>
    /// </remarks>
    [Fact]
    public async Task SignedRequest_MatchesGoldenFixture_Characterization()
    {
        var golden = JsonDocument.Parse(Fixtures.Read("sign-golden.json")).RootElement;
        var expectedSign = golden.GetProperty("sign").GetString()!;
        var expectedSeedQuery = golden.GetProperty("seedQuery").GetString()!;
        var body = golden.GetProperty("body").GetString()!;

        await Send(
            new BodianRequest
            {
                Path = "play/music/v2/checkRight",
                Verb = BodianHttpVerb.Get,
                Query = [new("musicId", "228908")],
                JsonBody = body,
                Signed = true,
            },
            BodianJsonContext.Default.CheckRightDto);

        var url = _handler.LastRequest.Url;
        var query = url[(url.IndexOf('?') + 1)..];

        Assert.StartsWith("https://bd-api.kuwo.cn/api/play/music/v2/checkRight?", url);
        Assert.Contains(expectedSign, query);
        // 签名前的形态与 fixture 记录的一致（timestamp 被冻结，所以可逐字比对）
        Assert.Contains(expectedSeedQuery.TrimEnd('='), query.Replace($"sign={expectedSign}", "sign="));
    }

    [Fact]
    public async Task UnsignedRequest_HasNoTimestampOrSign()
    {
        await Send(
            new BodianRequest { Path = "service/music/info", Query = [new("musicId", "228908")] },
            BodianJsonContext.Default.TrackDto);

        var url = _handler.LastRequest.Url;

        Assert.DoesNotContain("timestamp=", url);
        Assert.DoesNotContain("sign=", url);
    }

    // ── 信封解析 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Envelope_ParsesRealFixture()
    {
        _handler.Responder = _ => ReplayHandler.Json(Fixtures.Read("music-info-228908.json"));

        var envelope = await Send(
            new BodianRequest { Path = "service/music/info", Query = [new("musicId", "228908")] },
            BodianJsonContext.Default.TrackDto);

        Assert.Equal(200, envelope.Code);
        Assert.True(envelope.IsSuccess);
        Assert.Equal("success", envelope.Message);
        Assert.False(string.IsNullOrWhiteSpace(envelope.RequestId));
        Assert.Equal(228908, envelope.Data!.Id);
    }

    /// <summary>业务码非 200 时 <c>data</c> 可能整个不存在，也要能正确取到 code 与 msg。</summary>
    [Theory]
    [InlineData("audiourl-anon-20018.json", 20018)]
    [InlineData("music-info-10250281307392909.json", 20012)]
    public async Task Envelope_HandlesResponsesWithoutData(string fixture, int expectedCode)
    {
        _handler.Responder = _ => ReplayHandler.Json(Fixtures.Read(fixture));

        var exception = await Assert.ThrowsAsync<BodianApiException>(() => Send(
            new BodianRequest { Path = "play/music/v2/audioUrl" },
            BodianJsonContext.Default.AudioUrlDto));

        Assert.Equal(expectedCode, exception.RawCode);
        Assert.False(string.IsNullOrWhiteSpace(exception.ServerMessage));
        Assert.False(string.IsNullOrWhiteSpace(exception.RawBody));   // 保留原文供排查
    }

    [Fact]
    public async Task ErrorCodes_MapToEnum()
    {
        Assert.Equal(BodianErrorCode.Success, BodianErrorCodeExtensions.FromRaw(200));
        Assert.Equal(BodianErrorCode.MissingHeaders, BodianErrorCodeExtensions.FromRaw(402));
        Assert.Equal(BodianErrorCode.SignInvalid, BodianErrorCodeExtensions.FromRaw(439));
        Assert.Equal(BodianErrorCode.NeedAuth, BodianErrorCodeExtensions.FromRaw(11012));
        Assert.Equal(BodianErrorCode.LoginPending, BodianErrorCodeExtensions.FromRaw(11027));
        Assert.Equal(BodianErrorCode.TrackOffline, BodianErrorCodeExtensions.FromRaw(20012));
        Assert.Equal(BodianErrorCode.NotPlayable, BodianErrorCodeExtensions.FromRaw(20018));
        Assert.Equal(BodianErrorCode.Unknown, BodianErrorCodeExtensions.FromRaw(99999));
    }

    /// <summary>未知码不能丢——原始值必须保留，否则排查时无从下手。</summary>
    [Fact]
    public async Task UnknownCode_IsPreserved()
    {
        _handler.Responder = _ => ReplayHandler.Json("""{"code":88888,"msg":"something new","data":null}""");

        var exception = await Assert.ThrowsAsync<BodianApiException>(() => Send(
            new BodianRequest { Path = "service/whatever" },
            BodianJsonContext.Default.TrackDto));

        Assert.Equal(88888, exception.RawCode);
        Assert.Equal(BodianErrorCode.Unknown, exception.ErrorCode);
        Assert.Equal("something new", exception.ServerMessage);
    }

    // ── 11012 / 11027 ───────────────────────────────────────────────────────

    /// <summary>
    /// <c>11012</c> 必须清会话并触发事件，**同时**抛出正确的错误码。
    /// </summary>
    [Fact]
    public async Task NeedAuth_ClearsSessionAndThrows()
    {
        _session.Set("50303440", "tok_secret");
        var revisionBefore = _session.Revision;
        var cleared = 0;
        _session.Cleared += (_, _) => cleared++;

        _handler.Responder = _ => ReplayHandler.Json("""{"code":11012,"msg":"需要登录","data":null}""");

        var exception = await Assert.ThrowsAsync<BodianApiException>(() => Send(
            new BodianRequest { Path = "service/collect" },
            BodianJsonContext.Default.TrackDto));

        Assert.Equal(BodianErrorCode.NeedAuth, exception.ErrorCode);
        Assert.False(_session.IsAuthenticated);
        Assert.Equal("-1", _session.Uid);
        Assert.Equal(string.Empty, _session.Token);
        Assert.Equal(revisionBefore + 1, _session.Revision);
        Assert.Equal(1, cleared);
    }

    /// <summary>
    /// <c>11027</c>（扫码已扫未确认）是登录轮询的正常中间态：**不能抛异常**。
    /// </summary>
    [Fact]
    public async Task LoginPending_IsAcceptedAndDoesNotThrow()
    {
        _handler.Responder = _ => ReplayHandler.Json("""{"code":11027,"msg":"待确认","data":null}""");

        var envelope = await Send(
            new BodianRequest
            {
                Path = "ucenter/login/qrCodeStatus",
                AcceptedCodes = [BodianErrorCode.LoginPending],
            },
            BodianJsonContext.Default.TrackDto);

        Assert.Equal(11027, envelope.Code);
        Assert.False(envelope.IsSuccess);
        Assert.Equal(BodianErrorCode.LoginPending, envelope.ErrorCode);
    }

    /// <summary>不在 AcceptedCodes 里的码照样抛。</summary>
    [Fact]
    public async Task LoginPending_ThrowsWhenNotAccepted()
    {
        _handler.Responder = _ => ReplayHandler.Json("""{"code":11027,"msg":"待确认","data":null}""");

        await Assert.ThrowsAsync<BodianApiException>(() => Send(
            new BodianRequest { Path = "ucenter/login/qrCodeStatus" },
            BodianJsonContext.Default.TrackDto));
    }

    // ── 边界 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task OversizedResponse_IsRejected()
    {
        var oversized = new string('x', (int)_options.MaxResponseBytes + 1024);
        _handler.Responder = _ => ReplayHandler.Json($$"""{"code":200,"msg":"{{oversized}}"}""");

        var exception = await Assert.ThrowsAsync<BodianPayloadTooLargeException>(() => Send(
            new BodianRequest { Path = "service/music/info" },
            BodianJsonContext.Default.TrackDto));

        Assert.Equal(_options.MaxResponseBytes, exception.LimitBytes);
    }

    [Fact]
    public async Task ResponseJustUnderLimit_IsAccepted()
    {
        var padding = new string('x', (int)_options.MaxResponseBytes - 4096);
        _handler.Responder = _ => ReplayHandler.Json($$"""{"code":200,"msg":"ok","pad":"{{padding}}"}""");

        var envelope = await Send(
            new BodianRequest { Path = "service/music/info" },
            BodianJsonContext.Default.TrackDto);

        Assert.Equal(200, envelope.Code);
    }

    /// <summary>超时必须由本层的 <c>RequestTimeout</c> 触发，且要与「调用方取消」区分开。</summary>
    [Fact]
    public async Task Timeout_ThrowsTimeoutException()
    {
        using var transport = new BodianHttpTransport(
            _handler,
            _options with { RequestTimeout = TimeSpan.FromMilliseconds(80) },
            _session,
            new FakeDeviceIdentity());

        _handler.Delay = TimeSpan.FromSeconds(5);

        await Assert.ThrowsAsync<TimeoutException>(() => transport.SendAsync(
            new BodianRequest { Path = "service/music/info" },
            BodianJsonContext.Default.TrackDto,
            TestContext.Current.CancellationToken));
    }

    /// <summary>调用方主动取消时应当原样抛 <see cref="OperationCanceledException"/>，不要伪装成超时。</summary>
    [Fact]
    public async Task CallerCancellation_IsNotReportedAsTimeout()
    {
        _handler.Delay = TimeSpan.FromSeconds(5);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));

        // 这里刻意直接调 transport 并传**自己的**令牌，验证它不会被误报成 TimeoutException。
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _transport.SendAsync(
            new BodianRequest { Path = "service/music/info" },
            BodianJsonContext.Default.TrackDto,
            cts.Token));
    }

    /// <summary>非 JSON 响应（网关错误页）要保留原文供排查，不能吞成「未知错误」。</summary>
    [Fact]
    public async Task NonJsonResponse_PreservesRawBody()
    {
        _handler.Responder = _ => ReplayHandler.Raw("<html><body>502 Bad Gateway</body></html>", "text/html");

        var exception = await Assert.ThrowsAsync<BodianMalformedResponseException>(() => Send(
            new BodianRequest { Path = "service/music/info" },
            BodianJsonContext.Default.TrackDto));

        Assert.Contains("502 Bad Gateway", exception.RawBody);
    }

    [Fact]
    public async Task HttpErrorStatus_ThrowsHttpRequestException()
    {
        _handler.Responder = _ => ReplayHandler.Json("{}", HttpStatusCode.InternalServerError);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => Send(
            new BodianRequest { Path = "service/music/info" },
            BodianJsonContext.Default.TrackDto));

        Assert.Equal(HttpStatusCode.InternalServerError, exception.StatusCode);
    }

    // ── handler 配置 ────────────────────────────────────────────────────────

    /// <summary>
    /// gzip 解压是 <see cref="SocketsHttpHandler"/> 自身的行为，回放 handler 在解压链之上**测不到**。
    /// 诚实的做法是断言配置正确，真正的 gzip 验证留到 P2 的首次真实请求——
    /// 而不是起一个本机回环去测「回环」。
    /// </summary>
    [Fact]
    public void CreateHandler_ConfiguresDecompressionAndPooling()
    {
        var handler = BodianHttpTransport.CreateHandler(_options);

        Assert.Equal(DecompressionMethods.All, handler.AutomaticDecompression);
        Assert.Equal(_options.PooledConnectionLifetime, handler.PooledConnectionLifetime);
        Assert.Null(handler.Proxy);
    }

    [Fact]
    public void CreateHandler_ConfiguresProxy()
    {
        var proxy = new Uri("http://127.0.0.1:7890");
        var handler = BodianHttpTransport.CreateHandler(_options with { Proxy = proxy });

        Assert.True(handler.UseProxy);
        Assert.NotNull(handler.Proxy);
    }

    // ── 会话版本 ────────────────────────────────────────────────────────────

    /// <summary>信封上带着收到响应时的会话版本，调用方据此丢弃「切号后迟到的响应」。</summary>
    [Fact]
    public async Task Envelope_CarriesSessionRevision()
    {
        _session.Set("50303440", "tok");
        var expected = _session.Revision;

        var envelope = await Send(
            new BodianRequest { Path = "service/music/info" },
            BodianJsonContext.Default.TrackDto);

        Assert.Equal(expected, envelope.SessionRevision);
    }

    [Fact]
    public void Session_RevisionAdvancesOnChange_NotOnRedundantClear()
    {
        var session = BodianSession.CreateAnonymous();

        Assert.Equal(0, session.Revision);
        session.Clear();                                       // 本来就是匿名，空操作
        Assert.Equal(0, session.Revision);

        session.Set("1", "t");
        Assert.Equal(1, session.Revision);

        session.Clear();
        Assert.Equal(2, session.Revision);
        Assert.False(session.IsAuthenticated);
    }
}
