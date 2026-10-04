using Bodian.Core.Api;
using Bodian.Core.Models.Account;
using Bodian.Core.Models.Login;
using Bodian.Core.Services.Abstractions;
using Bodian.Core.Services.Implementations;
using Bodian.Core.Tests.Support;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 扫码登录。全部走回放 handler，零真实网络。
/// </summary>
/// <remarks>
/// 响应体是**内联**的，不用 <c>fixtures/login-users-login.json</c> —— 那份 fixture 的身份字段
/// 被脱敏成了 <c>"&lt;redacted&gt;"</c>，解析出来是 null，走不到「身份一致」这条路径。
/// </remarks>
public sealed class BodianLoginTests : IDisposable
{
    private const string CreateResponse =
        """{"code":200,"msg":"success","data":{"qrCode":"KEY-ABC"}}""";

    private const string WaitingResponse =
        """{"code":200,"msg":"success","data":{"status":1}}""";

    /// <summary>身份字段一致、token 齐全的正常登录响应。</summary>
    private const string SuccessResponse = """
        {"code":200,"msg":"success","data":{
          "id":50303440,"bid":50303440,"token":"tok_secret",
          "userInfo":{"id":50303440,"nickname":"小音波"}}}
        """;

    private readonly ReplayHandler _handler = new();
    private readonly BodianSession _session = BodianSession.CreateAnonymous();
    private readonly InMemoryCredentialStore _credentials = new();
    private readonly BodianHttpTransport _transport;
    private readonly BodianLogin _login;

    public BodianLoginTests()
    {
        _transport = new BodianHttpTransport(
            _handler,
            new BodianTransportOptions(),
            _session,
            new FakeDeviceIdentity(),
            FixedTimeProvider.Golden);

        _login = new BodianLogin(
            _transport,
            _session,
            _credentials,
            new LoginOptions
            {
                // 测试全速跑：间隔归零，不真的等 2 秒。
                PollInterval = TimeSpan.Zero,
                ExchangeRetryInterval = TimeSpan.Zero,
            },
            FixedTimeProvider.Golden);
    }

    public void Dispose() => _transport.Dispose();

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    // ── 二维码 ──────────────────────────────────────────────────────────────

    /// <summary>二维码里放的必须是**完整落地页表单**，不是 key 本身。</summary>
    [Fact]
    public async Task CreateChallenge_UsesTheFullLandingPageForm()
    {
        _handler.Responder = _ => ReplayHandler.Json(CreateResponse);

        var challenge = await _login.CreateChallengeAsync(Ct);

        Assert.Equal("KEY-ABC", challenge.Key);
        Assert.Equal(
            "https://bodian-oia.kuwo.cn/bodian/download.html?pageName=login_pc&pt=3&id=KEY-ABC",
            challenge.LandingPage.ToString());

        // 这两条是实测踩过的坑：放 key 本身、或 login_pc?qrCode=... 都扫不出正确结果。
        Assert.NotEqual(challenge.Key, challenge.LandingPage.ToString());
        Assert.DoesNotContain("qrCode=", challenge.LandingPage.ToString());
    }

    [Fact]
    public async Task CreateChallenge_RequestShape()
    {
        _handler.Responder = _ => ReplayHandler.Json(CreateResponse);

        await _login.CreateChallengeAsync(Ct);

        var sent = Assert.Single(_handler.Requests);

        Assert.Equal("GET", sent.Method);
        Assert.Contains("ucenter/login/qrCode", sent.Url);
        Assert.Contains("sign=", sent.Url);
        Assert.Contains("uid=-1", sent.Url);   // 匿名
    }

    [Fact]
    public async Task CreateChallenge_WithoutKey_Throws()
    {
        _handler.Responder = _ => ReplayHandler.Json("""{"code":200,"msg":"success","data":{}}""");

        await Assert.ThrowsAsync<BodianApiException>(() => _login.CreateChallengeAsync(Ct));
    }

    // ── 轮询 ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1, QrScanStatus.Waiting)]
    [InlineData(2, QrScanStatus.Expired)]
    [InlineData(3, QrScanStatus.Confirmed)]
    [InlineData(9, QrScanStatus.Unknown)]
    public async Task Poll_MapsServerStatus(int serverStatus, QrScanStatus expected)
    {
        // 这里用普通插值字符串：JSON 结尾的连续右大括号在 $$ 原始字符串里写不出来。
        _handler.Responder = _ =>
            ReplayHandler.Json($"{{\"code\":200,\"msg\":\"success\",\"data\":{{\"status\":{serverStatus}}}}}");

        var status = await _login.PollAsync(new QrCodeChallenge("KEY-ABC", new Uri("https://x.invalid/"), DateTimeOffset.UnixEpoch), Ct);

        Assert.Equal(expected, status);
    }

    [Fact]
    public async Task Poll_PutsKeyInQuery()
    {
        _handler.Responder = _ => ReplayHandler.Json(WaitingResponse);

        await _login.PollAsync(
            new QrCodeChallenge("KEY-ABC", new Uri("https://x.invalid/"), DateTimeOffset.UnixEpoch),
            Ct);

        Assert.Contains("qrCode=KEY-ABC", _handler.LastRequest.Url);
        Assert.Equal("GET", _handler.LastRequest.Method);
    }

    // ── 换取会话 ────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>authType</c> 必须是 10。写成 9 会让服务端返回**别人**的会话 —— 这条断言守着那个历史坑。
    /// </summary>
    [Fact]
    public async Task Complete_SendsAuthTypeTen()
    {
        _handler.Responder = _ => ReplayHandler.Json(SuccessResponse);

        await _login.CompleteAsync(Challenge(), Ct);

        var sent = Assert.Single(_handler.Requests);

        Assert.Equal("POST", sent.Method);
        Assert.Equal("""{"authType":10,"qrCode":"KEY-ABC"}""", sent.Body);
    }

    /// <summary>11027 是「已扫码未确认」的中间态，要重试而不是报错。</summary>
    [Fact]
    public async Task Complete_RetriesOnLoginPending()
    {
        var attempts = 0;

        _handler.Responder = _ =>
        {
            attempts++;
            return ReplayHandler.Json(
                attempts < 3
                    ? """{"code":11027,"msg":"waiting"}"""
                    : SuccessResponse);
        };

        var outcome = await _login.CompleteAsync(Challenge(), Ct);

        Assert.IsType<LoginOutcome.Success>(outcome);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task Complete_GivesUpAfterMaxAttempts()
    {
        var attempts = 0;

        _handler.Responder = _ =>
        {
            attempts++;
            return ReplayHandler.Json("""{"code":11027,"msg":"waiting"}""");
        };

        var outcome = await _login.CompleteAsync(Challenge(), Ct);

        Assert.IsType<LoginOutcome.Failed>(outcome);
        Assert.Equal(5, attempts);   // MaxExchangeAttempts 的默认值
    }

    [Fact]
    public async Task Complete_Success_WritesSessionAndCredential()
    {
        _handler.Responder = _ => ReplayHandler.Json(SuccessResponse);

        var changed = 0;
        _login.AccountChanged += (_, _) => changed++;

        var outcome = await _login.CompleteAsync(Challenge(), Ct);

        var success = Assert.IsType<LoginOutcome.Success>(outcome);
        Assert.Equal("50303440", success.Uid);
        Assert.Equal("小音波", success.Nickname);

        Assert.True(_session.IsAuthenticated);
        Assert.Equal("50303440", _session.Uid);
        Assert.Equal("tok_secret", _session.Token);
        Assert.True(_login.IsAuthenticated);
        Assert.Equal("小音波", _login.Nickname);

        var stored = _credentials.Load();
        Assert.NotNull(stored);
        Assert.Equal("50303440", stored.Uid);
        Assert.Equal(1, changed);
    }

    // ── 必须丢弃的分支 ──────────────────────────────────────────────────────

    /// <summary>身份字段对不上时**绝不能落盘、绝不能改进程内的会话**。</summary>
    [Fact]
    public async Task Complete_IdentityMismatch_DiscardsEverything()
    {
        _handler.Responder = _ => ReplayHandler.Json("""
            {"code":200,"msg":"success","data":{
              "id":50303440,"bid":99999999,"token":"tok_secret",
              "userInfo":{"id":50303440,"nickname":"别人"}}}
            """);

        var changed = 0;
        _login.AccountChanged += (_, _) => changed++;

        var outcome = await _login.CompleteAsync(Challenge(), Ct);

        Assert.IsType<LoginOutcome.IdentityMismatch>(outcome);
        Assert.False(_session.IsAuthenticated);
        Assert.Null(_credentials.Load());
        Assert.Equal(0, changed);
    }

    /// <summary>只有一个身份字段时证据不足，同样丢弃。</summary>
    [Fact]
    public async Task Complete_InsufficientEvidence_DiscardsEverything()
    {
        _handler.Responder = _ => ReplayHandler.Json("""
            {"code":200,"msg":"success","data":{"id":50303440,"token":"tok_secret"}}
            """);

        var outcome = await _login.CompleteAsync(Challenge(), Ct);

        Assert.IsType<LoginOutcome.IdentityMismatch>(outcome);
        Assert.False(_session.IsAuthenticated);
        Assert.Null(_credentials.Load());
    }

    [Fact]
    public async Task Complete_MissingToken_DiscardsEverything()
    {
        _handler.Responder = _ => ReplayHandler.Json("""
            {"code":200,"msg":"success","data":{"id":50303440,"bid":50303440}}
            """);

        var outcome = await _login.CompleteAsync(Challenge(), Ct);

        Assert.IsType<LoginOutcome.InvalidResponse>(outcome);
        Assert.False(_session.IsAuthenticated);
        Assert.Null(_credentials.Load());
    }

    // ── 会话生命周期 ────────────────────────────────────────────────────────

    [Fact]
    public void TryRestore_ReadsFromStore()
    {
        _credentials.Save(new BodianCredential("50303440", "tok_secret", "小音波"));

        Assert.True(_login.TryRestorePersistedSession());
        Assert.True(_session.IsAuthenticated);
        Assert.Equal("50303440", _session.Uid);
        Assert.Equal("小音波", _login.Nickname);
    }

    [Fact]
    public void TryRestore_WithoutCredential_LeavesSessionAnonymous()
    {
        Assert.False(_login.TryRestorePersistedSession());
        Assert.False(_session.IsAuthenticated);
        Assert.False(_login.IsAuthenticated);
    }

    /// <summary>
    /// <c>VipBadge</c> 是后加的字段，老凭据里没有它（读出来是 <c>None</c>）。
    /// 直接采用会让老会话**一个会员图标都不显示**，所以按「是会员但档位认不出来」退回大会员。
    /// </summary>
    [Fact]
    public void TryRestore_MemberWithoutBadge_FallsBackToBig()
    {
        _credentials.Save(new BodianCredential("50303440", "tok_secret", "小音波", IsVip: true));

        Assert.True(_login.TryRestorePersistedSession());
        Assert.Equal(VipBadgeKind.Big, _login.Account?.VipBadge);
    }

    /// <summary>非会员不该被这条回落误伤 —— 回落的前提是「确实是会员」。</summary>
    [Fact]
    public void TryRestore_NonMember_StaysWithoutBadge()
    {
        _credentials.Save(new BodianCredential("50303440", "tok_secret", "小音波", IsVip: false));

        Assert.True(_login.TryRestorePersistedSession());
        Assert.Equal(VipBadgeKind.None, _login.Account?.VipBadge);
    }

    /// <summary>凭据里存了档位就照用，不要被回落覆盖。</summary>
    [Fact]
    public void TryRestore_UsesStoredBadge()
    {
        _credentials.Save(new BodianCredential(
            "50303440", "tok_secret", "小音波", IsVip: true, VipBadge: VipBadgeKind.Welfare));

        Assert.True(_login.TryRestorePersistedSession());
        Assert.Equal(VipBadgeKind.Welfare, _login.Account?.VipBadge);
    }

    [Fact]
    public void TryRestore_IgnoresAnonymousCredential()
    {
        _credentials.Save(new BodianCredential("-1", "", null));

        Assert.False(_login.TryRestorePersistedSession());
    }

    [Fact]
    public void SignOut_ClearsSessionAndCredential()
    {
        _credentials.Save(new BodianCredential("50303440", "tok_secret", "小音波"));
        _login.TryRestorePersistedSession();

        _login.SignOut();

        Assert.False(_session.IsAuthenticated);
        Assert.Null(_credentials.Load());
        Assert.Null(_login.Nickname);
    }

    /// <summary>
    /// 服务端说会话无效（11012）时，磁盘凭据也要清掉 —— 否则下次启动会恢复一个死 token，
    /// 表现为「一启动就掉登录」。
    /// </summary>
    [Fact]
    public void ServerClearingSession_AlsoClearsStoredCredential()
    {
        _credentials.Save(new BodianCredential("50303440", "tok_secret", "小音波"));
        _login.TryRestorePersistedSession();

        _session.NotifyUnauthorized();

        Assert.False(_session.IsAuthenticated);
        Assert.Null(_credentials.Load());
    }

    private static QrCodeChallenge Challenge() =>
        new("KEY-ABC", new Uri("https://x.invalid/"), DateTimeOffset.UnixEpoch);
}
