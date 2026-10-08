using Bodian.Core.Models;
using Bodian.Core.Tests.Support;
using Bodian.WinUI.Playback;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 重启后恢复播放列表：恢复时机、续播位置、开关与退出落盘。
/// </summary>
public sealed class PlayQueueRestoreTests
{
    private static Track Track(long id, int durationSeconds = 0) => new()
    {
        Id = id,
        Title = $"Track {id}",
        Duration = TimeSpan.FromSeconds(durationSeconds),
        AvailableQualities = [AudioQuality.Lossless, AudioQuality.High, AudioQuality.Standard],
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

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>摆一份「上次退出时的快照」。</summary>
    private static FakePlayQueueSnapshotStore SavedQueue(
        IReadOnlyList<Track> tracks, long currentId, double positionSeconds)
    {
        var list = tracks.ToList();

        return new FakePlayQueueSnapshotStore
        {
            Stored = new PlayQueueSnapshot
            {
                Items = [.. tracks.Select(QueuedTrack.From)],
                CurrentTrackId = currentId,
                CurrentIndex = list.FindIndex(t => t.Id == currentId),
                PositionSeconds = positionSeconds,
            },
        };
    }

    [Fact]
    public void Restore_BringsBackTheQueueAndCurrentTrack_WithoutAutoPlaying()
    {
        var engine = new FakePlaybackEngine();
        var store = SavedQueue([Track(1), Track(2), Track(3)], 2, 42);

        using var coordinator = new PlaybackCoordinator(
            Api(), engine, new FakePlayHistoryStore(), queueStore: store);

        Assert.Equal(3, coordinator.Queue.Count);
        Assert.Equal(2, coordinator.Queue.Current!.Id);
        Assert.Equal(1, coordinator.Queue.CurrentIndex);

        // 不自动播放：恢复只是把列表摆回来，等用户点。
        Assert.Equal(0, engine.LoadCount);

        Assert.NotNull(coordinator.RestoredState);
        Assert.Equal(2, coordinator.RestoredState!.Track.Id);
        Assert.Equal(42, coordinator.RestoredState.Position.TotalSeconds);
    }

    [Fact]
    public async Task PlayAfterRestore_ResumesFromTheSavedPosition()
    {
        var engine = new FakePlaybackEngine();
        var store = SavedQueue([Track(1, 300), Track(2, 300)], 1, 42);

        using var coordinator = new PlaybackCoordinator(
            Api(), engine, new FakePlayHistoryStore(), queueStore: store);

        await coordinator.PlayAsync(Ct);

        Assert.NotNull(engine.Last);
        Assert.Equal(TimeSpan.FromSeconds(42), engine.Last!.Start);
    }

    [Fact]
    public async Task NearTheEndOfTheTrack_TheResumePositionIsDropped()
    {
        var engine = new FakePlaybackEngine();
        // 300 秒的歌停在 295 秒 —— 只剩 5 秒，续播只会听到个尾巴。
        var store = SavedQueue([Track(1, 300)], 1, 295);

        using var coordinator = new PlaybackCoordinator(
            Api(), engine, new FakePlayHistoryStore(), queueStore: store);

        await coordinator.PlayAsync(Ct);

        Assert.NotNull(engine.Last);
        Assert.Null(engine.Last!.Start);
    }

    [Fact]
    public async Task SwitchingToAnotherTrack_DoesNotResume()
    {
        var engine = new FakePlaybackEngine();
        var store = SavedQueue([Track(1, 300), Track(2, 300)], 1, 42);

        using var coordinator = new PlaybackCoordinator(
            Api(), engine, new FakePlayHistoryStore(), queueStore: store);

        await coordinator.PlayQueueItemAsync(1, Ct);

        Assert.Equal(2, coordinator.Queue.Current!.Id);
        Assert.Null(engine.Last!.Start);
    }

    [Fact]
    public void WhenTheSwitchIsOff_NothingIsRestored()
    {
        var engine = new FakePlaybackEngine();
        var store = SavedQueue([Track(1), Track(2)], 1, 42);
        store.Stored = store.Stored with { RestoreEnabled = false };

        using var coordinator = new PlaybackCoordinator(
            Api(), engine, new FakePlayHistoryStore(), queueStore: store);

        Assert.Equal(0, coordinator.Queue.Count);
        Assert.False(coordinator.RestoreQueueEnabled);
        Assert.Null(coordinator.RestoredState);
    }

    [Fact]
    public void TurningTheSwitchOff_WritesAnEmptyQueue()
    {
        var engine = new FakePlaybackEngine();
        var store = SavedQueue([Track(1), Track(2)], 1, 42);

        using var coordinator = new PlaybackCoordinator(
            Api(), engine, new FakePlayHistoryStore(), queueStore: store);

        Assert.True(coordinator.SetRestoreQueueEnabled(false));

        var written = store.Saves[^1];
        Assert.False(written.RestoreEnabled);
        Assert.Empty(written.Items);

        // 内存里的队列不受影响 —— 关掉的只是「记不记」。
        Assert.Equal(2, coordinator.Queue.Count);
    }

    [Fact]
    public void WhenSavingTheSwitchFails_ItRollsBack()
    {
        var engine = new FakePlaybackEngine();
        var store = SavedQueue([Track(1)], 1, 0);

        using var coordinator = new PlaybackCoordinator(
            Api(), engine, new FakePlayHistoryStore(), queueStore: store);
        store.FailSaves = true;

        Assert.False(coordinator.SetRestoreQueueEnabled(false));
        Assert.True(coordinator.RestoreQueueEnabled);
    }

    [Fact]
    public async Task FlushForShutdown_WritesTheCurrentQueue()
    {
        var engine = new FakePlaybackEngine();
        var store = new FakePlayQueueSnapshotStore();

        using var coordinator = new PlaybackCoordinator(
            Api(), engine, new FakePlayHistoryStore(), queueStore: store);

        await coordinator.PlayFromAsync([Track(1), Track(2)], 1, Ct);
        coordinator.FlushForShutdown();

        var written = store.Saves[^1];
        Assert.Equal(2, written.Items.Length);
        Assert.Equal(2, written.CurrentTrackId);
        Assert.Equal(1, written.CurrentIndex);
    }

    [Fact]
    public void CorruptEntriesAreDropped_AndTheCurrentTrackIsFoundById()
    {
        var engine = new FakePlaybackEngine();
        var store = new FakePlayQueueSnapshotStore
        {
            Stored = new PlayQueueSnapshot
            {
                // 中间那条没有标题，属于坏项；它被丢掉后下标会整体错位，所以得靠 id 找回当前曲目。
                Items =
                [
                    new QueuedTrack { Id = 1, Title = "第一首" },
                    new QueuedTrack { Id = 0, Title = "" },
                    new QueuedTrack { Id = 3, Title = "第三首" },
                ],
                CurrentTrackId = 3,
                CurrentIndex = 2,
            },
        };

        using var coordinator = new PlaybackCoordinator(
            Api(), engine, new FakePlayHistoryStore(), queueStore: store);

        Assert.Equal(2, coordinator.Queue.Count);
        Assert.Equal(3, coordinator.Queue.Current!.Id);
        Assert.Equal(1, coordinator.Queue.CurrentIndex);
    }
}
