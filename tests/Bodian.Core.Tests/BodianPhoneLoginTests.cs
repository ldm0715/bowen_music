using Bodian.Core.Api;
using Bodian.Core.Models.Login;
using Bodian.Core.Services.Implementations;
using Bodian.Core.Tests.Support;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 手机号登录。全部走回放 handler，零真实网络。
/// </summary>
/// <remarks>
/// 协议来源：<c>reverse/findings/16-phone-login.md</c>（实网跑通过一次完整登录）。
/// 与 <see cref="BodianLoginTests"/> 是姐妹文件：两条换会话路径共用落地逻辑，
/// 但请求形状完全不同，所以各自的断言不能互相替代。
/// </remarks>
public sealed class BodianPhoneLoginTests : IDisposable
{
    /// <summary>身份字段一致、token 齐全的正常登录响应（与扫码那条同构）。</summary>
    private const string SuccessResponse = """
        {"code":200,"msg":"success","data":{
          "id":50303440,"bid":50303440,"token":"tok_secret",
          "userInfo":{"id":50303440,"nickname":"小音波"}}}
        """;

    /// <summary>
    /// 全文件的手机号与验证码都用这个合成值。
    /// </summary>
    /// <remarks>
    /// <b>不要替换成真实号码或真实收到的验证码。</b> 这是要进公开仓库的文件，
    /// 手机号是 PII、验证码是登录凭据，两者都不该留下。这里也不打网络，值合不合法无所谓。
    /// </remarks>
    private const string Mobile = "13800138000";

    private readonly ReplayHandler _handler = new();
    private readonly BodianSession _session = BodianSession.CreateAnonymous();
    private readonly InMemoryCredentialStore _credentials = new();
    private readonly BodianHttpTransport _transport;
    private readonly BodianLogin _login;

    public BodianPhoneLoginTests()
    {
        _transport = new BodianHttpTransport(
            _handler,
            new BodianTransportOptions(),
            _session,
            new FakeDeviceIdentity(),
            FixedTimeProvider.Golden);

        _login = new BodianLogin(_transport, _session, _credentials, new LoginOptions(), FixedTimeProvider.Golden);
    }

    public void Dispose() => _transport.Dispose();

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    // ── 发验证码 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 发码是 <b>GET + query</b>，不是 POST body。
    /// </summary>
    /// <remarks>
    /// 做成 POST 会拿到 HTTP 500 而不是业务码，排查成本很高 —— 这条断言守着那个方向。
    /// </remarks>
    [Fact]
    public async Task SendSms_IsGetWithQueryParameters()
    {
        _handler.Responder = _ => ReplayHandler.Json("""{"code":200,"msg":"success"}""");

        var outcome = await _login.SendSmsCodeAsync(Mobile, Ct);

        Assert.IsType<SmsSendOutcome.Sent>(outcome);

        var sent = Assert.Single(_handler.Requests);

        Assert.Equal("GET", sent.Method);
        Assert.Contains("ucenter/code/sendsms", sent.Url);
        Assert.Contains("type=2", sent.Url);
        Assert.Contains($"mobile={Mobile}", sent.Url);
        Assert.Null(sent.Body);
    }

    /// <summary>
    /// 桌面指纹的硬约束。**<c>ver</c> 一旦到 3.5 会让所有请求一起挂在 <c>sign invalid</c> 上**，
    /// 而报错只有那一句，极难定位。
    /// </summary>
    [Fact]
    public async Task SendSms_KeepsDesktopFingerprint()
    {
        _handler.Responder = _ => ReplayHandler.Json("""{"code":200,"msg":"success"}""");

        await _login.SendSmsCodeAsync(Mobile, Ct);

        var sent = Assert.Single(_handler.Requests);

        Assert.Equal("1.1.7", sent.Header("ver"));
        Assert.Equal("win", sent.Header("plat"));
    }

    [Fact]
    public async Task SendSms_ServiceRejection_ReturnsFailed()
    {
        _handler.Responder = _ => ReplayHandler.Json("""{"code":11003,"msg":"短信发送失败"}""");

        var outcome = await _login.SendSmsCodeAsync(Mobile, Ct);

        var failed = Assert.IsType<SmsSendOutcome.Failed>(outcome);
        Assert.Equal(11003, failed.Code);
    }

    /// <summary>发码不碰会话：失败时不能在本地留下任何痕迹。</summary>
    [Fact]
    public async Task SendSms_DoesNotTouchSession()
    {
        _handler.Responder = _ => ReplayHandler.Json("""{"code":11003,"msg":"短信发送失败"}""");

        var changed = 0;
        _login.AccountChanged += (_, _) => changed++;

        await _login.SendSmsCodeAsync(Mobile, Ct);

        Assert.False(_login.IsAuthenticated);
        Assert.Null(_login.Account);
        Assert.Null(_credentials.Load());
        Assert.Equal(0, changed);
    }

    [Fact]
    public async Task SendSms_EmptyMobile_ThrowsBeforeAnyRequest()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _login.SendSmsCodeAsync("", Ct));

        Assert.Empty(_handler.Requests);
    }

    // ── 换取会话 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 手机号的 <c>authType</c> 是 <c>1</c>，字段全明文。
    /// </summary>
    /// <remarks>
    /// 与 <c>BodianLoginTests.Complete_SendsAuthTypeTen</c> 形成对照：扫码是 10、手机号是 1，
    /// 两个值都钉住，防止两条路径互相污染。
    /// <b>安卓端的 <c>encvMobile</c> / <c>encvVerifyCode</c> 加密在桌面端不认</b>，
    /// 所以这里断言的是明文 —— 哪天有人"顺手"加上加密，这条会红。
    /// </remarks>
    [Fact]
    public async Task Login_SendsAuthTypeOneWithPlaintextFields()
    {
        _handler.Responder = _ => ReplayHandler.Json(SuccessResponse);

        await _login.LoginByPhoneAsync(Mobile, "123456", Ct);

        var sent = Assert.Single(_handler.Requests);

        Assert.Equal("POST", sent.Method);
        Assert.Contains("ucenter/users/login", sent.Url);
        Assert.Equal(
            $$"""{"authType":1,"mobile":"{{Mobile}}","verifyCode":"123456"}""",
            sent.Body);
    }

    [Fact]
    public async Task Login_Success_WritesSessionAndCredential()
    {
        _handler.Responder = _ => ReplayHandler.Json(SuccessResponse);

        var changed = 0;
        _login.AccountChanged += (_, _) => changed++;

        var outcome = await _login.LoginByPhoneAsync(Mobile, "123456", Ct);

        var success = Assert.IsType<LoginOutcome.Success>(outcome);
        Assert.Equal("50303440", success.Uid);

        Assert.True(_session.IsAuthenticated);
        Assert.Equal("50303440", _session.Uid);
        Assert.Equal("tok_secret", _session.Token);

        var stored = _credentials.Load();
        Assert.NotNull(stored);
        Assert.Equal("50303440", stored.Uid);
        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task Login_WrongVerifyCode_ReturnsFailedWithoutSession()
    {
        _handler.Responder = _ => ReplayHandler.Json("""{"code":11004,"msg":"验证码错误"}""");

        var outcome = await _login.LoginByPhoneAsync(Mobile, "000000", Ct);

        var failed = Assert.IsType<LoginOutcome.Failed>(outcome);
        Assert.Equal(11004, failed.Code);

        Assert.False(_session.IsAuthenticated);
        Assert.Null(_credentials.Load());
    }

    /// <summary>
    /// <c>11027</c> 在手机号路径上**不是**「已扫未确认」，不该被当成可重试的中间态。
    /// </summary>
    /// <remarks>
    /// 扫码那条会为它重试 5 次（见 <c>BodianLoginTests.Complete_GivesUpAfterMaxAttempts</c>），
    /// 那是轮询语义专有的。手机号是一次性提交，撞上它就该立刻结束 ——
    /// 这里「抛异常」正好证明它没进重试分支，而只发了一次证明没重试。
    /// </remarks>
    [Fact]
    public async Task Login_DoesNotTreatLoginPendingAsRetryable()
    {
        _handler.Responder = _ => ReplayHandler.Json("""{"code":11027,"msg":"账号登录失败"}""");

        await Assert.ThrowsAsync<BodianApiException>(
            () => _login.LoginByPhoneAsync(Mobile, "123456", Ct));

        Assert.Single(_handler.Requests);
    }

    // ── 必须丢弃的分支（与扫码同一条落地路径，所以同一套断言）────────────────

    [Fact]
    public async Task Login_IdentityMismatch_DiscardsEverything()
    {
        _handler.Responder = _ => ReplayHandler.Json("""
            {"code":200,"msg":"success","data":{
              "id":50303440,"bid":99999999,"token":"tok_secret",
              "userInfo":{"id":50303440,"nickname":"别人"}}}
            """);

        var changed = 0;
        _login.AccountChanged += (_, _) => changed++;

        var outcome = await _login.LoginByPhoneAsync(Mobile, "123456", Ct);

        Assert.IsType<LoginOutcome.IdentityMismatch>(outcome);
        Assert.False(_session.IsAuthenticated);
        Assert.Null(_credentials.Load());
        Assert.Equal(0, changed);
    }

    [Fact]
    public async Task Login_InsufficientEvidence_DiscardsEverything()
    {
        _handler.Responder = _ => ReplayHandler.Json(
            """{"code":200,"msg":"success","data":{"id":50303440,"token":"tok_secret"}}""");

        var outcome = await _login.LoginByPhoneAsync(Mobile, "123456", Ct);

        Assert.IsType<LoginOutcome.IdentityMismatch>(outcome);
        Assert.False(_session.IsAuthenticated);
        Assert.Null(_credentials.Load());
    }

    [Fact]
    public async Task Login_MissingToken_DiscardsEverything()
    {
        _handler.Responder = _ => ReplayHandler.Json(
            """{"code":200,"msg":"success","data":{"id":50303440,"bid":50303440}}""");

        var outcome = await _login.LoginByPhoneAsync(Mobile, "123456", Ct);

        Assert.IsType<LoginOutcome.InvalidResponse>(outcome);
        Assert.False(_session.IsAuthenticated);
        Assert.Null(_credentials.Load());
    }
}
