using System.Text.Json;
using Bodian.Core.Api;
using Bodian.Core.Tests.Support;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 新建歌单的报文形状。零真实网络。
/// </summary>
/// <remarks>
/// 路径、method 与 body 键名来自移动端反汇编（<c>reverse/findings/11-share-playlist-crud.md</c> §2.1），
/// 2026-10-03 实测走通：<b>回执只有 <c>{id}</c></b>、<c>private</c> 与读回的 <c>isPrivate</c> 一一对应、
/// 名字传空服务端也照建（所以非空校验只能靠客户端）。
/// 这里断言的是**发出去的报文**，不是服务端行为。
/// </remarks>
public sealed class PlaylistCreateApiTests : IDisposable
{
    private const string Uid = "36828743";

    private readonly ReplayHandler _handler = new();
    private readonly BodianSession _session = BodianSession.CreateAnonymous();
    private readonly BodianHttpTransport _transport;
    private readonly BodianApi _api;

    public PlaylistCreateApiTests()
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

    private void RespondWith(string json) => _handler.Responder = _ => ReplayHandler.Json(json);

    private static JsonElement BodyOf(CapturedRequest request)
        => JsonDocument.Parse(request.Body!).RootElement;

    // ── 报文 ────────────────────────────────────────────────────────────────

    /// <summary>裸路径 <c>service/playlist</c>，POST、带签名。</summary>
    [Fact]
    public async Task CreatePlaylist_PostsToBarePlaylistPath()
    {
        RespondWith("""{"code":200,"msg":"success","data":{"id":100167230}}""");

        await _api.CreatePlaylistAsync("通勤", isPrivate: false, Ct);

        var request = _handler.LastRequest;
        Assert.Equal("POST", request.Method);
        Assert.Contains("service/playlist?", request.Url, StringComparison.Ordinal);

        // 与两条邻居端点分清楚：那条动歌单里的歌，另一条是列表。
        Assert.DoesNotContain("playlist/music", request.Url, StringComparison.Ordinal);
        Assert.DoesNotContain("userCreate", request.Url, StringComparison.Ordinal);
        Assert.Contains("&sign=", request.Url, StringComparison.Ordinal);
    }

    /// <summary>
    /// 请求键是 <c>private</c>（JSON 布尔），而**不是**响应那个 <c>isPrivate</c>。
    /// </summary>
    /// <remarks>
    /// 写错不会有报错，只会静默建出一个公开歌单 —— 所以这个键名与类型要显式守着。
    /// </remarks>
    [Fact]
    public async Task CreatePlaylist_SendsNameAndBooleanPrivate()
    {
        RespondWith("""{"code":200,"msg":"success","data":{"id":1}}""");

        await _api.CreatePlaylistAsync("通勤", isPrivate: true, Ct);

        var body = BodyOf(_handler.LastRequest);
        Assert.Equal("通勤", body.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.True, body.GetProperty("private").ValueKind);
        Assert.False(body.TryGetProperty("isPrivate", out _));
    }

    /// <summary>关掉开关时发的必须是 <c>false</c>，不是缺省。</summary>
    [Fact]
    public async Task CreatePlaylist_PrivateFalseSendsFalse()
    {
        RespondWith("""{"code":200,"msg":"success","data":{"id":1}}""");

        await _api.CreatePlaylistAsync("通勤", isPrivate: false, Ct);

        Assert.Equal(JsonValueKind.False, BodyOf(_handler.LastRequest).GetProperty("private").ValueKind);
    }

    [Fact]
    public async Task CreatePlaylist_TrimsName()
    {
        RespondWith("""{"code":200,"msg":"success","data":{"id":1}}""");

        await _api.CreatePlaylistAsync("  通勤  ", isPrivate: false, Ct);

        Assert.Equal("通勤", BodyOf(_handler.LastRequest).GetProperty("name").GetString());
    }

    // ── 前置条件 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 空白名字本地就挡掉，**不发请求**。
    /// </summary>
    /// <remarks>
    /// 实测服务端对空名字照建（返回 200 + 新 id），这条校验服务端不会替我们做，
    /// 放过去就是一个无名歌单。
    /// </remarks>
    [Fact]
    public async Task CreatePlaylist_BlankName_ThrowsWithoutRequest()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _api.CreatePlaylistAsync("   ", isPrivate: false, Ct));

        Assert.Empty(_handler.Requests);
    }

    /// <summary>未登录不发请求。</summary>
    [Fact]
    public async Task CreatePlaylist_Anonymous_ThrowsWithoutRequest()
    {
        _session.Clear();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _api.CreatePlaylistAsync("通勤", isPrivate: false, Ct));

        Assert.Empty(_handler.Requests);
    }

    /// <summary>请求在途时换了账号，写请求要作废。</summary>
    [Fact]
    public async Task CreatePlaylist_RejectsStaleAccount()
    {
        _handler.Responder = _ =>
        {
            _session.Set("999", "other-token");
            return ReplayHandler.Json("""{"code":200,"msg":"success","data":{"id":1}}""");
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _api.CreatePlaylistAsync("通勤", isPrivate: false, Ct));
    }

    // ── 回执 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreatePlaylist_ReturnsResponseId()
    {
        RespondWith("""{"code":200,"msg":"success","data":{"id":100167230}}""");

        Assert.Equal(100167230, await _api.CreatePlaylistAsync("通勤", isPrivate: false, Ct));
    }

    /// <summary>
    /// 业务码 200 但没给 id 时抛。
    /// </summary>
    /// <remarks>
    /// 回执只有 id 这一个字段（实测），所以「没有 id」等于「不知道建出来的是什么」——
    /// 返回 0 会让界面插进一行点不开的空歌单。
    /// </remarks>
    [Fact]
    public async Task CreatePlaylist_MissingId_Throws()
    {
        RespondWith("""{"code":200,"msg":"success","data":{}}""");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _api.CreatePlaylistAsync("通勤", isPrivate: false, Ct));
    }
}
