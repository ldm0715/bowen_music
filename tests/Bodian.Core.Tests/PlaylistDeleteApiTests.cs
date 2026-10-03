using System.Text.Json;
using Bodian.Core.Api;
using Bodian.Core.Tests.Support;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 删除歌单的报文形状。零真实网络。
/// </summary>
/// <remarks>
/// 路径与 method 来自 2026-10-03 的实测（<c>DELETE service/playlist</c> +
/// <c>{"playlistIds":[a,b]}</c> → 200、<c>data: {}</c>，一次能删多个）。
/// 这里断言的是**发出去的报文**，不是服务端行为。
/// </remarks>
public sealed class PlaylistDeleteApiTests : IDisposable
{
    private const string Uid = "36828743";
    private const long PlaylistId = 100167230;

    private readonly ReplayHandler _handler = new();
    private readonly BodianSession _session = BodianSession.CreateAnonymous();
    private readonly BodianHttpTransport _transport;
    private readonly BodianApi _api;

    public PlaylistDeleteApiTests()
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

    /// <summary>DELETE 打到裸路径 <c>service/playlist</c> 上，body 是 id 数组。</summary>
    [Fact]
    public async Task DeletePlaylist_SendsDeleteWithIdArray()
    {
        _handler.Responder = _ => ReplayHandler.Json("""{"code":200,"msg":"success","data":{}}""");

        await _api.DeletePlaylistAsync(PlaylistId, Ct);

        var request = _handler.LastRequest;
        Assert.Equal("DELETE", request.Method);
        Assert.Contains("service/playlist?", request.Url, StringComparison.Ordinal);

        // 与两条邻居端点分清楚：那条动歌单里的歌，另一条是列表。
        Assert.DoesNotContain("playlist/music", request.Url, StringComparison.Ordinal);
        Assert.DoesNotContain("userCreate", request.Url, StringComparison.Ordinal);

        var body = JsonDocument.Parse(request.Body!).RootElement;
        Assert.Equal(PlaylistId, body.GetProperty("playlistIds")[0].GetInt64());
    }

    /// <summary>空回执（<c>data: {}</c>）不算失败 —— 删不掉的话服务端给的是业务码，不是这个空对象。</summary>
    [Fact]
    public async Task DeletePlaylist_EmptyDataSucceeds()
    {
        _handler.Responder = _ => ReplayHandler.Json("""{"code":200,"msg":"success","data":{}}""");

        await _api.DeletePlaylistAsync(PlaylistId, Ct);
    }

    /// <summary>非正数 id 本地就挡掉，不发请求。</summary>
    [Fact]
    public async Task DeletePlaylist_NonPositiveId_ThrowsWithoutRequest()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _api.DeletePlaylistAsync(0, Ct));

        Assert.Empty(_handler.Requests);
    }

    /// <summary>未登录不发请求。</summary>
    [Fact]
    public async Task DeletePlaylist_Anonymous_ThrowsWithoutRequest()
    {
        _session.Clear();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _api.DeletePlaylistAsync(PlaylistId, Ct));

        Assert.Empty(_handler.Requests);
    }

    /// <summary>请求在途时换了账号，这次删除要作废。</summary>
    [Fact]
    public async Task DeletePlaylist_RejectsStaleAccount()
    {
        _handler.Responder = _ =>
        {
            _session.Set("999", "other-token");
            return ReplayHandler.Json("""{"code":200,"msg":"success","data":{}}""");
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _api.DeletePlaylistAsync(PlaylistId, Ct));
    }
}
