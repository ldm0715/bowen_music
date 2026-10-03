using Bodian.Core.Models;
using Bodian.Core.Services.Abstractions;
using Bodian.Core.Tests.Support;
using Bodian.WinUI.Playback;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 协调器上的队列命令与播放模式。
/// </summary>
/// <remarks>
/// 队列本身的行为在 <see cref="PlayQueueTests"/> 里验；这里只看「命令有没有把队列和引擎一起照顾好」——
/// 尤其是<b>换个模式之后自动续播还对不对</b>，那条路只在引擎报「放完了」的时候才走到。
/// </remarks>
public sealed class PlayQueueCommandTests
{
    private static Track Track(long id) => new()
    {
        Id = id, Title = $"Track {id}",
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

    [Fact]
    public async Task AddToQueue_AppendsWithoutSwitchingTracks()
    {
        var engine = new FakePlaybackEngine();
        using var coordinator = new PlaybackCoordinator(Api(), engine, new FakePlayHistoryStore());
        await coordinator.PlayFromAsync([Track(1), Track(2)], 0, Ct);

        await coordinator.AddToQueueAsync(Track(3), Ct);

        Assert.Equal(3, coordinator.Queue.Count);
        Assert.Equal(3, coordinator.Queue.Items[2].Id);
        Assert.Equal(1, coordinator.CurrentTrack!.Id);
        Assert.Equal(1, engine.LoadCount);
    }

    [Fact]
    public async Task AddToQueue_OnAnEmptyQueue_StartsPlaying()
    {
        var engine = new FakePlaybackEngine();
        using var coordinator = new PlaybackCoordinator(Api(), engine, new FakePlayHistoryStore());

        await coordinator.AddToQueueAsync(Track(9), Ct);

        Assert.Equal(1, coordinator.Queue.Count);
        Assert.Equal(9, coordinator.CurrentTrack!.Id);
        Assert.Equal(1, engine.LoadCount);
    }

    [Fact]
    public async Task PlayNext_InsertsRightAfterTheCurrentTrack()
    {
        var engine = new FakePlaybackEngine();
        using var coordinator = new PlaybackCoordinator(Api(), engine, new FakePlayHistoryStore());
        await coordinator.PlayFromAsync([Track(1), Track(2), Track(3)], 0, Ct);

        await coordinator.PlayNextAsync(Track(8), Ct);

        Assert.Equal([1L, 8L, 2L, 3L], coordinator.Queue.Items.Select(t => t.Id));
        Assert.Equal(1, coordinator.CurrentTrack!.Id);

        await coordinator.NextAsync(Ct);

        Assert.Equal(8, coordinator.CurrentTrack!.Id);
    }

    [Fact]
    public async Task PlayNext_OnAnEmptyQueue_StartsPlaying()
    {
        var engine = new FakePlaybackEngine();
        using var coordinator = new PlaybackCoordinator(Api(), engine, new FakePlayHistoryStore());

        await coordinator.PlayNextAsync(Track(6), Ct);

        Assert.Equal(6, coordinator.CurrentTrack!.Id);
        Assert.Equal(1, engine.LoadCount);
    }

    [Fact]
    public async Task PlayQueueItem_JumpsToThatTrack()
    {
        var engine = new FakePlaybackEngine();
        using var coordinator = new PlaybackCoordinator(Api(), engine, new FakePlayHistoryStore());
        await coordinator.PlayFromAsync([Track(1), Track(2), Track(3)], 0, Ct);

        await coordinator.PlayQueueItemAsync(2, Ct);

        Assert.Equal(3, coordinator.CurrentTrack!.Id);
        Assert.Contains("/3/", engine.Last!.StreamUrl.AbsolutePath);
    }

    [Fact]
    public async Task PlayQueueItem_OutOfRange_DoesNothing()
    {
        var engine = new FakePlaybackEngine();
        using var coordinator = new PlaybackCoordinator(Api(), engine, new FakePlayHistoryStore());
        await coordinator.PlayFromAsync([Track(1), Track(2)], 0, Ct);

        await coordinator.PlayQueueItemAsync(5, Ct);

        Assert.Equal(1, coordinator.CurrentTrack!.Id);
        Assert.Equal(1, engine.LoadCount);
    }

    [Fact]
    public async Task RemoveQueueItem_LeavesThePlayingTrackAlone()
    {
        var engine = new FakePlaybackEngine();
        using var coordinator = new PlaybackCoordinator(Api(), engine, new FakePlayHistoryStore());
        await coordinator.PlayFromAsync([Track(1), Track(2), Track(3)], 0, Ct);

        await coordinator.RemoveQueueItemAsync(2, Ct);

        Assert.Equal(2, coordinator.Queue.Count);
        Assert.Equal(1, coordinator.CurrentTrack!.Id);
        Assert.Equal(1, engine.LoadCount);
    }

    [Fact]
    public async Task RemoveQueueItem_ThePlayingOne_MovesOn()
    {
        var engine = new FakePlaybackEngine();
        using var coordinator = new PlaybackCoordinator(Api(), engine, new FakePlayHistoryStore());
        await coordinator.PlayFromAsync([Track(1), Track(2), Track(3)], 0, Ct);

        await coordinator.RemoveQueueItemAsync(0, Ct);

        Assert.Equal(2, coordinator.Queue.Count);
        Assert.Equal(2, coordinator.CurrentTrack!.Id);
        Assert.Contains("/2/", engine.Last!.StreamUrl.AbsolutePath);
    }

    [Fact]
    public async Task ClearQueue_DoesNotInterruptPlayback()
    {
        var engine = new FakePlaybackEngine();
        using var coordinator = new PlaybackCoordinator(Api(), engine, new FakePlayHistoryStore());
        await coordinator.PlayFromAsync([Track(1), Track(2)], 0, Ct);

        coordinator.ClearQueue();

        Assert.Equal(0, coordinator.Queue.Count);
        Assert.Equal(1, coordinator.CurrentTrack!.Id);
        Assert.Equal(1, engine.LoadCount);
        Assert.Equal(PlaybackState.Playing, engine.State);
    }

    [Fact]
    public async Task ListLoop_AutoAdvanceWrapsInsteadOfStopping()
    {
        var engine = new FakePlaybackEngine();
        using var coordinator = new PlaybackCoordinator(Api(), engine, new FakePlayHistoryStore());
        await coordinator.PlayFromAsync([Track(1), Track(2)], 0, Ct);

        var exhausted = 0;
        coordinator.QueueExhausted += (_, _) => exhausted++;

        coordinator.CyclePlayMode();
        Assert.Equal(PlayMode.ListLoop, coordinator.Mode);

        engine.RaiseEnded();
        Assert.Equal(2, coordinator.CurrentTrack!.Id);

        engine.RaiseEnded();
        Assert.Equal(1, coordinator.CurrentTrack!.Id);

        Assert.Equal(0, exhausted);
    }

    [Fact]
    public async Task Sequential_AutoAdvanceStopsAtTheEnd()
    {
        var engine = new FakePlaybackEngine();
        using var coordinator = new PlaybackCoordinator(Api(), engine, new FakePlayHistoryStore());
        await coordinator.PlayFromAsync([Track(1), Track(2)], 0, Ct);

        var exhausted = 0;
        coordinator.QueueExhausted += (_, _) => exhausted++;

        engine.RaiseEnded();
        Assert.Equal(2, coordinator.CurrentTrack!.Id);

        engine.RaiseEnded();

        Assert.Equal(1, exhausted);
        Assert.Equal(2, coordinator.CurrentTrack!.Id);
        Assert.Equal(2, engine.LoadCount);
    }

    [Fact]
    public async Task Shuffle_AutoAdvanceNeverRepeatsTheTrackItJustFinished()
    {
        var engine = new FakePlaybackEngine();
        using var coordinator = new PlaybackCoordinator(Api(), engine, new FakePlayHistoryStore());
        await coordinator.PlayFromAsync([Track(1), Track(2), Track(3), Track(4)], 0, Ct);

        coordinator.SetPlayMode(PlayMode.Shuffle);

        var visited = new List<long> { coordinator.CurrentTrack!.Id };

        for (var i = 0; i < 3; i++)
        {
            engine.RaiseEnded();
            visited.Add(coordinator.CurrentTrack!.Id);
        }

        Assert.Equal(4, visited.Distinct().Count());
    }

    [Fact]
    public async Task CyclePlayMode_WalksTheThreeModesInOrder()
    {
        var engine = new FakePlaybackEngine();
        var settings = new Settings();
        using var coordinator = new PlaybackCoordinator(Api(), engine, new FakePlayHistoryStore(), playbackSettings: settings);

        Assert.Equal(PlayMode.Sequential, coordinator.Mode);

        coordinator.CyclePlayMode();
        Assert.Equal(PlayMode.ListLoop, coordinator.Mode);

        coordinator.CyclePlayMode();
        Assert.Equal(PlayMode.Shuffle, coordinator.Mode);

        coordinator.CyclePlayMode();
        Assert.Equal(PlayMode.Sequential, coordinator.Mode);

        Assert.Equal(PlayMode.Sequential, settings.Mode);
    }

    [Fact]
    public async Task SavedMode_IsRestoredOnTheNextStartup()
    {
        var settings = new Settings { Mode = PlayMode.ListLoop };
        var engine = new FakePlaybackEngine();
        using (var coordinator = new PlaybackCoordinator(Api(), engine, new FakePlayHistoryStore(), playbackSettings: settings))
        {
            Assert.Equal(PlayMode.ListLoop, coordinator.Mode);
        }

        using var restarted = new PlaybackCoordinator(Api(), new FakePlaybackEngine(), new FakePlayHistoryStore(), playbackSettings: settings);

        Assert.Equal(PlayMode.ListLoop, restarted.Mode);
    }

    private sealed class Settings : IPlaybackSettingsStore
    {
        public PlayMode Mode { get; set; } = PlayMode.Sequential;

        public PlayMode Load() => Mode;

        public void Save(PlayMode mode) => Mode = mode;
    }
}
