using System.Text.Json;
using Bowen.Core.Api;
using Bowen.Core.Services.Implementations;
using Bowen.Core.Tests.Support;
using Xunit;

namespace Bowen.Core.Tests;

/// <summary>
/// 播放历史上报的报文形状与失败行为。零真实网络。
/// </summary>
/// <remarks>
/// 路径与 body 来自 5.9.8 arm64 静态反汇编，见 <c>docs/play-history-sync.md</c>。
/// 这里断言的是**发出去的报文**，不是服务端行为。
/// </remarks>
public sealed class PlayHistorySyncTests : IDisposable
{
    private const string Uid = "36828743";
    private const long MusicId = 489568001;

    private readonly ReplayHandler _handler = new();
    private readonly BodianSession _session = BodianSession.CreateAnonymous();
    private readonly BodianHttpTransport _transport;
    private readonly BodianApi _api;
    private readonly PlayHistorySync _sync;

    public PlayHistorySyncTests()
    {
        _transport = new BodianHttpTransport(
            _handler,
            new BodianTransportOptions(),
            _session,
            new FakeDeviceIdentity(),
            FixedTimeProvider.Golden);

        _api = new BodianApi(_transport, _session, new FakeDeviceIdentity());
        _sync = new PlayHistorySync(_api, _session);
    }

    public void Dispose() => _transport.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ── 报文形状 ────────────────────────────────────────────────────────────

    /// <summary><c>sourceType=1</c>，一条 item 只有 <c>sourceId</c>。</summary>
    [Fact]
    public async Task AddSongPlayHistory_PostsSourceType1WithSingleSourceId()
    {
        _session.Set(Uid, "test-token");
        _handler.Responder = _ => ReplayHandler.Json("""{"code":200,"msg":"success","data":{}}""");

        await _api.AddSongPlayHistoryAsync(MusicId, Ct);

        var request = _handler.LastRequest;
        Assert.Equal("POST", request.Method);
        Assert.Contains("playlist/history/add?", request.Url, StringComparison.Ordinal);
        Assert.DoesNotContain("service/playlist", request.Url, StringComparison.Ordinal);

        var body = JsonDocument.Parse(request.Body!).RootElement;
        Assert.Equal(1, body.GetProperty("sourceType").GetInt32());

        var items = body.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal(MusicId, items[0].GetProperty("sourceId").GetInt64());

        // 歌曲条目不能带 playlistType —— 签名覆盖精确字节，多一个键就是另一份报文。
        Assert.False(items[0].TryGetProperty("playlistType", out _));
    }

    [Fact]
    public async Task AddSongPlayHistory_IsSigned()
    {
        _session.Set(Uid, "test-token");
        _handler.Responder = _ => ReplayHandler.Json("""{"code":200,"msg":"success","data":{}}""");

        await _api.AddSongPlayHistoryAsync(MusicId, Ct);

        Assert.Contains("sign=", _handler.LastRequest.Url, StringComparison.Ordinal);
        Assert.Contains("timestamp=", _handler.LastRequest.Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddSongPlayHistory_RequiresLogin()
    {
        await Assert.ThrowsAsync<BodianNotSignedInException>(
            () => _api.AddSongPlayHistoryAsync(MusicId, Ct));
    }

    // ── 同步服务 ────────────────────────────────────────────────────────────

    /// <summary>未登录时一个请求都不发。</summary>
    [Fact]
    public void NotifyPlayed_WhenAnonymous_SendsNothing()
    {
        _sync.NotifyPlayed(MusicId);

        Assert.Empty(_handler.Requests);
    }

    /// <summary>登录态下发出一次上报，且方法本身立即返回。</summary>
    [Fact]
    public async Task NotifyPlayed_WhenAuthenticated_SendsOneRequest()
    {
        _session.Set(Uid, "test-token");

        var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _handler.Responder = _ =>
        {
            sent.TrySetResult();
            return ReplayHandler.Json("""{"code":200,"msg":"success","data":{}}""");
        };

        _sync.NotifyPlayed(MusicId);

        await sent.Task.WaitAsync(TimeSpan.FromSeconds(2), Ct);

        Assert.Single(_handler.Requests);
        var body = JsonDocument.Parse(_handler.LastRequest.Body!).RootElement;
        Assert.Equal(1, body.GetProperty("sourceType").GetInt32());
        Assert.Equal(MusicId, body.GetProperty("items")[0].GetProperty("sourceId").GetInt64());
    }

    /// <summary>传输层抛异常时，调用方不受影响，也不能有异常逃出后台任务。</summary>
    [Fact]
    public async Task NotifyPlayed_SwallowsTransportFailure()
    {
        _session.Set(Uid, "test-token");

        var attempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _handler.Responder = _ =>
        {
            attempted.TrySetResult();
            throw new HttpRequestException("boom");
        };

        _sync.NotifyPlayed(MusicId);

        await attempted.Task.WaitAsync(TimeSpan.FromSeconds(2), Ct);

        // 给后台 catch 一点时间；这个测试只验证「没有异常冒出来」。
        await Task.Delay(50, Ct);
    }

    // ── 歌单 / 专辑 ─────────────────────────────────────────────────────────

    /// <summary>歌单：<c>sourceType=2</c>，item 带透传的 <c>playlistType</c>。</summary>
    [Fact]
    public async Task AddCollectionPlayHistory_PlaylistPostsSourceType2()
    {
        _session.Set(Uid, "test-token");
        _handler.Responder = _ => ReplayHandler.Json("""{"code":200,"msg":"success","data":{}}""");

        await _api.AddCollectionPlayHistoryAsync(historySourceType: 2, sourceId: 97634652, playlistType: 5, Ct);

        var body = JsonDocument.Parse(_handler.LastRequest.Body!).RootElement;
        Assert.Equal(2, body.GetProperty("sourceType").GetInt32());

        var item = body.GetProperty("items")[0];
        Assert.Equal(97634652, item.GetProperty("sourceId").GetInt64());
        Assert.Equal(5, item.GetProperty("playlistType").GetInt32());
    }

    /// <summary>专辑：<c>sourceType=3</c>，<c>playlistType</c> 实测为 6。</summary>
    [Fact]
    public async Task AddCollectionPlayHistory_AlbumPostsSourceType3()
    {
        _session.Set(Uid, "test-token");
        _handler.Responder = _ => ReplayHandler.Json("""{"code":200,"msg":"success","data":{}}""");

        await _api.AddCollectionPlayHistoryAsync(historySourceType: 3, sourceId: 87758985, playlistType: 6, Ct);

        var body = JsonDocument.Parse(_handler.LastRequest.Body!).RootElement;
        Assert.Equal(3, body.GetProperty("sourceType").GetInt32());

        var item = body.GetProperty("items")[0];
        Assert.Equal(87758985, item.GetProperty("sourceId").GetInt64());
        Assert.Equal(6, item.GetProperty("playlistType").GetInt32());
    }

    [Fact]
    public async Task AddCollectionPlayHistory_RejectsSongSourceType()
    {
        _session.Set(Uid, "test-token");

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _api.AddCollectionPlayHistoryAsync(historySourceType: 1, sourceId: 1, playlistType: 5, Ct));
    }

    [Fact]
    public async Task NotifyCollectionPlayed_WhenAuthenticated_SendsOneRequest()
    {
        _session.Set(Uid, "test-token");

        var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _handler.Responder = _ =>
        {
            sent.TrySetResult();
            return ReplayHandler.Json("""{"code":200,"msg":"success","data":{}}""");
        };

        _sync.NotifyCollectionPlayed(historySourceType: 2, sourceId: 97634652, playlistType: 5);

        await sent.Task.WaitAsync(TimeSpan.FromSeconds(2), Ct);

        var body = JsonDocument.Parse(_handler.LastRequest.Body!).RootElement;
        Assert.Equal(2, body.GetProperty("sourceType").GetInt32());
        Assert.Equal(5, body.GetProperty("items")[0].GetProperty("playlistType").GetInt32());
    }
}
