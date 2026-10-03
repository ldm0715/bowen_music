using System.Text.Json;
using Bodian.Core.Api;
using Bodian.Core.Tests.Support;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 歌单加歌 / 删歌两条写请求的形状。零真实网络。
/// </summary>
/// <remarks>
/// 路径与 body 来自文档 2.4 的往返实测（2026-09-30），以及 findings/06 §8 的桌面端字符串抽取。
/// 这里断言的是**发出去的报文**，不是服务端行为 —— 真实写入由用户手动实测。
/// </remarks>
public sealed class LikedSongsApiTests : IDisposable
{
    private const string Uid = "50303440";
    private const long PlaylistId = 99980832;
    private const long MusicId = 228908;

    private readonly ReplayHandler _handler = new();
    private readonly BodianSession _session = BodianSession.CreateAnonymous();
    private readonly BodianHttpTransport _transport;
    private readonly BodianApi _api;

    public LikedSongsApiTests()
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

    [Fact]
    public async Task Add_PostsPlaylistIdAndMusicIds()
    {
        _session.Set(Uid, "test-token");

        await _api.AddPlaylistMusicAsync(PlaylistId, [MusicId], Ct);

        var request = _handler.LastRequest;
        Assert.Equal("POST", request.Method);
        Assert.Contains($"service/playlist/music?", request.Url, StringComparison.Ordinal);
        Assert.DoesNotContain("music/delete", request.Url, StringComparison.Ordinal);

        var body = JsonDocument.Parse(request.Body!).RootElement;
        Assert.Equal(PlaylistId, body.GetProperty("playListId").GetInt64());
        Assert.Equal(MusicId, body.GetProperty("musicIdList")[0].GetInt64());
    }

    /// <summary>id 必须是数字，不能是字符串 —— 文档 2.4 明确要求安全整数。</summary>
    [Fact]
    public async Task Add_SendsNumericIds()
    {
        _session.Set(Uid, "test-token");

        await _api.AddPlaylistMusicAsync(PlaylistId, [MusicId], Ct);

        var element = JsonDocument.Parse(_handler.LastRequest.Body!).RootElement;
        Assert.Equal(JsonValueKind.Number, element.GetProperty("playListId").ValueKind);
        Assert.Equal(JsonValueKind.Number, element.GetProperty("musicIdList")[0].ValueKind);
    }

    [Fact]
    public async Task Remove_UsesTheDeletePath()
    {
        _session.Set(Uid, "test-token");

        await _api.RemovePlaylistMusicAsync(PlaylistId, [MusicId], Ct);

        var request = _handler.LastRequest;
        Assert.Equal("POST", request.Method);
        Assert.Contains("service/playlist/music/delete?", request.Url, StringComparison.Ordinal);

        var body = JsonDocument.Parse(request.Body!).RootElement;
        Assert.Equal(PlaylistId, body.GetProperty("playListId").GetInt64());
    }

    /// <summary>两条都是签名请求；签名覆盖 body，缺了就打不通。</summary>
    [Fact]
    public async Task Write_IsSigned()
    {
        _session.Set(Uid, "test-token");

        await _api.AddPlaylistMusicAsync(PlaylistId, [MusicId], Ct);

        Assert.Contains("sign=", _handler.LastRequest.Url, StringComparison.Ordinal);
        Assert.Contains("timestamp=", _handler.LastRequest.Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Write_RequiresLogin()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _api.AddPlaylistMusicAsync(PlaylistId, [MusicId], Ct));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Write_RejectsBadPlaylistId(long playlistId)
    {
        _session.Set(Uid, "test-token");

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _api.AddPlaylistMusicAsync(playlistId, [MusicId], Ct));
    }

    [Fact]
    public async Task Write_RejectsEmptyAndOversizedBatches()
    {
        _session.Set(Uid, "test-token");

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _api.AddPlaylistMusicAsync(PlaylistId, [], Ct));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _api.AddPlaylistMusicAsync(PlaylistId, [.. Enumerable.Range(1, 101).Select(i => (long)i)], Ct));
    }

    /// <summary>100 首是上限内的最后一批，必须放行。</summary>
    [Fact]
    public async Task Write_AcceptsAFullBatch()
    {
        _session.Set(Uid, "test-token");

        await _api.AddPlaylistMusicAsync(
            PlaylistId, [.. Enumerable.Range(1, 100).Select(i => (long)i)], Ct);

        var element = JsonDocument.Parse(_handler.LastRequest.Body!).RootElement;
        Assert.Equal(100, element.GetProperty("musicIdList").GetArrayLength());
    }
}
