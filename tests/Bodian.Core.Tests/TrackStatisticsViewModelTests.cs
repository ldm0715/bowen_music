using Bodian.Core.Api;
using Bodian.Core.Models;
using Bodian.Core.Services.Abstractions;
using Bodian.Core.Tests.Support;
using Bodian.WinUI.ViewModels;
using Xunit;

namespace Bodian.Core.Tests;

public sealed class TrackStatisticsViewModelTests
{
    private static Track Track(long id, long? favorite = null, long? share = null, long? comment = null) => new()
    {
        Id = id, Title = $"Song {id}", FavoriteCount = favorite, ShareCount = share, CommentCount = comment,
    };

    [Fact]
    public async Task CompleteMetadata_DoesNotFetchAndReusesCountsForComments()
    {
        var api = new PlaybackApiStub { GetTrack = (_, _) => throw new InvalidOperationException("Unexpected network") };
        using var vm = new TrackStatisticsViewModel(api);
        await vm.LoadAsync(Track(1, 12345, 42, 90));
        Assert.Equal("1w2+", vm.FavoriteBadgeText);
        Assert.Equal("42", vm.ShareBadgeText);
        var comments = new SongCommentsViewModel(api, BodianSession.CreateAnonymous(), statistics: vm);
        await comments.LoadBadgeAsync(1);
        Assert.Equal(90, comments.BadgeCount);
        Assert.True(vm.HasFavoriteCount);
        Assert.True(vm.HasShareCount);
    }

    [Fact]
    public async Task UnknownCounts_AreHiddenAndExplicitZeroGetsABadge()
    {
        var requests = new Requests();
        using var vm = new TrackStatisticsViewModel(requests.Api);
        var loading = vm.LoadAsync(Track(1));
        Assert.False(vm.HasFavoriteCount);
        Assert.False(vm.HasShareCount);
        Assert.Equal("", vm.FavoriteBadgeText);
        Assert.Equal("", vm.ShareBadgeText);
        requests.Items[0].Complete(Track(1, 0, 0, 0));
        await loading;
        Assert.True(vm.HasFavoriteCount);
        Assert.True(vm.HasShareCount);
        Assert.Equal("0", vm.FavoriteBadgeText);
        Assert.Equal("0", vm.ShareBadgeText);
    }

    [Fact]
    public async Task MissingDetailCounts_DoNotOverwriteKnownMetadata()
    {
        var requests = new Requests();
        using var vm = new TrackStatisticsViewModel(requests.Api);
        var loading = vm.LoadAsync(Track(1, favorite: 28));
        Assert.Equal("28", vm.FavoriteBadgeText);
        requests.Items[0].Complete(Track(1, share: 12345, comment: 8));
        await loading;
        Assert.Equal(28, vm.FavoriteCount);
        Assert.Equal("1w2+", vm.ShareBadgeText);
        Assert.Equal(8, (await vm.GetDetailsAsync(1, TestContext.Current.CancellationToken))!.CommentCount);
    }

    [Fact]
    public async Task SharingTheCurrentDetailRequest_DoesNotFetchAgain()
    {
        var requests = new Requests();
        using var vm = new TrackStatisticsViewModel(requests.Api);
        var first = vm.LoadAsync(Track(1));
        var second = vm.LoadAsync(Track(1));
        var comments = new SongCommentsViewModel(requests.Api, BodianSession.CreateAnonymous(), statistics: vm);
        var badge = comments.LoadBadgeAsync(1);
        Assert.Single(requests.Items);
        requests.Items[0].Complete(Track(1, 3837270, 42427, 30149));
        await Task.WhenAll(first, second, badge);
        Assert.Equal("383w7+", vm.FavoriteBadgeText);
        Assert.Equal("4w2+", vm.ShareBadgeText);
        Assert.Equal("3w+", comments.BadgeText);
        await vm.LoadAsync(Track(1));
        await comments.LoadBadgeAsync(1);
        Assert.Single(requests.Items);
    }

    [Fact]
    public async Task SongChanges_ClearOldCountsAndRejectLateResponses()
    {
        var requests = new Requests();
        using var vm = new TrackStatisticsViewModel(requests.Api);
        var first = vm.LoadAsync(Track(1, favorite: 123));
        var second = vm.LoadAsync(Track(2));
        Assert.True(requests.Items[0].Cancellation.IsCancellationRequested);
        Assert.Null(vm.FavoriteCount);
        Assert.Null(vm.ShareCount);
        requests.Items[1].Complete(Track(2, 8, 9, 10));
        await second;
        requests.Items[0].Complete(Track(1, 99, 98, 97));
        await first;
        Assert.Equal(2, vm.MusicId);
        Assert.Equal(8, vm.FavoriteCount);
        Assert.Equal(9, vm.ShareCount);
    }

    [Fact]
    public async Task CommentCancellation_DoesNotCancelPlaybackStatistics()
    {
        var requests = new Requests();
        using var vm = new TrackStatisticsViewModel(requests.Api);
        var loading = vm.LoadAsync(Track(1));
        var comments = new SongCommentsViewModel(requests.Api, BodianSession.CreateAnonymous(), statistics: vm);
        var badge = comments.LoadBadgeAsync(1);
        comments.CancelBadgeRequest();
        await badge;
        Assert.False(requests.Items[0].Cancellation.IsCancellationRequested);
        requests.Items[0].Complete(Track(1, 5, 6, 7));
        await loading;
        Assert.Equal(5, vm.FavoriteCount);
        Assert.Equal(6, vm.ShareCount);
        Assert.Null(comments.BadgeCount);
    }

    [Fact]
    public async Task RequestFailure_KeepsKnownCountsAndCanRetry()
    {
        var requests = new Requests();
        using var vm = new TrackStatisticsViewModel(requests.Api);
        var loading = vm.LoadAsync(Track(1, favorite: 12));
        requests.Items[0].Completion.SetException(new IOException("Synthetic failure"));
        await loading;
        Assert.Equal(12, vm.FavoriteCount);
        Assert.False(vm.HasShareCount);
        var retry = vm.LoadAsync(Track(1, favorite: 12));
        Assert.Equal(2, requests.Items.Count);
        requests.Items[1].Complete(Track(1, 13, 14, 15));
        await retry;
        Assert.Equal(14, vm.ShareCount);
    }

    [Fact]
    public async Task WrongTrackDetail_IsNotApplied()
    {
        var requests = new Requests();
        using var vm = new TrackStatisticsViewModel(requests.Api);
        var loading = vm.LoadAsync(Track(1));
        requests.Items[0].Complete(Track(2, 900, 800, 700));
        await loading;
        Assert.Null(vm.FavoriteCount);
        Assert.Null(vm.ShareCount);
    }

    [Fact]
    public async Task NegativeMetadata_IsClampedToZero()
    {
        var api = new PlaybackApiStub();
        using var vm = new TrackStatisticsViewModel(api);
        await vm.LoadAsync(Track(1, -1, -2, -3));
        Assert.Equal("0", vm.FavoriteBadgeText);
        Assert.Equal("0", vm.ShareBadgeText);
    }

    [Fact]
    public async Task Disposal_CancelsRequestAndRejectsItsLateResponse()
    {
        var requests = new Requests();
        var vm = new TrackStatisticsViewModel(requests.Api);
        var loading = vm.LoadAsync(Track(1));
        vm.Dispose();
        Assert.True(requests.Items[0].Cancellation.IsCancellationRequested);
        requests.Items[0].Complete(Track(1, 8, 9, 10));
        await loading;
        Assert.Null(vm.FavoriteCount);
        Assert.Null(vm.ShareCount);
    }

    // ── 喜欢往返 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Favorite_TogglesBothWaysAndMovesTheCountByOne()
    {
        var api = new PlaybackApiStub();
        var liked = new LikedSongsStub();
        using var vm = new TrackStatisticsViewModel(api, likedSongs: liked);
        await vm.LoadAsync(Track(1, favorite: 10));
        Assert.False(vm.IsFavorite);

        Assert.Equal(LikedSongsOutcome.Succeeded, await vm.ToggleFavoriteAsync(Ct));
        Assert.True(vm.IsFavorite);
        Assert.Equal(11, vm.FavoriteCount);
        Assert.Equal([(1L, true)], liked.Requests);

        // 已喜欢时点按是取消，走反向。
        Assert.Equal(LikedSongsOutcome.Succeeded, await vm.ToggleFavoriteAsync(Ct));
        Assert.False(vm.IsFavorite);
        Assert.Equal(10, vm.FavoriteCount);
        Assert.Equal([(1L, true), (1L, false)], liked.Requests);
    }

    /// <summary>计数只在写成功之后动；失败时状态和数字都不能变。</summary>
    [Fact]
    public async Task Favorite_KeepsTheCountWhenTheWriteFails()
    {
        var api = new PlaybackApiStub();
        var liked = new LikedSongsStub { Next = LikedSongsOutcome.Failed };
        using var vm = new TrackStatisticsViewModel(api, likedSongs: liked);
        await vm.LoadAsync(Track(1, favorite: 10));

        Assert.Equal(LikedSongsOutcome.Failed, await vm.ToggleFavoriteAsync(Ct));
        Assert.False(vm.IsFavorite);
        Assert.Equal(10, vm.FavoriteCount);
    }

    [Theory]
    [InlineData(LikedSongsOutcome.NotAuthenticated)]
    [InlineData(LikedSongsOutcome.NoLikedPlaylist)]
    [InlineData(LikedSongsOutcome.AlreadyPending)]
    public async Task Favorite_LeavesStateAloneWhenItCannotWrite(LikedSongsOutcome outcome)
    {
        var api = new PlaybackApiStub();
        var liked = new LikedSongsStub { Next = outcome };
        using var vm = new TrackStatisticsViewModel(api, likedSongs: liked);
        await vm.LoadAsync(Track(1, favorite: 10));

        Assert.Equal(outcome, await vm.ToggleFavoriteAsync(Ct));
        Assert.False(vm.IsFavorite);
        Assert.Equal(10, vm.FavoriteCount);
    }

    /// <summary>计数未知时不能凭空造一个数字出来。</summary>
    [Fact]
    public async Task Favorite_LeavesAnUnknownCountUnknown()
    {
        var api = new PlaybackApiStub();
        var liked = new LikedSongsStub();
        using var vm = new TrackStatisticsViewModel(api, likedSongs: liked);
        await vm.LoadAsync(Track(1, favorite: null));

        Assert.Equal(LikedSongsOutcome.Succeeded, await vm.ToggleFavoriteAsync(Ct));
        Assert.True(vm.IsFavorite);
        Assert.Null(vm.FavoriteCount);
    }

    [Fact]
    public async Task Favorite_ReflectsTheKnownSetOnTrackChange()
    {
        var api = new PlaybackApiStub();
        var liked = new LikedSongsStub();
        liked.Liked.Add(2);
        using var vm = new TrackStatisticsViewModel(api, likedSongs: liked);

        await vm.LoadAsync(Track(1, favorite: 3, share: 0, comment: 0));
        Assert.False(vm.IsFavorite);

        await vm.LoadAsync(Track(2, favorite: 3, share: 0, comment: 0));
        Assert.True(vm.IsFavorite);
    }

    /// <summary>没有喜欢服务（离线测试宿主）时不能假装写成功。</summary>
    [Fact]
    public async Task Favorite_WithoutTheServiceReportsFailure()
    {
        var api = new PlaybackApiStub();
        using var vm = new TrackStatisticsViewModel(api);
        await vm.LoadAsync(Track(1, favorite: 10));

        Assert.Equal(LikedSongsOutcome.Failed, await vm.ToggleFavoriteAsync(Ct));
        Assert.Equal(10, vm.FavoriteCount);
    }

    // ── 分享上报 ────────────────────────────────────────────────────────────

    /// <summary>上报是写操作：成功一次就 +1（文档 2.8 实测）。</summary>
    [Fact]
    public async Task Share_BumpsTheCountOnce()
    {
        var api = new PlaybackApiStub { ReportShare = (_, _) => Task.FromResult(ShareOutcome.Succeeded) };
        using var vm = new TrackStatisticsViewModel(api);
        await vm.LoadAsync(Track(1, favorite: 0, share: 42, comment: 0));

        Assert.Equal(ShareOutcome.Succeeded, await vm.ReportShareAsync(Ct));
        Assert.Equal(43, vm.ShareCount);
    }

    [Fact]
    public async Task Share_KeepsTheCountWhenTheReportFails()
    {
        var api = new PlaybackApiStub { ReportShare = (_, _) => Task.FromResult(ShareOutcome.Failed) };
        using var vm = new TrackStatisticsViewModel(api);
        await vm.LoadAsync(Track(1, favorite: 0, share: 42, comment: 0));

        Assert.Equal(ShareOutcome.Failed, await vm.ReportShareAsync(Ct));
        Assert.Equal(42, vm.ShareCount);
    }

    /// <summary>不支持分享（23006）也不该动计数。</summary>
    [Fact]
    public async Task Share_KeepsTheCountWhenUnsupported()
    {
        var api = new PlaybackApiStub { ReportShare = (_, _) => Task.FromResult(ShareOutcome.Unsupported) };
        using var vm = new TrackStatisticsViewModel(api);
        await vm.LoadAsync(Track(1, favorite: 0, share: 42, comment: 0));

        Assert.Equal(ShareOutcome.Unsupported, await vm.ReportShareAsync(Ct));
        Assert.Equal(42, vm.ShareCount);
    }

    [Fact]
    public async Task Share_LeavesAnUnknownCountUnknown()
    {
        var api = new PlaybackApiStub { ReportShare = (_, _) => Task.FromResult(ShareOutcome.Succeeded) };
        using var vm = new TrackStatisticsViewModel(api);
        await vm.LoadAsync(Track(1, favorite: 0, share: null, comment: 0));

        Assert.Equal(ShareOutcome.Succeeded, await vm.ReportShareAsync(Ct));
        Assert.Null(vm.ShareCount);
    }

    /// <summary>上报抛异常不能把分享流程带崩 —— 链接是本地拼的，照旧要能复制。</summary>
    [Fact]
    public async Task Share_SwallowsTransportFailures()
    {
        var api = new PlaybackApiStub { ReportShare = (_, _) => throw new HttpRequestException("网络不可用") };
        using var vm = new TrackStatisticsViewModel(api);
        await vm.LoadAsync(Track(1, favorite: 0, share: 42, comment: 0));

        Assert.Equal(ShareOutcome.Failed, await vm.ReportShareAsync(Ct));
        Assert.Equal(42, vm.ShareCount);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Requests
    {
        public List<Pending> Items { get; } = [];
        public PlaybackApiStub Api { get; }
        public Requests()
        {
            Api = new PlaybackApiStub { GetTrack = (id, cancellation) =>
            {
                var request = new Pending(id, cancellation);
                Items.Add(request);
                return request.Completion.Task;
            } };
        }
    }
    private sealed class Pending(long id, CancellationToken cancellation)
    {
        public long Id { get; } = id;
        public CancellationToken Cancellation { get; } = cancellation;
        public TaskCompletionSource<Track?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Complete(Track? track) => Completion.SetResult(track);
    }
}
