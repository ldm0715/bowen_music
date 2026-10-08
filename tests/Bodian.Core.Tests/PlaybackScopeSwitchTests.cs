using Bodian.Core.Models;
using Bodian.Core.Tests.Support;
using Bodian.WinUI.Playback;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 换账号时播放队列跟着换一份。
/// </summary>
public sealed class PlaybackScopeSwitchTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Track Track(long id) => new()
    {
        Id = id,
        Title = $"Track {id}",
        Duration = TimeSpan.FromSeconds(180),
        AvailableQualities = [AudioQuality.Lossless],
    };

    private static PlaybackApiStub Api() => new()
    {
        Resolve = (track, quality, _) => Task.FromResult<PlaybackResolution>(new PlaybackResolution.Playable(new AudioSource
        {
            Url = new Uri($"https://audio.example.invalid/{track.Id}/{quality}"),
            RequestedQuality = quality, Format = AudioQualityTable.ExpectedFormat(quality),
            BitrateKbps = AudioQualityTable.ExpectedBitrateKbps(quality),
        })),
    };

    private static QueuedTrack Queued(long id) => QueuedTrack.From(Track(id));

    /// <summary>
    /// 切号要动绑定到界面的东西，协调器会 marshal 回「构造它的那个线程」。
    /// 这里显式声明没有 UI 线程，好让切号在断言之前同步做完。
    /// </summary>
    private static void RunWithoutUiThread() => SynchronizationContext.SetSynchronizationContext(null);

    [Fact]
    public void SigningIn_SwapsToThatAccountsQueue_AndReportsSomethingWasReplaced()
    {
        RunWithoutUiThread();

        var account = new FakeCurrentAccount();
        var store = new FakePlayQueueSnapshotStore
        {
            Stored = new PlayQueueSnapshot { Items = [Queued(1)] },
        };
        store.Save("111", new PlayQueueSnapshot { Items = [Queued(2)] });

        using var coordinator = new PlaybackCoordinator(
            Api(), new FakePlaybackEngine(), new FakePlayHistoryStore(), queueStore: store, account: account);

        Assert.Equal(1, coordinator.Queue.Current!.Id);

        QueueScopeSwitchedEventArgs? reported = null;
        coordinator.ScopeSwitched += (_, e) => reported = e;

        account.SignIn("111");

        Assert.Equal(2, coordinator.Queue.Current!.Id);
        Assert.NotNull(reported);
        Assert.True(reported.HadSomething);
    }

    [Fact]
    public void SigningOut_GoesBackToTheAnonymousQueue()
    {
        RunWithoutUiThread();

        var account = new FakeCurrentAccount();
        var store = new FakePlayQueueSnapshotStore { Stored = new PlayQueueSnapshot { Items = [Queued(1)] } };
        store.Save("111", new PlayQueueSnapshot { Items = [Queued(2)] });

        using var coordinator = new PlaybackCoordinator(
            Api(), new FakePlaybackEngine(), new FakePlayHistoryStore(), queueStore: store, account: account);

        account.SignIn("111");
        Assert.Equal(2, coordinator.Queue.Current!.Id);

        account.SignOut();

        Assert.Equal(1, coordinator.Queue.Current!.Id);
    }

    [Fact]
    public void StartupRestore_DoesNotAnnounceAQueueSwitch()
    {
        RunWithoutUiThread();

        // 启动时协调器先按匿名装（空），恢复会话再切到账号 —— 这不是用户眼里的「换了队列」。
        var account = new FakeCurrentAccount();
        var store = new FakePlayQueueSnapshotStore();
        store.Save("111", new PlayQueueSnapshot { Items = [Queued(2)] });

        using var coordinator = new PlaybackCoordinator(
            Api(), new FakePlaybackEngine(), new FakePlayHistoryStore(), queueStore: store, account: account);

        QueueScopeSwitchedEventArgs? reported = null;
        coordinator.ScopeSwitched += (_, e) => reported = e;

        account.SignIn("111");

        Assert.Equal(2, coordinator.Queue.Current!.Id);
        Assert.NotNull(reported);
        Assert.False(reported.HadSomething);
    }

    [Fact]
    public async Task Switching_StopsTheTrackThatBelongsToTheOtherAccount()
    {
        RunWithoutUiThread();

        var account = new FakeCurrentAccount();
        var engine = new FakePlaybackEngine();
        var store = new FakePlayQueueSnapshotStore();

        using var coordinator = new PlaybackCoordinator(
            Api(), engine, new FakePlayHistoryStore(), queueStore: store, account: account);

        await coordinator.PlayFromAsync([Track(1), Track(2)], 1, Ct);
        Assert.NotNull(coordinator.CurrentTrack);

        account.SignIn("111");

        // 停播：上一首属于别的账号，用新账号的权限接着播可能被拒或降级。
        Assert.Null(coordinator.CurrentTrack);
        Assert.Equal(PlaybackState.Stopped, engine.State);
        Assert.Empty(coordinator.Queue.Items);
    }

    [Fact]
    public async Task Switching_WritesTheOutgoingQueueUnderTheOutgoingScope()
    {
        RunWithoutUiThread();

        var account = new FakeCurrentAccount();
        var store = new FakePlayQueueSnapshotStore();
        store.Save("111", new PlayQueueSnapshot { Items = [Queued(9)] });

        using var coordinator = new PlaybackCoordinator(
            Api(), new FakePlaybackEngine(), new FakePlayHistoryStore(), queueStore: store, account: account);

        await coordinator.PlayFromAsync([Track(1), Track(2)], 1, Ct);

        account.SignIn("111");

        // 落盘在后台线程上，给它一点时间；切号之前那份快照必须写在匿名作用域下。
        var written = await WaitForSaveAsync(store, "anonymous");

        Assert.Equal(2, written.Items.Length);

        // 而它绝不能落进新账号的目录：防抖窗口里换号，这份快照的归属必须在抓它的那一刻定下来。
        Assert.DoesNotContain(
            store.Saves.Where(save => save.Scope == "111").Select(save => save.Snapshot),
            snapshot => snapshot.Items.Any(item => item.Id == 1));
    }

    private static async Task<PlayQueueSnapshot> WaitForSaveAsync(
        FakePlayQueueSnapshotStore store, string scope)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var match = store.Saves
                .Where(save => save.Scope == scope)
                .Select(save => save.Snapshot)
                .LastOrDefault();

            if (match is not null)
            {
                return match;
            }

            await Task.Delay(20, Ct);
        }

        Assert.Fail($"没有在 {scope} 作用域下观察到落盘");
        throw new InvalidOperationException();
    }
}
