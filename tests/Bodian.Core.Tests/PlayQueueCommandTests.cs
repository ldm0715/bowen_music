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
    public async Task AddToQueue_WhenTheTrackIsAlreadyQueued_ReportsNotAdded()
    {
        var engine = new FakePlaybackEngine();
        using var coordinator = new PlaybackCoordinator(Api(), engine, new FakePlayHistoryStore());
        await coordinator.PlayFromAsync([Track(1), Track(2)], 0, Ct);

        Assert.False(await coordinator.AddToQueueAsync(Track(2), Ct));

        Assert.Equal(2, coordinator.Queue.Count);
        Assert.Equal(1, engine.LoadCount);
    }

    /// <summary>
    /// 点一首歌：排到队尾并立即播放它。<b>队列里原来那些必须留着</b> ——
    /// 清空队列是上一版的做法，用起来很怪。
    /// </summary>
    [Fact]
    public async Task EnqueueAndPlay_AppendsToTheTailAndPlaysIt()
    {
        var engine = new FakePlaybackEngine();
        using var coordinator = new PlaybackCoordinator(Api(), engine, new FakePlayHistoryStore());
        await coordinator.PlayFromAsync([Track(1), Track(2)], 0, Ct);

        await coordinator.EnqueueAndPlayAsync(Track(3), Ct);

        Assert.Equal([1L, 2L, 3L], coordinator.Queue.Items.Select(t => t.Id));
        Assert.Equal(3, coordinator.CurrentTrack!.Id);
        Assert.Equal(2, coordinator.Queue.CurrentIndex);
    }

    /// <summary>队列里已经有这一首时不重复添加，跳到原来那一份放。</summary>
    [Fact]
    public async Task EnqueueAndPlay_WhenAlreadyQueued_JumpsToItWithoutDuplicating()
    {
        var engine = new FakePlaybackEngine();
        using var coordinator = new PlaybackCoordinator(Api(), engine, new FakePlayHistoryStore());
        await coordinator.PlayFromAsync([Track(1), Track(2), Track(3)], 0, Ct);

        await coordinator.EnqueueAndPlayAsync(Track(2), Ct);

        Assert.Equal([1L, 2L, 3L], coordinator.Queue.Items.Select(t => t.Id));
        Assert.Equal(2, coordinator.CurrentTrack!.Id);
    }

    [Fact]
    public async Task EnqueueAndPlay_OnAnEmptyQueue_StartsPlaying()
    {
        var engine = new FakePlaybackEngine();
        using var coordinator = new PlaybackCoordinator(Api(), engine, new FakePlayHistoryStore());

        await coordinator.EnqueueAndPlayAsync(Track(7), Ct);

        Assert.Equal(1, coordinator.Queue.Count);
        Assert.Equal(7, coordinator.CurrentTrack!.Id);
    }

    [Fact]
    public async Task AddToQueueRange_AppendsTheNewOnesOnly()
    {
        var engine = new FakePlaybackEngine();
        using var coordinator = new PlaybackCoordinator(Api(), engine, new FakePlayHistoryStore());
        await coordinator.PlayFromAsync([Track(1), Track(2)], 0, Ct);

        var added = await coordinator.AddToQueueAsync([Track(2), Track(3), Track(4)], Ct);

        Assert.Equal(2, added);
        Assert.Equal([1L, 2L, 3L, 4L], coordinator.Queue.Items.Select(t => t.Id));
        Assert.Equal(1, coordinator.CurrentTrack!.Id);
        Assert.Equal(1, engine.LoadCount);
    }

    [Fact]
    public async Task AddToQueueRange_OnAnEmptyQueue_StartsPlaying()
    {
        var engine = new FakePlaybackEngine();
        using var coordinator = new PlaybackCoordinator(Api(), engine, new FakePlayHistoryStore());

        var added = await coordinator.AddToQueueAsync([Track(5), Track(6)], Ct);

        Assert.Equal(2, added);
        Assert.Equal(5, coordinator.CurrentTrack!.Id);
        Assert.Equal(1, engine.LoadCount);
    }

    /// <summary>队列是空的但一首都没加进去（全是重复或没有效 Id）时不该开播 —— 空队列没什么可播的。</summary>
    [Fact]
    public async Task AddToQueueRange_WhenNothingWasAdded_DoesNotStartPlaying()
    {
        var engine = new FakePlaybackEngine();
        using var coordinator = new PlaybackCoordinator(Api(), engine, new FakePlayHistoryStore());

        var added = await coordinator.AddToQueueAsync([Track(0), Track(-1)], Ct);

        Assert.Equal(0, added);
        Assert.Equal(0, coordinator.Queue.Count);
        Assert.Null(coordinator.CurrentTrack);
        Assert.Equal(0, engine.LoadCount);
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
    public async Task Sequential_AfterExhaustion_PlayRestartsFromTheFirstTrack()
    {
        var engine = new FakePlaybackEngine();
        using var coordinator = new PlaybackCoordinator(Api(), engine, new FakePlayHistoryStore());
        await coordinator.PlayFromAsync([Track(1), Track(2), Track(3)], 0, Ct);

        engine.RaiseEnded();
        engine.RaiseEnded();

        // 放完最后一首：引擎报 Stopped，队列不再往前走。
        engine.RaiseEnded();
        Assert.Equal(PlaybackState.Stopped, engine.State);
        Assert.Equal(3, coordinator.CurrentTrack!.Id);
        Assert.Equal(3, engine.LoadCount);

        await coordinator.PlayAsync(Ct);

        // 整个队列从第一首重来 —— 引擎里没有文件时不能只设 pause=false。
        Assert.Equal(0, coordinator.Queue.CurrentIndex);
        Assert.Equal(1, coordinator.CurrentTrack!.Id);
        Assert.Equal(4, engine.LoadCount);
        Assert.Equal(PlaybackState.Playing, engine.State);
    }

    [Fact]
    public async Task StoppedWithoutExhaustion_PlayReplaysTheCurrentTrack()
    {
        var engine = new FakePlaybackEngine();
        using var coordinator = new PlaybackCoordinator(Api(), engine, new FakePlayHistoryStore());
        await coordinator.PlayFromAsync([Track(1), Track(2), Track(3)], 1, Ct);

        // 试听片段结束：引擎停下，但队列没走到头。
        engine.State = PlaybackState.Stopped;

        await coordinator.PlayAsync(Ct);

        Assert.Equal(1, coordinator.Queue.CurrentIndex);
        Assert.Equal(2, coordinator.CurrentTrack!.Id);
        Assert.Equal(2, engine.LoadCount);
    }

    [Fact]
    public async Task Paused_PlayOnlyResumesWithoutReloading()
    {
        var engine = new FakePlaybackEngine();
        using var coordinator = new PlaybackCoordinator(Api(), engine, new FakePlayHistoryStore());
        await coordinator.PlayFromAsync([Track(1), Track(2)], 0, Ct);

        engine.State = PlaybackState.Paused;

        await coordinator.PlayAsync(Ct);

        // 暂停态只是恢复，不重新解析音源、不回到第一首。
        Assert.Equal(1, engine.LoadCount);
        Assert.Equal(PlaybackState.Playing, engine.State);
        Assert.Equal(1, coordinator.CurrentTrack!.Id);
    }

    [Fact]
    public async Task AfterExhaustion_AFreshPlayCommand_ClearsExhaustion()
    {
        var engine = new FakePlaybackEngine();
        using var coordinator = new PlaybackCoordinator(Api(), engine, new FakePlayHistoryStore());
        await coordinator.PlayFromAsync([Track(1), Track(2), Track(3)], 0, Ct);

        engine.RaiseEnded();
        engine.RaiseEnded();
        engine.RaiseEnded();
        Assert.Equal(3, coordinator.CurrentTrack!.Id);

        // 用户在队列抽屉里点了一首 —— 这是一次新的播放，不该再算「已播到头」。
        await coordinator.PlayQueueItemAsync(1, Ct);
        Assert.Equal(2, coordinator.CurrentTrack!.Id);

        engine.State = PlaybackState.Stopped;
        await coordinator.PlayAsync(Ct);

        Assert.Equal(2, coordinator.CurrentTrack!.Id);
        Assert.Equal(1, coordinator.Queue.CurrentIndex);
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
