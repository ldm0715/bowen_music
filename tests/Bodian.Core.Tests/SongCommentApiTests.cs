using System.Text.Json;
using Bodian.Core.Api;
using Bodian.Core.Api.Paging;
using Bodian.Core.Models;
using Bodian.Core.Tests.Support;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>只读评论接口：真实公开样本回放，测试不联网。</summary>
public sealed class SongCommentApiTests : IDisposable
{
    private readonly ReplayHandler _handler = new();
    private readonly BodianHttpTransport _transport;
    private readonly BodianApi _api;
    private readonly BodianSession _session;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public SongCommentApiTests()
    {
        var session = BodianSession.CreateAnonymous();
        _session = session;
        var device = new FakeDeviceIdentity();
        _transport = new BodianHttpTransport(_handler, new BodianTransportOptions(), session, device, FixedTimeProvider.Golden);
        _api = new BodianApi(_transport, session, device);
    }

    public void Dispose() => _transport.Dispose();

    [Theory]
    [InlineData(SongCommentSort.Latest, "new", 31286235)]
    [InlineData(SongCommentSort.Recommended, "hot", 867666)]
    public async Task Lists_MapCapturedPublicResponses(SongCommentSort sort, string endpoint, long firstId)
    {
        _handler.Responder = _ => ReplayHandler.Json(Fixtures.Read($"comments-{endpoint}-118990.json"));
        var page = await _api.GetSongCommentsAsync(118990, sort, cancellationToken: Ct);
        Assert.Equal(30, page.Items.Count);
        Assert.Equal(1522, page.TotalCount);
        Assert.True(page.HasMore);
        Assert.Equal(firstId, page.Items[0].Id);
        Assert.NotEmpty(page.Items[0].Content);
        Assert.NotEmpty(page.Items[0].Nickname);
        Assert.NotEmpty(page.Items[0].PublishTime);
        Assert.NotNull(page.Items[0].AvatarUri);
        Assert.StartsWith($"https://bd-api.kuwo.cn/api/comments/v3/{endpoint}?", _handler.LastRequest.Url);
    }

    [Fact]
    public async Task List_UsesUnsignedAndroidGetWithSafePageSizeAndDoesNotChangeDesktopHeaders()
    {
        _handler.Responder = _ => ReplayHandler.Json(Fixtures.Read("comments-hot-118990.json"));
        await _api.GetSongCommentsAsync(118990, SongCommentSort.Recommended, 2, Ct);
        var request = _handler.LastRequest;
        Assert.Equal("GET", request.Method);
        Assert.Null(request.Body);
        Assert.Equal("android", request.Header("plat"));
        Assert.Equal(1, request.HeaderCount("plat"));
        Assert.Equal("1.1.7", request.Header("ver"));
        Assert.Contains("moduleType=2&moduleId=118990&pn=2&rn=30", request.Url);
        Assert.DoesNotContain("sign=", request.Url);
        Assert.DoesNotContain("timestamp=", request.Url);
        Assert.Equal(1, request.Url.Split("uid=").Length - 1);

        _handler.Responder = _ => ReplayHandler.Json("""{"code":200,"data":{"resultList":[],"total":0}}""");
        await _api.SearchAsync("test", new PagedCursor(PagingConvention.ZeroBased), Ct);
        Assert.Equal("win", _handler.LastRequest.Header("plat"));
        Assert.Equal(1, _handler.LastRequest.HeaderCount("plat"));
    }

    [Fact]
    public async Task EmptyList_IsAValidResult()
    {
        _handler.Responder = _ => ReplayHandler.Json("""{"code":200,"data":{"comment_list":[],"comment_total":0,"more":false}}""");
        var result = await _api.GetSongCommentsAsync(118990, SongCommentSort.Latest, cancellationToken: Ct);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.False(result.HasMore);
    }

    [Fact]
    public async Task MissingData_IsAnErrorRatherThanAnEmptyList()
    {
        _handler.Responder = _ => ReplayHandler.Json("""{"code":200,"data":null}""");
        await Assert.ThrowsAsync<JsonException>(() => _api.GetSongCommentsAsync(118990, SongCommentSort.Latest, cancellationToken: Ct));
    }

    [Fact]
    public async Task StringNumbers_AnonymousIdentityAndMissingFields_AreHandled()
    {
        _handler.Responder = _ => ReplayHandler.Json("""
            {"code":200,"data":{"comment_total":"2","more":false,"comment_list":[
                {"id":"21","nickname":"private","headImg":"https://example.com/avatar.jpg","anonymous":"1",
                 "likeCount":"7","replyCount":"2","like":"1","createTime":"2026-10-01","ipProvince":"上海"},
                {"id":22,"headImg":"file:///C:/test.jpg","imgUrl":"invalid","likeCount":-1}
            ]}}
            """);
        var result = await _api.GetSongCommentsAsync(118990, SongCommentSort.Latest, cancellationToken: Ct);
        Assert.Equal(2, result.TotalCount);
        var anonymous = result.Items[0];
        Assert.Equal("匿名听友", anonymous.Nickname);
        Assert.Null(anonymous.AvatarUri);
        Assert.Equal("", anonymous.Content);
        Assert.Equal(7, anonymous.LikeCount);
        Assert.Equal(2, anonymous.ReplyCount);
        Assert.True(anonymous.IsLiked);
        Assert.Equal("2026-10-01", anonymous.PublishTime);
        Assert.Equal("上海", anonymous.Location);
        Assert.Equal("听友", result.Items[1].Nickname);
        Assert.Null(result.Items[1].AvatarUri);
        Assert.Null(result.Items[1].ImageUri);
        Assert.Equal(0, result.Items[1].LikeCount);
    }

    [Theory]
    [InlineData(0, SongCommentSort.Latest, 1)]
    [InlineData(118990, SongCommentSort.Latest, 0)]
    [InlineData(118990, (SongCommentSort)99, 1)]
    public async Task InvalidArguments_DoNotSendARequest(long id, SongCommentSort sort, int page)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _api.GetSongCommentsAsync(id, sort, page, Ct));
        Assert.Empty(_handler.Requests);
    }
    [Fact]
    public async Task Replies_UseV3ParentIdAndMapNestedIdentity()
    {
        _handler.Responder = _ => ReplayHandler.Json(Fixtures.Read("comments-replies-867666.json"));
        var page = await _api.GetSongCommentRepliesAsync(118990, 867666, 1, Ct);
        Assert.Equal(30, page.Items.Count);
        Assert.Equal(69, page.TotalCount);
        Assert.True(page.HasMore);
        Assert.All(page.Items, comment => Assert.Equal(867666, comment.ParentId));
        var nested = page.Items.Single(comment => comment.Id == 5906044);
        Assert.Equal(5897162, nested.ReplyId);
        Assert.Equal("rad爷", nested.ReplyNickname);
        Assert.NotEqual("听友", nested.Nickname);
        Assert.NotNull(nested.AvatarUri);
        Assert.StartsWith("https://bd-api.kuwo.cn/api/comments/v3/replies?", _handler.LastRequest.Url);
        Assert.Contains("parentId=867666&pn=1&rn=30", _handler.LastRequest.Url);
        Assert.Equal("GET", _handler.LastRequest.Method);
        Assert.Equal("android", _handler.LastRequest.Header("plat"));
    }

    [Fact]
    public async Task Writes_RequireLoginBeforeSendingAnything()
    {
        await Assert.ThrowsAsync<BodianNotSignedInException>(() => _api.PublishSongCommentAsync(118990, "draft", cancellationToken: Ct));
        await Assert.ThrowsAsync<BodianNotSignedInException>(() => _api.SetSongCommentLikeAsync(118990, 867666, true, cancellationToken: Ct));
        Assert.Empty(_handler.Requests);
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(867666, 0, false)]
    [InlineData(867666, 5906044, true)]
    public async Task Publish_UsesJsonAndOmitsUnknownOptionalFields(long parentId, long replyId, bool anonymous)
    {
        // 全程为 ReplayHandler，不接触真实服务端。
        _session.Set("42", "test-session");
        _handler.Responder = _ => ReplayHandler.Json("""{"code":200,"reqId":"offline","data":{"id":"123"}}""");
        var result = await _api.PublishSongCommentAsync(118990, "  my comment  ", parentId, replyId, anonymous, Ct);
        Assert.Equal(123, result);
        Assert.Equal("POST", _handler.LastRequest.Method);
        Assert.Equal("android", _handler.LastRequest.Header("plat"));
        Assert.Equal("application/json", _handler.LastRequest.Header("Content-Type"));
        Assert.Contains("comments/v3/publish?", _handler.LastRequest.Url);
        Assert.DoesNotContain("sign=", _handler.LastRequest.Url);
        using var doc = JsonDocument.Parse(_handler.LastRequest.Body!);
        var body = doc.RootElement;
        Assert.Equal(118990, body.GetProperty("moduleId").GetInt64());
        Assert.Equal(2, body.GetProperty("moduleType").GetInt32());
        Assert.Equal(42, body.GetProperty("uid").GetInt64());
        Assert.Equal("my comment", body.GetProperty("content").GetString());
        Assert.Equal(anonymous ? 1 : 0, body.GetProperty("anonymous").GetInt32());
        Assert.Equal(parentId > 0, body.TryGetProperty("parentId", out var parent));
        Assert.Equal(replyId > 0, body.TryGetProperty("replyId", out var reply));
        if (parentId > 0) Assert.Equal(parentId, parent.GetInt64());
        if (replyId > 0) Assert.Equal(replyId, reply.GetInt64());
        Assert.False(body.TryGetProperty("pSrc", out _));
        Assert.False(body.TryGetProperty("imgUrl", out _));
    }

    [Theory]
    [InlineData(true, 0, 1)]
    [InlineData(false, 0, 2)]
    [InlineData(true, 867666, 1)]
    [InlineData(false, 867666, 2)]
    public async Task Like_UsesTheV3SongUiProtocolForRootAndReply(bool liked, long parentId, int operation)
    {
        // 依据原客户端歌曲界面的真实调用链；ReplayHandler 不访问服务端。
        _session.Set("42", "test-session");
        _handler.Responder = _ => ReplayHandler.Json("""{"code":200,"data":{}}""");
        await _api.SetSongCommentLikeAsync(118990, 5906044, liked, parentId, Ct);
        Assert.Equal("POST", _handler.LastRequest.Method);
        Assert.StartsWith("https://bd-api.kuwo.cn/api/comments/v3/like?", _handler.LastRequest.Url);
        Assert.Equal("android", _handler.LastRequest.Header("plat"));
        Assert.Equal(1, _handler.LastRequest.HeaderCount("plat"));
        Assert.Equal("1.1.7", _handler.LastRequest.Header("ver"));
        Assert.Equal("application/json", _handler.LastRequest.Header("Content-Type"));
        Assert.DoesNotContain("sign=", _handler.LastRequest.Url);
        using var doc = JsonDocument.Parse(_handler.LastRequest.Body!);
        var body = doc.RootElement;
        Assert.Equal(118990, body.GetProperty("moduleId").GetInt64());
        Assert.Equal(2, body.GetProperty("moduleType").GetInt32());
        Assert.Equal(42, body.GetProperty("uid").GetInt64());
        Assert.Equal(5906044, body.GetProperty("commentId").GetInt64());
        Assert.Equal(operation, body.GetProperty("op").GetInt32());
        Assert.Equal(parentId > 0, body.TryGetProperty("parentId", out var parent));
        if (parentId > 0) Assert.Equal(parentId, parent.GetInt64());
        Assert.Equal(parentId > 0 ? 6 : 5, body.EnumerateObject().Count());
        Assert.False(body.TryGetProperty("sourceId", out _));
        Assert.False(body.TryGetProperty("source", out _));
        Assert.False(body.TryGetProperty("parentCommentId", out _));
    }

    [Fact]
    public async Task LikeBusinessError_IsNotTreatedAsSuccess()
    {
        _session.Set("42", "test-session");
        _handler.Responder = _ => ReplayHandler.Json("""{"code":-10,"msg":"error param","data":{}}""");
        var exception = await Assert.ThrowsAsync<BodianApiException>(() => _api.SetSongCommentLikeAsync(118990, 867666, true, cancellationToken: Ct));
        Assert.Equal(-10, exception.RawCode);
        Assert.Equal("comments/v3/like", exception.Path);
    }

    [Fact]
    public async Task WriteBusinessError_IsNotTreatedAsSuccess()
    {
        _session.Set("42", "test-session");
        _handler.Responder = _ => ReplayHandler.Json("""{"code":-10,"msg":"error param","data":{}}""");
        var exception = await Assert.ThrowsAsync<BodianApiException>(() => _api.PublishSongCommentAsync(118990, "draft", cancellationToken: Ct));
        Assert.Equal(-10, exception.RawCode);
        Assert.Equal("error param", exception.ServerMessage);
    }

    [Fact]
    public async Task IdOutsideTheCommentServicesIntegerRange_IsRejectedBeforeNetworkRequests()
    {
        const long id = 10250281307392909;
        await Assert.ThrowsAsync<NotSupportedException>(() => _api.GetSongCommentsAsync(id, SongCommentSort.Latest, cancellationToken: Ct));
        await Assert.ThrowsAsync<NotSupportedException>(() => _api.GetSongCommentRepliesAsync(id, 1, cancellationToken: Ct));
        _session.Set("42", "test-session");
        await Assert.ThrowsAsync<NotSupportedException>(() => _api.SetSongCommentLikeAsync(id, 1, true, cancellationToken: Ct));
        await Assert.ThrowsAsync<NotSupportedException>(() => _api.PublishSongCommentAsync(id, "draft", cancellationToken: Ct));
        Assert.Empty(_handler.Requests);
    }

}
