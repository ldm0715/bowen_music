using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Bodian.Core.Api;
using Bodian.Core.Models;
using Bodian.Core.Tests.Support;
using Bodian.WinUI.ViewModels;
using Xunit;

namespace Bodian.Core.Tests;

public sealed class SongCommentsViewModelTests
{
    private readonly ControlledTransport _transport = new();
    private readonly SongCommentsViewModel _vm;
    private readonly BodianSession _session;

    public SongCommentsViewModelTests()
    {
        var session = BodianSession.CreateAnonymous();
        _session = session;
        _vm = new SongCommentsViewModel(new BodianApi(_transport, session, new FakeDeviceIdentity()), session);
    }

    [Fact]
    public async Task CreatingAndChangingSortWhileClosed_DoesNotFetch()
    {
        Assert.Empty(_transport.Requests);
        await _vm.ChangeSortAsync(SongCommentSort.Latest);
        Assert.Empty(_transport.Requests);
        Assert.False(_vm.IsOpen);
    }

    [Fact]
    public async Task SwitchingSongs_DiscardsLateResponseAndCancelsPreviousRequest()
    {
        var first = _vm.ShowAsync(118990, "first");
        var second = _vm.ShowAsync(228908, "second");
        Assert.True(_transport.Requests[0].Token.IsCancellationRequested);
        _transport.Requests[1].Complete(Page([22], false));
        await second;
        _transport.Requests[0].Complete(Page([11], true));
        await first;
        Assert.Equal("second", _vm.SongTitle);
        Assert.Equal(22, Assert.Single(_vm.Items).Id);
        Assert.False(_vm.IsLoading);
        Assert.False(_vm.HasMore);
    }

    [Fact]
    public async Task SwitchingSort_RestartsAtPageOneAndDiscardsLateResponse()
    {
        var first = _vm.ShowAsync(118990, "song");
        var latest = _vm.ChangeSortAsync(SongCommentSort.Latest);
        Assert.True(_transport.Requests[0].Token.IsCancellationRequested);
        Assert.Equal("comments/v3/new", _transport.Requests[1].Request.Path);
        Assert.Contains(new KeyValuePair<string, string>("pn", "1"), _transport.Requests[1].Request.Query);
        _transport.Requests[1].Complete(Page([2], false));
        await latest;
        _transport.Requests[0].Complete(Page([1], true));
        await first;
        Assert.Equal(2, Assert.Single(_vm.Items).Id);
        Assert.True(_vm.IsLatest);
        Assert.False(_vm.IsRecommended);
    }

    [Fact]
    public async Task Closing_DiscardsLateResponseAndCancelsRequest()
    {
        var pending = _vm.ShowAsync(118990, "song");
        _vm.Close();
        Assert.True(_transport.Requests[0].Token.IsCancellationRequested);
        _transport.Requests[0].Complete(Page([1], true));
        await pending;
        Assert.False(_vm.IsOpen);
        Assert.False(_vm.IsLoading);
        Assert.False(_vm.LoadFailed);
        Assert.Empty(_vm.Items);
        Assert.False(_vm.LoadMoreCommand.CanExecute(null));
    }

    [Fact]
    public async Task Paging_DeduplicatesAndRetriesTheSameFailedPage()
    {
        var first = _vm.ShowAsync(118990, "song");
        _transport.Requests[0].Complete(Page([1, 2], true));
        await first;
        Assert.True(_vm.ShowLoadMore);
        var failed = _vm.LoadMoreCommand.ExecuteAsync(null);
        _transport.Requests[1].Fail(new HttpRequestException("offline"));
        await failed;
        Assert.Equal(2, _vm.Items.Count);
        Assert.True(_vm.ShowMoreError);
        var retry = _vm.RetryCommand.ExecuteAsync(null);
        Assert.Contains(new KeyValuePair<string, string>("pn", "2"), _transport.Requests[2].Request.Query);
        _transport.Requests[2].Complete(Page([2, 3], false));
        await retry;
        Assert.Equal(new long[] { 1, 2, 3 }, _vm.Items.Select(item => item.Id));
        Assert.True(_vm.ShowEnd);
        Assert.False(_vm.LoadFailed);
        Assert.False(_vm.ShowLoadMore);
    }

    [Fact]
    public async Task InitialFailure_CanRetryWithoutSkippingAPage()
    {
        var first = _vm.ShowAsync(118990, "song");
        _transport.Requests[0].Fail(new TimeoutException());
        await first;
        Assert.True(_vm.ShowInitialError);
        Assert.False(_vm.IsEmpty);
        var retry = _vm.RetryCommand.ExecuteAsync(null);
        Assert.Contains(new KeyValuePair<string, string>("pn", "1"), _transport.Requests[1].Request.Query);
        _transport.Requests[1].Complete(Page([], false));
        await retry;
        Assert.True(_vm.IsEmpty);
        Assert.False(_vm.LoadFailed);
    }

    [Fact]
    public async Task ConcurrentLoadMore_DoesNotDuplicateRequestsAndEmptyPageStopsPaging()
    {
        var first = _vm.ShowAsync(118990, "song");
        _transport.Requests[0].Complete(Page([1], true));
        await first;
        var next = _vm.LoadMoreCommand.ExecuteAsync(null);
        await _vm.RetryCommand.ExecuteAsync(null);
        Assert.Equal(2, _transport.Requests.Count);
        _transport.Requests[1].Complete(Page([], true));
        await next;
        Assert.False(_vm.HasMore);
        Assert.True(_vm.ShowEnd);
        Assert.Single(_vm.Items);
    }


    [Fact]
    public async Task ReplyThread_CancelsAndDiscardsLateRepliesOnClose()
    {
        var loading = _vm.ShowAsync(118990, "song");
        _transport.Requests[0].Complete(Page([1], false));
        await loading;
        var thread = _vm.OpenThreadAsync(_vm.Items[0]);
        Assert.True(_vm.IsThreadOpen);
        Assert.Equal("comments/v3/replies", _transport.Requests[1].Request.Path);
        _vm.CloseThread();
        Assert.True(_transport.Requests[1].Token.IsCancellationRequested);
        _transport.Requests[1].Complete(Page([11], false));
        await thread;
        Assert.Empty(_vm.Replies);
        Assert.True(_vm.IsListOpen);
    }

    [Fact]
    public async Task Drafts_AreSeparateForSongsAndRootReplyTargets()
    {
        var loading = _vm.ShowAsync(118990, "song");
        _transport.Requests[0].Complete(Page([1], false));
        await loading;
        _vm.DraftText = "song draft";
        var thread = _vm.OpenThreadAsync(_vm.Items[0]);
        _transport.Requests[1].Complete(Page([11], false));
        await thread;
        Assert.Equal("", _vm.DraftText);
        _vm.DraftText = "root reply draft";
        await _vm.ReplyToAsync(_vm.Replies[0]);
        Assert.Equal("", _vm.DraftText);
        _vm.DraftText = "specific reply draft";
        _vm.ClearReplyTargetCommand.Execute(null);
        Assert.Equal("root reply draft", _vm.DraftText);
        _vm.CloseThread();
        Assert.Equal("song draft", _vm.DraftText);
        var next = _vm.ShowAsync(228908, "next");
        _transport.Requests[2].Complete(Page([], false));
        await next;
        Assert.Equal("", _vm.DraftText);
        var back = _vm.ShowAsync(118990, "song");
        _transport.Requests[3].Complete(Page([1], false));
        await back;
        Assert.Equal("song draft", _vm.DraftText);
    }

    [Fact]
    public async Task LikeAndUnlike_UpdateSolidStateAfterSuccessWithoutReadingTheList()
    {
        _session.Set("42", "test-session");
        var loading = _vm.ShowAsync(118990, "song");
        _transport.Requests[0].Complete("""{"comment_list":[{"id":1,"content":"test","like":0,"likeCount":5}],"comment_total":1,"more":false}""");
        await loading;
        var row = _vm.Items[0];
        var like = _vm.ToggleLikeAsync(row);
        Assert.Equal("comments/v3/like", _transport.Requests[1].Request.Path);
        using var likeBody = JsonDocument.Parse(_transport.Requests[1].Request.JsonBody!);
        Assert.Equal(1, likeBody.RootElement.GetProperty("op").GetInt32());
        Assert.False(row.IsLiked);
        Assert.Equal(5, row.LikeCount);
        Assert.True(row.IsLiking);
        await _vm.ToggleLikeAsync(row);
        Assert.Equal(2, _transport.Requests.Count);
        _transport.Requests[1].Complete("{}");
        await like;
        Assert.True(row.IsLiked);
        Assert.Equal(6, row.LikeCount);
        Assert.False(row.IsLiking);
        Assert.False(_vm.HasFeedback);
        Assert.Equal(2, _transport.Requests.Count);

        var unlike = _vm.ToggleLikeAsync(row);
        Assert.Equal("comments/v3/like", _transport.Requests[2].Request.Path);
        using var unlikeBody = JsonDocument.Parse(_transport.Requests[2].Request.JsonBody!);
        Assert.Equal(2, unlikeBody.RootElement.GetProperty("op").GetInt32());
        Assert.True(row.IsLiked);
        _transport.Requests[2].Complete("{}");
        await unlike;
        Assert.False(row.IsLiked);
        Assert.Equal(5, row.LikeCount);
        Assert.False(row.IsLiking);
        Assert.False(_vm.HasFeedback);
        Assert.Equal(3, _transport.Requests.Count);
    }

    [Theory]
    [InlineData(false, 5)]
    [InlineData(true, 5)]
    public async Task FailedLikeOrUnlike_KeepsItsStateAndShowsOneError(bool initiallyLiked, long count)
    {
        _session.Set("42", "test-session");
        var loading = _vm.ShowAsync(118990, "song");
        _transport.Requests[0].Complete($$"""{"comment_list":[{"id":1,"like":{{(initiallyLiked ? 1 : 0)}},"likeCount":{{count}}}],"comment_total":1,"more":false}""");
        await loading;
        var row = _vm.Items[0];
        var like = _vm.ToggleLikeAsync(row);
        _transport.Requests[1].Fail(new BodianApiException(BodianErrorCode.Unknown, -10, "comments/v3/like", "error param", null));
        await like;
        Assert.Equal(initiallyLiked, row.IsLiked);
        Assert.Equal(count, row.LikeCount);
        Assert.False(row.IsLiking);
        Assert.True(_vm.HasFeedback);
        Assert.True(_vm.FeedbackIsError);
        Assert.Contains("-10", _vm.Feedback);
        Assert.Equal(2, _transport.Requests.Count);
    }

    [Fact]
    public async Task ReplyLike_UpdatesTheReplyWithoutRefreshingItsThread()
    {
        _session.Set("42", "test-session");
        var loading = _vm.ShowAsync(118990, "song");
        _transport.Requests[0].Complete(Page([1], false));
        await loading;
        var thread = _vm.OpenThreadAsync(_vm.Items[0]);
        _transport.Requests[1].Complete(Page([11], false));
        await thread;
        var reply = _vm.Replies[0];
        var like = _vm.ToggleLikeAsync(reply);
        using var body = JsonDocument.Parse(_transport.Requests[2].Request.JsonBody!);
        Assert.Equal(1, body.RootElement.GetProperty("parentId").GetInt64());
        Assert.Equal(11, body.RootElement.GetProperty("commentId").GetInt64());
        _transport.Requests[2].Complete("{}");
        await like;
        Assert.True(reply.IsLiked);
        Assert.Equal(1, reply.LikeCount);
        Assert.Same(reply, Assert.Single(_vm.Replies));
        Assert.Equal(3, _transport.Requests.Count);
    }

    [Fact]
    public async Task LateLikeSuccess_DoesNotChangeTheNewSongsComments()
    {
        _session.Set("42", "test-session");
        var loading = _vm.ShowAsync(118990, "song");
        _transport.Requests[0].Complete(Page([1], false));
        await loading;
        var oldRow = _vm.Items[0];
        var like = _vm.ToggleLikeAsync(oldRow);
        var next = _vm.ShowAsync(228908, "next");
        _transport.Requests[2].Complete(Page([2], false));
        await next;
        _transport.Requests[1].Complete("{}");
        await like;
        Assert.False(oldRow.IsLiked);
        Assert.False(oldRow.IsLiking);
        Assert.False(Assert.Single(_vm.Items).IsLiked);
        Assert.False(_vm.HasFeedback);
        Assert.Equal(3, _transport.Requests.Count);
    }

    [Fact]
    public async Task Publish_VerifiesReadbackBeforeClearingDraft()
    {
        _session.Set("42", "test-session");
        var loading = _vm.ShowAsync(118990, "song");
        _transport.Requests[0].Complete(Page([1], false));
        await loading;
        _vm.DraftText = "my comment";
        var sending = _vm.PublishCommand.ExecuteAsync(null);
        Assert.True(_vm.IsPosting);
        _transport.Requests[1].Complete("""{"id":23}""");
        await WaitForRequestCountAsync(3);
        Assert.Equal("comments/v3/new", _transport.Requests[2].Request.Path);
        _transport.Requests[2].Complete("""{"comment_list":[{"id":23,"uid":42,"content":"my comment"}],"comment_total":2,"more":false}""");
        await sending;
        Assert.Equal("", _vm.DraftText);
        Assert.Equal("评论已发送", _vm.Feedback);
        Assert.False(_vm.IsPosting);
    }

    [Fact]
    public async Task FailedPublish_KeepsDraftAndShowsError()
    {
        _session.Set("42", "test-session");
        var loading = _vm.ShowAsync(118990, "song");
        _transport.Requests[0].Complete(Page([1], false));
        await loading;
        _vm.DraftText = "keep me";
        var sending = _vm.PublishCommand.ExecuteAsync(null);
        _transport.Requests[1].Fail(new BodianApiException(BodianErrorCode.Unknown, -10, "comments/v3/publish", "error param", null));
        await sending;
        Assert.Equal("keep me", _vm.DraftText);
        Assert.Contains("-10", _vm.Feedback);
        Assert.True(_vm.FeedbackIsError);
        Assert.False(_vm.IsPosting);
    }

    [Fact]
    public async Task AcceptedButUnverifiedPublish_KeepsDraftAndDoesNotClaimSuccess()
    {
        _session.Set("42", "test-session");
        var loading = _vm.ShowAsync(118990, "song");
        _transport.Requests[0].Complete(Page([1], false));
        await loading;
        _vm.DraftText = "keep me";
        var sending = _vm.PublishCommand.ExecuteAsync(null);
        _transport.Requests[1].Complete("{}");
        await WaitForRequestCountAsync(3);
        _transport.Requests[2].Complete(Page([1], false));
        await sending;
        Assert.Equal("keep me", _vm.DraftText);
        Assert.Contains("暂未", _vm.Feedback);
    }

    [Fact]
    public async Task LatePublishResponse_DoesNotClearNewSongDraftOrTriggerRefresh()
    {
        _session.Set("42", "test-session");
        var loading = _vm.ShowAsync(118990, "song");
        _transport.Requests[0].Complete(Page([1], false));
        await loading;
        _vm.DraftText = "first draft";
        var sending = _vm.PublishCommand.ExecuteAsync(null);
        var next = _vm.ShowAsync(228908, "next");
        _transport.Requests[2].Complete(Page([], false));
        await next;
        _vm.DraftText = "second draft";
        _transport.Requests[1].Complete("""{"id":23}""");
        await sending;
        Assert.Equal("second draft", _vm.DraftText);
        Assert.Equal(3, _transport.Requests.Count);
    }

    [Fact]
    public async Task ImagePreview_RejectsLocalUrisAndClosesWhenPanelCloses()
    {
        var loading = _vm.ShowAsync(118990, "song");
        _transport.Requests[0].Complete(Page([], false));
        await loading;
        _vm.OpenImage(new Uri("file:///C:/test.jpg"));
        Assert.False(_vm.IsImageOpen);
        _vm.OpenImage(new Uri("https://example.com/image.jpg"));
        Assert.True(_vm.IsImageOpen);
        _vm.Close();
        Assert.False(_vm.IsImageOpen);
    }

    [Fact]
    public async Task PublishReply_VerifiesTheTailPageAndPreservesParentAndReplyIds()
    {
        _session.Set("42", "test-session");
        var loading = _vm.ShowAsync(118990, "song");
        _transport.Requests[0].Complete(Page([1], false));
        await loading;
        var thread = _vm.OpenThreadAsync(_vm.Items[0]);
        _transport.Requests[1].Complete(Page([11], true));
        await thread;
        await _vm.ReplyToAsync(_vm.Replies[0]);
        _vm.DraftText = "new reply";
        var sending = _vm.PublishCommand.ExecuteAsync(null);
        using var body = JsonDocument.Parse(_transport.Requests[2].Request.JsonBody!);
        Assert.Equal(1, body.RootElement.GetProperty("parentId").GetInt64());
        Assert.Equal(11, body.RootElement.GetProperty("replyId").GetInt64());
        _transport.Requests[2].Complete("""{"id":23}""");
        await WaitForRequestCountAsync(4);
        _transport.Requests[3].Complete("""{"comment_list":[{"id":11,"parentId":1}],"comment_total":31,"more":true}""");
        await WaitForRequestCountAsync(5);
        Assert.Contains(new KeyValuePair<string, string>("pn", "2"), _transport.Requests[4].Request.Query);
        _transport.Requests[4].Complete("""{"comment_list":[{"id":23,"parentId":1,"replyId":11,"uid":42,"content":"new reply"}],"comment_total":31,"more":false}""");
        await sending;
        Assert.Equal("回复已发送", _vm.Feedback);
        Assert.Equal("", _vm.DraftText);
        Assert.False(_vm.IsPosting);
    }

    [Fact]
    public async Task NoComments_ShowsTheEmptyStateAndClearsThePreviousSong()
    {
        var first = _vm.ShowAsync(118990, "with comments");
        _transport.Requests[0].Complete(Page([1], false));
        await first;
        var empty = _vm.ShowAsync(228908, "without comments");
        _transport.Requests[1].Complete("""{"comment_list":[],"comment_total":0,"more":false}""");
        await empty;
        Assert.Empty(_vm.Items);
        Assert.True(_vm.IsEmpty);
        Assert.True(_vm.HasLoaded);
        Assert.False(_vm.IsLoading);
        Assert.False(_vm.LoadFailed);
        Assert.False(_vm.ShowInitialError);
        Assert.Equal(0, _vm.TotalCount);
        Assert.False(_vm.HasMore);
    }

    [Fact]
    public async Task UnsupportedSourceId_IsDistinctFromNoCommentsAndDoesNotRetryOrPublish()
    {
        _session.Set("42", "test-session");
        await _vm.ShowAsync(10250281307392909, "unsupported source");
        Assert.Empty(_transport.Requests);
        Assert.True(_vm.CommentsUnavailable);
        Assert.False(_vm.IsEmpty);
        Assert.True(_vm.ShowInitialError);
        Assert.False(_vm.ShowInitialRetry);
        Assert.False(_vm.IsComposerEnabled);
        _vm.DraftText = "draft";
        Assert.False(_vm.PublishCommand.CanExecute(null));
        await _vm.RetryCommand.ExecuteAsync(null);
        Assert.Empty(_transport.Requests);
        var next = _vm.ShowAsync(118990, "supported song");
        _transport.Requests[0].Complete(Page([], false));
        await next;
        Assert.False(_vm.CommentsUnavailable);
        Assert.True(_vm.IsEmpty);
        Assert.True(_vm.IsComposerEnabled);
    }

    [Fact]
    public async Task Badge_FetchesOnlyTrackDetailsAndReusesKnownCounts()
    {
        var badge = _vm.LoadBadgeAsync(118990);
        Assert.Equal("service/music/info", _transport.Requests[0].Request.Path);
        _transport.Requests[0].Complete("""{"id":118990,"name":"song","comment":12345}""");
        await badge;
        Assert.Equal(12345, _vm.BadgeCount);
        Assert.Equal("1w2+", _vm.BadgeText);
        Assert.True(_vm.HasBadgeCount);
        await _vm.LoadBadgeAsync(118990);
        Assert.Single(_transport.Requests);
        Assert.False(_vm.IsOpen);
    }

    [Fact]
    public async Task Badge_RejectsLateCountsAfterSongChanges()
    {
        var first = _vm.LoadBadgeAsync(118990);
        var second = _vm.LoadBadgeAsync(228908);
        Assert.True(_transport.Requests[0].Token.IsCancellationRequested);
        _transport.Requests[1].Complete("""{"id":228908,"name":"second","comment":12}""");
        await second;
        _transport.Requests[0].Complete("""{"id":118990,"name":"first","comment":12345}""");
        await first;
        Assert.Equal(12, _vm.BadgeCount);
        Assert.Equal("12", _vm.BadgeText);
    }

    [Fact]
    public async Task ListCount_OverridesPendingBadgeMetadataAndZeroIsDisplayed()
    {
        var metadata = _vm.LoadBadgeAsync(118990);
        var list = _vm.ShowAsync(118990, "song");
        _transport.Requests[1].Complete("""{"comment_list":[],"comment_total":0,"more":false}""");
        await list;
        Assert.True(_transport.Requests[0].Token.IsCancellationRequested);
        _transport.Requests[0].Complete("""{"id":118990,"name":"song","comment":100}""");
        await metadata;
        Assert.Equal(0, _vm.BadgeCount);
        Assert.Equal("0", _vm.BadgeText);
        Assert.True(_vm.HasBadgeCount);
    }

    [Fact]
    public async Task UnknownBadgeCount_IsHiddenRatherThanShownAsZero()
    {
        var badge = _vm.LoadBadgeAsync(118990);
        _transport.Requests[0].Complete("""{"id":118990,"name":"song"}""");
        await badge;
        Assert.Null(_vm.BadgeCount);
        Assert.False(_vm.HasBadgeCount);
        Assert.Equal("", _vm.BadgeText);
    }

    private async Task WaitForRequestCountAsync(int count)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (_transport.Requests.Count < count) await Task.Delay(1, timeout.Token);
    }

    private static string Page(long[] ids, bool more) => $$"""
        {"comment_list":[{{string.Join(",", ids.Select(id => $"{{\"id\":{id},\"content\":\"test\"}}"))}}],"comment_total":100,"more":{{(more ? "true" : "false")}}}
        """;

    /// <summary>故意不自动遵守取消，让测试能注入迟到响应，检验 UI 的代次保护。</summary>
    private sealed class ControlledTransport : IBodianTransport
    {
        public List<PendingRequest> Requests { get; } = [];

        public async Task<BodianEnvelope<T>> SendAsync<T>(BodianRequest request, JsonTypeInfo<T> dataTypeInfo,
            CancellationToken cancellationToken = default)
        {
            var pending = new PendingRequest(request, cancellationToken);
            Requests.Add(pending);
            var json = await pending.Completion.Task;
            return new BodianEnvelope<T>(200, "success", null, JsonSerializer.Deserialize(json, dataTypeInfo), 0);
        }

        public Task<BodianEnvelope<T>> SendAbsoluteAsync<T>(Uri url, JsonTypeInfo<T> dataTypeInfo,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed record PendingRequest(BodianRequest Request, CancellationToken Token)
    {
        public TaskCompletionSource<string> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Complete(string json) => Completion.SetResult(json);
        public void Fail(Exception error) => Completion.SetException(error);
    }
}
