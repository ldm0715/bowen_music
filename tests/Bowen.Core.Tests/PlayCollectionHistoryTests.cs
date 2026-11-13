using Bowen.Core.Models;
using Bowen.Core.Services.Abstractions;
using Bowen.Core.Tests.Support;
using Bowen.WinUI.Playback;
using Xunit;

namespace Bowen.Core.Tests;

/// <summary>
/// 播放协调器上的集合历史上报：只在歌单 / 专辑上下文变化时发一次。
/// </summary>
public sealed class PlayCollectionHistoryTests
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
    public async Task SameCollection_ReportsOnlyOnce()
    {
        var sync = new FakePlayHistorySync();
        var engine = new FakePlaybackEngine();
        using var coordinator = new PlaybackCoordinator(
            Api(), engine, new FakePlayHistoryStore(), historySync: sync);

        var origin = new PlaybackOrigin(PlaybackCollectionKind.Playlist, 42, 5);

        await coordinator.PlayFromAsync([Track(1), Track(2)], 0, origin, Ct);
        await coordinator.EnqueueAndPlayAsync(Track(2), origin, Ct);

        var report = Assert.Single(sync.Collections);
        Assert.Equal(2, report.SourceType);
        Assert.Equal(42, report.SourceId);
        Assert.Equal(5, report.PlaylistType);
    }

    [Fact]
    public async Task DifferentCollection_ReportsAgain_AndNullResets()
    {
        var sync = new FakePlayHistorySync();
        var engine = new FakePlaybackEngine();
        using var coordinator = new PlaybackCoordinator(
            Api(), engine, new FakePlayHistoryStore(), historySync: sync);

        var playlist = new PlaybackOrigin(PlaybackCollectionKind.Playlist, 42, 5);
        var album = new PlaybackOrigin(PlaybackCollectionKind.Album, 77, 6);

        await coordinator.PlayFromAsync([Track(1)], 0, playlist, Ct);
        await coordinator.PlayFromAsync([Track(2)], 0, album, Ct);

        // 没有集合上下文的入口（搜索、排行榜等）要清掉上一次的集合，
        // 否则再回歌单时会被去重逻辑误判成「同一个集合」。
        await coordinator.PlayFromAsync([Track(3)], 0, cancellationToken: Ct);
        await coordinator.PlayFromAsync([Track(4)], 0, playlist, Ct);

        Assert.Equal(3, sync.Collections.Count);
        Assert.Equal((3, 77L, 6), sync.Collections[1]);
        Assert.Equal((2, 42L, 5), sync.Collections[2]);
    }

    private sealed class FakePlayHistorySync : IPlayHistorySync
    {
        public List<(int SourceType, long SourceId, int PlaylistType)> Collections { get; } = [];

        public void NotifyPlayed(long musicId)
        {
        }

        public void NotifyCollectionPlayed(int historySourceType, long sourceId, int playlistType)
            => Collections.Add((historySourceType, sourceId, playlistType));
    }
}
