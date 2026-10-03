using Bodian.Core.Api;
using Bodian.Core.Models;
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
