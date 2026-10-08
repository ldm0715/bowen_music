using Bodian.Core.Api;
using Bodian.Core.Models.Account;
using Bodian.Core.Models.Login;
using Bodian.Core.Services.Abstractions;
using Bodian.Core.Services.Implementations;
using Bodian.Core.Tests.Support;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 账号快捷切换：切到记住的账号、忘掉账号、以及「被服务端判死」的记账。
/// </summary>
/// <remarks>
/// 切换路径**不发任何请求**，这一点用 <c>_handler.Requests</c> 直接断言。
/// </remarks>
public sealed class BodianLoginSwitchTests
{
    private const string SuccessResponse = """
        {"code":200,"msg":"success","data":{
          "id":50303440,"bid":50303440,"token":"tok_secret",
          "userInfo":{"id":50303440,"nickname":"小音波"}}}
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateTimeOffset Now => FixedTimeProvider.Golden.GetUtcNow();

    private readonly ReplayHandler _handler = new();
    private readonly BodianSession _session = BodianSession.CreateAnonymous();
    private readonly InMemoryCredentialStore _credentials = new();
    private readonly FakeRememberedAccountsStore _remembered = new();
    private readonly BodianLogin _login;

    public BodianLoginSwitchTests() => _login = NewLogin(_credentials, _session);

    private BodianLogin NewLogin(ICredentialStore credentials, BodianSession session) =>
        new(
            new BodianHttpTransport(
                _handler, new BodianTransportOptions(), session, new FakeDeviceIdentity(), FixedTimeProvider.Golden),
            session,
            credentials,
            // 测试全速跑：间隔归零，不真的等 2 秒。
            new LoginOptions { PollInterval = TimeSpan.Zero, ExchangeRetryInterval = TimeSpan.Zero },
            FixedTimeProvider.Golden,
            rememberedAccounts: _remembered);

    private static BodianCredential Credential(string uid, string? nickname = null) =>
        new(uid, $"tok_{uid}", nickname ?? $"账号{uid}");

    private void Remember(string uid) => _remembered.Remember(Credential(uid), Now);

    private static QrCodeChallenge Challenge() =>
        new("KEY-ABC", new Uri("https://x.invalid/"), DateTimeOffset.UnixEpoch);

    // ── 切换 ────────────────────────────────────────────────────────────────

    [Fact]
    public void SwitchTo_UnknownUid_ReturnsFalse_AndLeavesTheSessionAlone()
    {
        Remember("111");

        Assert.False(_login.SwitchTo("999"));

        Assert.False(_session.IsAuthenticated);
        Assert.Null(_login.Account);
    }

    [Fact]
    public void SwitchTo_RememberedUid_AdoptsItWithoutTouchingTheNetwork()
    {
        _remembered.Remember(
            new BodianCredential("111", "tok_111", "一号", "https://x.invalid/1.png", true,
                DateTimeOffset.UnixEpoch.AddYears(1), VipBadgeKind.Standard),
            Now);

        Assert.True(_login.SwitchTo("111"));

        // 不重新认证：一个请求都不该发出去。
        Assert.Empty(_handler.Requests);

        Assert.Equal("111", _session.Uid);
        Assert.Equal("tok_111", _session.Token);
        Assert.Equal("一号", _login.Account?.Nickname);
        Assert.Equal(VipBadgeKind.Standard, _login.Account?.VipBadge);

        // session.dat 要跟着指向新账号，否则下次启动会恢复到上一个。
        Assert.Equal("111", _credentials.Load()?.Uid);
    }

    [Fact]
    public void SwitchTo_RaisesAccountChanged()
    {
        Remember("111");

        var raised = 0;
        _login.AccountChanged += (_, _) => raised++;

        _login.SwitchTo("111");

        Assert.Equal(1, raised);
    }

    [Fact]
    public void SwitchTo_TheCurrentAccount_IsANoOp()
    {
        Remember("111");
        _login.SwitchTo("111");

        var raised = 0;
        _login.AccountChanged += (_, _) => raised++;

        Assert.True(_login.SwitchTo("111"));

        // 切到自己不发通知：白推一次会让播放队列跟着白换一遍。
        Assert.Equal(0, raised);
    }

    [Fact]
    public void SwitchTo_CarriesTheStoredVipExpiry()
    {
        var expires = DateTimeOffset.UnixEpoch.AddYears(2);
        _remembered.Remember(new BodianCredential("111", "tok", "一号", IsVip: true, VipExpiresAt: expires), Now);

        _login.SwitchTo("111");

        Assert.Equal(expires, _login.Account?.VipExpiresAt);
    }

    // ── 登出与清单 ──────────────────────────────────────────────────────────

    [Fact]
    public void SignOut_KeepsTheRememberedList()
    {
        Remember("111");
        _login.SwitchTo("111");

        _login.SignOut();

        Assert.False(_session.IsAuthenticated);
        Assert.Null(_credentials.Load());

        // 这条是「快捷切换」的前提：登出只清当前会话，凭据留着才切得回来。
        var entry = Assert.Single(_login.RememberedAccounts);
        Assert.Equal("111", entry.Credential.Uid);
    }

    [Fact]
    public void Forget_RemovesTheAccount()
    {
        Remember("111");

        _login.Forget("111");

        Assert.Empty(_login.RememberedAccounts);
    }

    [Fact]
    public void Forget_DoesNotDisturbTheCurrentSession()
    {
        Remember("111");
        _login.SwitchTo("111");

        _login.Forget("111");

        // 只从清单里忘掉凭据，当前会话照旧。
        Assert.True(_session.IsAuthenticated);
        Assert.Equal("111", _session.Uid);
    }

    // ── 被服务端判死 ────────────────────────────────────────────────────────

    [Fact]
    public void ServerClearingTheSession_MarksThatAccountStale()
    {
        Remember("111");
        _login.SwitchTo("111");

        _session.NotifyUnauthorized();

        Assert.True(_login.LastSessionEndWasServerInitiated);
        Assert.True(_login.IsStale("111"));

        // 清单里的凭据还在（用户还能看到它、并选择重新登录），只是标成失效。
        Assert.Single(_login.RememberedAccounts);
    }

    [Fact]
    public void SigningOutLocally_IsNotReportedAsAServerExpiry()
    {
        Remember("111");
        _login.SwitchTo("111");

        _login.SignOut();

        // 主动登出是用户自己的动作，不必解释；说成「登录已失效」会让人以为出了问题。
        Assert.False(_login.LastSessionEndWasServerInitiated);
        Assert.False(_login.IsStale("111"));
    }

    [Fact]
    public async Task LoggingInAgain_ClearsTheStaleMark()
    {
        Remember("50303440");
        _login.SwitchTo("50303440");
        _session.NotifyUnauthorized();
        Assert.True(_login.IsStale("50303440"));

        // 重新登录拿到一份全新的 token：失效标记到此为止。
        _handler.Responder = _ => ReplayHandler.Json(SuccessResponse);
        await _login.CompleteAsync(Challenge(), Ct);

        Assert.False(_login.IsStale("50303440"));
        Assert.False(_login.LastSessionEndWasServerInitiated);
    }

    [Fact]
    public void Forget_AlsoClearsTheStaleMark()
    {
        Remember("111");
        _login.SwitchTo("111");
        _session.NotifyUnauthorized();

        _login.Forget("111");

        Assert.False(_login.IsStale("111"));
    }

    // ── 记账与自愈 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task LoggingIn_RemembersTheAccount()
    {
        _handler.Responder = _ => ReplayHandler.Json(SuccessResponse);

        await _login.CompleteAsync(Challenge(), Ct);

        var entry = Assert.Single(_login.RememberedAccounts);
        Assert.Equal("50303440", entry.Credential.Uid);
        Assert.Equal("tok_secret", entry.Credential.Token);
    }

    [Fact]
    public void RestoringASession_AlsoRemembersIt()
    {
        // 探针（或旧版本）只写了 session.dat，没写清单 —— 不补记的话，
        // 用户会遇到「明明登录着，切换列表里却找不到当前账号」。
        _credentials.Save(Credential("111"));

        Assert.True(_login.TryRestorePersistedSession());

        var entry = Assert.Single(_login.RememberedAccounts);
        Assert.Equal("111", entry.Credential.Uid);
    }

    [Fact]
    public void RestoringASession_DoesNotRewriteTheCredentialFile()
    {
        var credentials = new CountingCredentialStore();
        credentials.Seed(Credential("111"));

        var login = NewLogin(credentials, BodianSession.CreateAnonymous());

        Assert.True(login.TryRestorePersistedSession());

        // 恢复那条路不落盘：Save 会抛（IO / DPAPI），为一次内容相同的写入把
        // 「启动时能恢复会话」搭进去不值。
        Assert.Equal(0, credentials.SaveCount);
    }

    [Fact]
    public void RestoredVipWithoutAKnownBadge_FallsBackToBig()
    {
        // VipBadge 是后加的字段：老凭据里没有它，读出来是 None。直接采用会让老会话一个图标都不显示。
        _credentials.Save(new BodianCredential("111", "tok", "老凭据", IsVip: true, VipBadge: VipBadgeKind.None));

        Assert.True(_login.TryRestorePersistedSession());

        Assert.True(_login.Account?.IsVip);
        Assert.Equal(VipBadgeKind.Big, _login.Account?.VipBadge);
    }

    [Fact]
    public void NotSignedIn_HasNoAccountAndNoStaleMarks()
    {
        Assert.False(_login.IsAuthenticated);
        Assert.Null(_login.Account);
        Assert.Empty(_login.RememberedAccounts);
        Assert.False(_login.LastSessionEndWasServerInitiated);
    }

    /// <summary>记账版凭据存储：用来断言「恢复会话那条路不落盘」。</summary>
    private sealed class CountingCredentialStore : ICredentialStore
    {
        private readonly InMemoryCredentialStore _inner = new();

        public int SaveCount { get; private set; }

        public BodianCredential? Load() => _inner.Load();

        public void Save(BodianCredential credential)
        {
            SaveCount++;
            _inner.Save(credential);
        }

        public bool Clear() => _inner.Clear();

        /// <summary>预置一份凭据，<b>不计入</b> <see cref="SaveCount"/>。</summary>
        public void Seed(BodianCredential credential) => _inner.Save(credential);
    }
}
