using Bodian.Core.Api;
using Bodian.Core.Models.Account;
using Bodian.Core.Tests.Support;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 账号统计（关注数 / 听歌时长）。
/// </summary>
/// <remarks>
/// <b>两条接口都未实测</b>，字段名来自反编译。所以这里重点钉两件事：
/// 一是字段名对不上时落成 <c>null</c> 而不是假的 0；二是失败一律返回 <c>null</c> 不抛 ——
/// 调用点是下拉框的 <c>Opening</c>，一个计数拉不到不该让整个下拉框炸掉。
/// </remarks>
public sealed class AccountStatsApiTests : IDisposable
{
    private const string Uid = "50303440";

    private readonly ReplayHandler _handler = new();
    private readonly BodianSession _session = BodianSession.CreateAnonymous();
    private readonly BodianHttpTransport _transport;
    private readonly BodianApi _api;

    public AccountStatsApiTests()
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

    // ── 关注数 ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Metadata_MapsAllCounts()
    {
        SignIn();
        RespondWith("""{"followCount":12,"fansCount":3,"followArtistCount":45,"praised":678}""");

        var metadata = await _api.GetAccountMetadataAsync(Ct);

        Assert.NotNull(metadata);
        Assert.Equal(12, metadata.FollowCount);
        Assert.Equal(3, metadata.FansCount);
        Assert.Equal(45, metadata.FollowArtistCount);
        Assert.Equal(678, metadata.Praised);
    }

    /// <summary>
    /// 字段名只有静态证据 —— <b>猜错时必须是 null，不能是 0</b>，
    /// 否则界面会显示一个看起来很像真的「0 关注」。
    /// </summary>
    [Fact]
    public async Task Metadata_MissingFields_AreNull()
    {
        SignIn();
        RespondWith("""{"somethingElse":1}""");

        var metadata = await _api.GetAccountMetadataAsync(Ct);

        Assert.NotNull(metadata);
        Assert.Null(metadata.FollowCount);
        Assert.Null(metadata.FansCount);
        Assert.Null(metadata.FollowArtistCount);
        Assert.Null(metadata.Praised);
    }

    [Fact]
    public async Task Metadata_RequestShape()
    {
        SignIn();
        RespondWith("""{"followCount":1}""");

        await _api.GetAccountMetadataAsync(Ct);

        var url = _handler.LastRequest.Url;
        Assert.Contains($"service/users/{Uid}/metadata", url, StringComparison.Ordinal);

        // uid 由传输层补，我们不能再自己拼一个 userId。
        Assert.DoesNotContain("userId", url, StringComparison.Ordinal);
    }

    // ── 听歌时长 ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task PlayData_MapsCounts()
    {
        SignIn();
        RespondWith("""{"playcnt":320,"playTime":86400}""");

        var play = await _api.GetAccountPlayDataAsync(Ct);

        Assert.NotNull(play);
        Assert.Equal(320, play.PlayCount);
        Assert.Equal(86400, play.PlaySeconds);
    }

    [Fact]
    public async Task PlayData_MissingFields_AreNull()
    {
        SignIn();
        RespondWith("""{}""");

        var play = await _api.GetAccountPlayDataAsync(Ct);

        Assert.NotNull(play);
        Assert.Null(play.PlayCount);
        Assert.Null(play.PlaySeconds);
    }

    /// <summary>这条必须自己拼 <c>userId</c> —— 传输层补不出这个键。</summary>
    [Fact]
    public async Task PlayData_RequestShape()
    {
        SignIn();
        RespondWith("""{"playcnt":1,"playTime":1}""");

        await _api.GetAccountPlayDataAsync(Ct);

        var url = _handler.LastRequest.Url;
        Assert.Contains("ucenter/playdata/user_data", url, StringComparison.Ordinal);
        Assert.Contains($"userId={Uid}", url, StringComparison.Ordinal);
    }

    // ── 会员档位（实时）─────────────────────────────────────────────────────

    /// <summary>本机大会员账号的形态：档位是大会员，到期取七个字段里最晚的那个。</summary>
    [Fact]
    public async Task VipInfo_MapsBadgeAndLatestExpiry()
    {
        SignIn();
        RespondWith("""
            {"id":36828743,"payInfo":{"isVip":1,"isVipBoolean":true,"vipType":1,"payVipType":1,
            "expireDate":1853575765824,"bigExpireDate":1853575765824,"actExpireDate":1853769599999}}
            """);

        var vip = await _api.GetAccountVipInfoAsync(Ct);

        Assert.NotNull(vip);
        Assert.Equal(VipBadgeKind.Big, vip.Badge);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1853769599999), vip.ExpiresAt);
    }

    [Fact]
    public async Task VipInfo_RequestShape()
    {
        SignIn();
        RespondWith("""{"id":36828743}""");

        await _api.GetAccountVipInfoAsync(Ct);

        Assert.Contains($"ucenter/users/pub/{Uid}", _handler.LastRequest.Url, StringComparison.Ordinal);
    }

    /// <summary>data 里没有 payInfo 时按非会员处理，不是「不知道」。</summary>
    [Fact]
    public async Task VipInfo_NoPayInfo_IsNotMember()
    {
        SignIn();
        RespondWith("""{"id":36828743}""");

        var vip = await _api.GetAccountVipInfoAsync(Ct);

        Assert.NotNull(vip);
        Assert.Equal(VipBadgeKind.None, vip.Badge);
        Assert.Null(vip.ExpiresAt);
    }

    [Fact]
    public async Task VipInfo_TransportFailure_ReturnsNull()
    {
        SignIn();
        _handler.Responder = _ => throw new HttpRequestException("boom");

        Assert.Null(await _api.GetAccountVipInfoAsync(Ct));
    }

    // ── 失败路径 ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Metadata_BusinessError_ReturnsNull()
    {
        SignIn();
        _handler.Responder = _ => ReplayHandler.Json("""{"code":11012,"msg":"会话无效"}""");

        Assert.Null(await _api.GetAccountMetadataAsync(Ct));
    }

    [Fact]
    public async Task PlayData_TransportFailure_ReturnsNull()
    {
        SignIn();
        _handler.Responder = _ => throw new HttpRequestException("boom");

        Assert.Null(await _api.GetAccountPlayDataAsync(Ct));
    }

    /// <summary>匿名会话要当场抛，不能返回 null —— 那会让「没登录」看起来像「拉不到数据」。</summary>
    [Fact]
    public async Task Anonymous_Throws()
    {
        await Assert.ThrowsAsync<BodianNotSignedInException>(() => _api.GetAccountMetadataAsync(Ct));
        await Assert.ThrowsAsync<BodianNotSignedInException>(() => _api.GetAccountPlayDataAsync(Ct));
        await Assert.ThrowsAsync<BodianNotSignedInException>(() => _api.GetAccountVipInfoAsync(Ct));
    }
}
