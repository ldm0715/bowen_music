using Bodian.Core.Models;
using Bodian.Core.Services.Abstractions;
using Bodian.Core.Tests.Support;
using Bodian.WinUI.Playback;
using Xunit;

namespace Bodian.Core.Tests;

public sealed class PlaybackQualitySwitchTests
{
    private static Track Track(long id = 1) => new()
    {
        Id = id, Title = $"Track {id}",
        AvailableQualities = [AudioQuality.Lossless, AudioQuality.High, AudioQuality.Standard],
    };
    private static PlaybackResolution Playable(Track track, AudioQuality quality) => new PlaybackResolution.Playable(new AudioSource
    {
        Url = new Uri($"https://audio.example.invalid/{track.Id}/{quality}"),
        RequestedQuality = quality, Format = AudioQualityTable.ExpectedFormat(quality),
        BitrateKbps = AudioQualityTable.ExpectedBitrateKbps(quality),
    });
    private static PlaybackApiStub Api() => new() { Resolve = (track, q, _) => Task.FromResult(Playable(track, q)) };
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Switch_PreservesPositionPauseQueueAndDoesNotReloadMetadataOrHistory(bool paused)
    {
        var api = Api(); var engine = new Engine(); var history = new History(); var settings = new Settings();
        using var coordinator = new PlaybackCoordinator(api, engine, history, qualitySettings: settings);
        var started = 0; var changed = 0;
        coordinator.Started += (_, _) => started++;
        coordinator.QualityChanged += (_, _) => changed++;
        await coordinator.PlayFromAsync([Track(), Track(2)], 0, Ct);
        engine.Position = TimeSpan.FromSeconds(93.5);
        engine.State = paused ? PlaybackState.Paused : PlaybackState.Playing;
        await coordinator.SwitchQualityAsync(AudioQuality.High, Ct);
        Assert.Equal(TimeSpan.FromSeconds(93.5), engine.Last!.Start);
        Assert.Equal(paused, engine.Last.InitiallyPaused);
        Assert.Equal(0, coordinator.Queue.CurrentIndex);
        Assert.Equal(1, coordinator.CurrentTrack!.Id);
        Assert.Equal(1, started);
        Assert.Equal(1, changed);
        Assert.Equal(1, history.Count);
        Assert.Equal(AudioQuality.High, coordinator.PreferredQuality);
        Assert.Equal(AudioQuality.High, settings.Quality);
        Assert.False(coordinator.IsChangingQuality);
    }

    [Fact]
    public async Task UnsupportedServerSource_PreservesOldStreamAndPreference()
    {
        var engine = new Engine(); var settings = new Settings(); var api = Api();
        using var coordinator = new PlaybackCoordinator(api, engine, new History(), qualitySettings: settings);
        await coordinator.PlayFromAsync([Track()], 0, Ct);
        var original = coordinator.CurrentSource;
        var failed = 0;
        coordinator.QualityChangeFailed += (_, _) => failed++;
        api.Resolve = (track, quality, _) => Task.FromResult<PlaybackResolution>(new PlaybackResolution.Playable(new AudioSource
        {
            Url = new Uri("https://audio.example.invalid/encrypted"), RequestedQuality = quality,
            Format = "mflac", BitrateKbps = 20900,
        }));
        await coordinator.SwitchQualityAsync(AudioQuality.High, Ct);
        Assert.Same(original, coordinator.CurrentSource);
        Assert.Equal(1, engine.LoadCount);
        Assert.Equal(1, failed);
        Assert.Equal(AudioQuality.Lossless, coordinator.PreferredQuality);
    }

    [Fact]
    public async Task FailedEngineLoad_RestoresOldStreamAtCurrentPosition()
    {
        var api = Api(); var engine = new Engine();
        using var coordinator = new PlaybackCoordinator(api, engine, new History());
        await coordinator.PlayFromAsync([Track()], 0, Ct);
        engine.Position = TimeSpan.FromSeconds(42);
        var original = coordinator.CurrentSource;
        engine.FailOnce = true;
        await coordinator.SwitchQualityAsync(AudioQuality.High, Ct);
        Assert.Equal(3, engine.LoadCount);
        Assert.Equal(original!.Url, engine.Last!.StreamUrl);
        Assert.Equal(TimeSpan.FromSeconds(42), engine.Last.Start);
        Assert.Same(original, coordinator.CurrentSource);
    }

    [Fact]
    public async Task LateQualityResponse_CannotReplaceNextTrack()
    {
        var api = Api(); var engine = new Engine();
        using var coordinator = new PlaybackCoordinator(api, engine, new History());
        await coordinator.PlayFromAsync([Track(), Track(2)], 0, Ct);
        var delayed = new TaskCompletionSource<PlaybackResolution>(TaskCreationOptions.RunContinuationsAsynchronously);
        api.Resolve = (track, q, _) => track.Id == 1 && q == AudioQuality.High
            ? delayed.Task : Task.FromResult(Playable(track, q));
        var switching = coordinator.SwitchQualityAsync(AudioQuality.High, Ct);
        await coordinator.NextAsync(Ct);
        delayed.SetResult(Playable(Track(), AudioQuality.High));
        await switching;
        Assert.Equal(2, coordinator.CurrentTrack!.Id);
        Assert.Contains("/2/", engine.Last!.StreamUrl.AbsolutePath);
        Assert.Equal(2, engine.LoadCount);
        Assert.False(coordinator.IsChangingQuality);
    }

    [Fact]
    public async Task ConsecutiveSwitches_OnlyLatestResponseTakesEffect()
    {
        var api = Api(); var engine = new Engine();
        using var coordinator = new PlaybackCoordinator(api, engine, new History());
        await coordinator.PlayFromAsync([Track()], 0, Ct);
        var delayed = new TaskCompletionSource<PlaybackResolution>(TaskCreationOptions.RunContinuationsAsynchronously);
        api.Resolve = (track, q, _) => q == AudioQuality.High ? delayed.Task : Task.FromResult(Playable(track, q));
        var first = coordinator.SwitchQualityAsync(AudioQuality.High, Ct);
        await coordinator.SwitchQualityAsync(AudioQuality.Standard, Ct);
        delayed.SetResult(Playable(Track(), AudioQuality.High));
        await first;
        Assert.Equal(AudioQuality.Standard, coordinator.CurrentSource!.RequestedQuality);
        Assert.Equal(2, engine.LoadCount);
    }

    [Fact]
    public async Task Audition_DoesNotMakeAQualityRequest()
    {
        var api = Api(); var engine = new Engine();
        api.Resolve = (track, _, _) => Task.FromResult<PlaybackResolution>(new PlaybackResolution.AuditionOnly(
            new AudioSource { Url = new Uri("https://audio.example.invalid/audition"), Format = "mp3" },
            TimeSpan.Zero, TimeSpan.FromSeconds(29)));
        using var coordinator = new PlaybackCoordinator(api, engine, new History());
        await coordinator.PlayFromAsync([Track()], 0, Ct);
        api.Resolve = (_, _, _) => throw new InvalidOperationException("Unexpected request");
        await coordinator.SwitchQualityAsync(AudioQuality.High, Ct);
        Assert.Equal(1, engine.LoadCount);
        Assert.True(coordinator.CurrentPolicy!.IsAudition);
    }

    [Fact]
    public async Task NewTracks_UseStoredPreference()
    {
        var api = Api(); var engine = new Engine(); var settings = new Settings { Quality = AudioQuality.High };
        using var coordinator = new PlaybackCoordinator(api, engine, new History(), qualitySettings: settings);
        await coordinator.PlayFromAsync([Track()], 0, Ct);
        Assert.Equal(AudioQuality.High, coordinator.CurrentSource!.RequestedQuality);
    }

    [Fact]
    public async Task ExternalCancellation_IsPropagatedAndLeavesOriginalPlayback()
    {
        var api = Api(); var engine = new Engine();
        using var coordinator = new PlaybackCoordinator(api, engine, new History());
        await coordinator.PlayFromAsync([Track()], 0, Ct);
        api.Resolve = async (_, _, ct) => { await Task.Delay(Timeout.InfiniteTimeSpan, ct); throw new InvalidOperationException(); };
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var switching = coordinator.SwitchQualityAsync(AudioQuality.High, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => switching);
        Assert.False(coordinator.IsChangingQuality);
        Assert.Equal(1, engine.LoadCount);
    }

    private sealed class Settings : IAudioQualitySettingsStore
    {
        public AudioQuality Quality { get; set; } = AudioQuality.Lossless;
        public AudioQuality Load() => Quality;
        public void Save(AudioQuality quality) => Quality = quality;
    }
    private sealed class History : IPlayHistoryStore
    {
        public int Count { get; private set; }
        public Task RecordAsync(Track track, CancellationToken cancellationToken = default) { Count++; return Task.CompletedTask; }
        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<PlayHistoryEntry>> GetRecentAsync(int limit, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<PlayHistoryEntry>>([]);
    }
    private sealed class Engine : IPlaybackService
    {
        public bool IsAvailable => true;
        public string? UnavailableReason => null;
        public PlaybackState State { get; set; }
        public TimeSpan Position { get; set; }
        public TimeSpan Duration => TimeSpan.FromSeconds(300);
        public double Volume => 100;
        public PlaybackSource? Last { get; private set; }
        public int LoadCount { get; private set; }
        public bool FailOnce { get; set; }
        public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged { add { } remove { } }
        public event EventHandler<PlaybackPositionChangedEventArgs>? PositionChanged { add { } remove { } }
        public event EventHandler? Ended { add { } remove { } }
        public event EventHandler<PlaybackFailedEventArgs>? Failed { add { } remove { } }
        public Task LoadAsync(PlaybackSource source, CancellationToken cancellationToken = default)
        {
            LoadCount++;
            if (FailOnce) { FailOnce = false; throw new IOException("Synthetic load failure"); }
            Last = source; Position = source.Start ?? TimeSpan.Zero;
            State = source.InitiallyPaused ? PlaybackState.Paused : PlaybackState.Playing;
            return Task.CompletedTask;
        }
        public Task PauseAsync(CancellationToken cancellationToken = default) { State = PlaybackState.Paused; return Task.CompletedTask; }
        public Task PlayAsync(CancellationToken cancellationToken = default) { State = PlaybackState.Playing; return Task.CompletedTask; }
        public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default) { Position = position; return Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellationToken = default) { State = PlaybackState.Stopped; return Task.CompletedTask; }
        public void SetVolume(double volume) { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
