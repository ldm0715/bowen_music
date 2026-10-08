using System.Text.Json;
using Bodian.Core.Api;
using Bodian.Core.Tests.Support;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 编辑歌单与封面上传的报文形状。零真实网络。
/// </summary>
/// <remarks>
/// 五个键（<c>id/name/description/pic/categoryList</c>）来自反汇编字面量证据
/// （<c>edit_user_playlist.dart:3796-3914</c>），<b>整条 PUT 路径尚未实测</b>。
/// 上传的端点与字段名同理。这里断言的是**发出去的报文**，不是服务端行为。
/// </remarks>
public sealed class PlaylistEditApiTests : IDisposable
{
    private const string Uid = "36828743";
    private const long PlaylistId = 100167230;

    private readonly ReplayHandler _handler = new();
    private readonly BodianSession _session = BodianSession.CreateAnonymous();
    private readonly BodianHttpTransport _transport;
    private readonly BodianApi _api;

    public PlaylistEditApiTests()
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

    private void Ok(string data = "{}")
        => _handler.Responder = _ => ReplayHandler.Json($$"""{"code":200,"msg":"success","data":{{data}}}""");

    // ── 编辑 ────────────────────────────────────────────────────────────────

    /// <summary>PUT 打到裸路径，body 恰好是那五个键。</summary>
    [Fact]
    public async Task UpdatePlaylist_SendsPutWithFiveKeys()
    {
        Ok();

        await _api.UpdatePlaylistAsync(
            PlaylistId, "新名字", "新简介", "https://img.example/a.webp", [77, 78], Ct);

        var request = _handler.LastRequest;
        Assert.Equal("PUT", request.Method);
        Assert.Contains("service/playlist?", request.Url, StringComparison.Ordinal);

        // 与两条邻居分清楚：那条动歌单里的歌，另一条是列表。
        Assert.DoesNotContain("playlist/music", request.Url, StringComparison.Ordinal);
        Assert.DoesNotContain("userCreate", request.Url, StringComparison.Ordinal);

        var body = JsonDocument.Parse(request.Body!).RootElement;
        Assert.Equal(PlaylistId, body.GetProperty("id").GetInt64());
        Assert.Equal("新名字", body.GetProperty("name").GetString());
        Assert.Equal("新简介", body.GetProperty("description").GetString());
        Assert.Equal("https://img.example/a.webp", body.GetProperty("pic").GetString());
        Assert.Equal([77, 78], body.GetProperty("categoryList").EnumerateArray().Select(e => e.GetInt32()));
    }

    /// <summary>
    /// <b>请求体里不能有隐私键。</b> 官方编辑页没有隐私开关，PUT 也没有这个键 ——
    /// 加上去会被服务端静默忽略（像 <c>private</c>/<c>isPrivate</c> 写错那样），
    /// 让用户以为改了其实没改。这条断言是防止将来有人「顺手补上」。
    /// </summary>
    [Fact]
    public async Task UpdatePlaylist_NeverSendsPrivacyKey()
    {
        Ok();

        await _api.UpdatePlaylistAsync(PlaylistId, "名字", "", "", [], Ct);

        var body = JsonDocument.Parse(_handler.LastRequest.Body!).RootElement;
        Assert.False(body.TryGetProperty("private", out _));
        Assert.False(body.TryGetProperty("isPrivate", out _));
    }

    [Fact]
    public async Task UpdatePlaylist_TrimsName()
    {
        Ok();

        await _api.UpdatePlaylistAsync(PlaylistId, "  前后有空格  ", "", "", [], Ct);

        var body = JsonDocument.Parse(_handler.LastRequest.Body!).RootElement;
        Assert.Equal("前后有空格", body.GetProperty("name").GetString());
    }

    /// <summary>服务端不校验空白名（新建时的实测结论），所以这条必须客户端自己挡。</summary>
    [Fact]
    public async Task UpdatePlaylist_BlankName_ThrowsWithoutRequest()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _api.UpdatePlaylistAsync(PlaylistId, "   ", "", "", [], Ct));

        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task UpdatePlaylist_NonPositiveId_ThrowsWithoutRequest()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _api.UpdatePlaylistAsync(0, "名字", "", "", [], Ct));

        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task UpdatePlaylist_Anonymous_ThrowsWithoutRequest()
    {
        _session.Clear();

        await Assert.ThrowsAsync<BodianNotSignedInException>(
            () => _api.UpdatePlaylistAsync(PlaylistId, "名字", "", "", [], Ct));

        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task UpdatePlaylist_RejectsStaleAccount()
    {
        _handler.Responder = _ =>
        {
            _session.Set("999", "other-token");
            return ReplayHandler.Json("""{"code":200,"msg":"success","data":{}}""");
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _api.UpdatePlaylistAsync(PlaylistId, "名字", "", "", [], Ct));
    }

    // ── 封面上传 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 上传是 multipart：路径带歌单 id、字段名是 <c>file</c>，
    /// 且 Content-Type 必须保留 boundary —— 被盖成 <c>application/json</c> 服务端就解析不出文件。
    /// </summary>
    [Fact]
    public async Task UploadCover_SendsMultipartWithFileField()
    {
        Ok("""{"imgUrl":"https://bodianimgcdn.kuwo.cn/file/abc.jpg"}""");

        await _api.UploadPlaylistCoverAsync(PlaylistId, [1, 2, 3, 4], "cover.jpg", "image/jpeg", Ct);

        var request = _handler.LastRequest;
        Assert.Equal("POST", request.Method);
        Assert.Contains($"service/playlist/uploadPic/{PlaylistId}?", request.Url, StringComparison.Ordinal);

        var contentType = request.Header("Content-Type");
        Assert.NotNull(contentType);
        Assert.StartsWith("multipart/form-data", contentType, StringComparison.Ordinal);

        Assert.Contains("name=file", request.Body, StringComparison.Ordinal);
        Assert.Contains("cover.jpg", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UploadCover_ReturnsImageUrl()
    {
        Ok("""{"imgUrl":"https://bodianimgcdn.kuwo.cn/file/abc.jpg"}""");

        var url = await _api.UploadPlaylistCoverAsync(PlaylistId, [1, 2, 3], "c.png", "image/png", Ct);

        Assert.Equal("https://bodianimgcdn.kuwo.cn/file/abc.jpg", url);
    }

    /// <summary>回执里没有可用的地址时必须抛 —— 调用方要拿它去填 <c>pic</c>，填空值等于清掉封面。</summary>
    [Fact]
    public async Task UploadCover_NoUrlInReply_Throws()
    {
        Ok("""{"somethingElse":true}""");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _api.UploadPlaylistCoverAsync(PlaylistId, [1, 2, 3], "c.png", "image/png", Ct));
    }

    [Fact]
    public async Task UploadCover_EmptyBytes_ThrowsWithoutRequest()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _api.UploadPlaylistCoverAsync(PlaylistId, [], "c.png", "image/png", Ct));

        Assert.Empty(_handler.Requests);
    }
}
