using Bowen.Core.Models;
using Bowen.Core.Tests.Support;
using Bowen.WinUI.Playback;
using Xunit;

namespace Bowen.Core.Tests;

/// <summary>
/// 下一首的预解析。
/// </summary>
/// <remarks>
/// 它省的是切歌时串行的那两跳 API 请求，所以验两件事：<b>下一首提前解析了</b>，
/// 以及<b>预取失败不能把一次正常的切歌变成「不能播放」</b>。
/// </remarks>
public sealed class PlaybackPrefetchTests
{
    private static Track Track(long id) => new()
    {
        Id = id, Title = $"Track {id}",
        AvailableQualities = [AudioQuality.Lossless, AudioQuality.High, AudioQuality.Standard],
    };

    private static PlaybackResolution Playable(Track track, AudioQuality quality) => new PlaybackResolution.Playable(
        new AudioSource
        {
            Url = new Uri($"https://audio.example.invalid/{track.Id}/{quality}"),
            RequestedQuality = quality, Format = AudioQualityTable.ExpectedFormat(quality),
            BitrateKbps = AudioQualityTable.ExpectedBitrateKbps(quality),
        });

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task NextTrack_IsResolvedWhileTheCurrentOnePlays()
    {
        var resolves = new List<long>();
        var api = new PlaybackApiStub
        {
            Resolve = (track, quality, _) =>
            {
                resolves.Add(track.Id);
                return Task.FromResult(Playable(track, quality));
            },
        };
        var engine = new FakePlaybackEngine();
        using var coordinator = new PlaybackCoordinator(api, engine, new FakePlayHistoryStore());

        await coordinator.PlayFromAsync([Track(1), Track(2)], 0, Ct);

        Assert.Equal([1L, 2L], resolves);

        await coordinator.NextAsync(Ct);

        // 切到第 2 首时用的是预取结果，没有再发一次请求。
        Assert.Equal([1L, 2L], resolves);
        Assert.Equal(2, coordinator.CurrentTrack!.Id);
        Assert.Equal(2, engine.LoadCount);
    }

    [Fact]
    public async Task PrefetchedSource_ThatFailsToLoad_IsResolvedAgain()
    {
        var resolves = 0;
        var api = new PlaybackApiStub
        {
            Resolve = (track, quality, _) =>
            {
                resolves++;
                return Task.FromResult(Playable(track, quality));
            },
        };
        var engine = new FakePlaybackEngine();
        using var coordinator = new PlaybackCoordinator(api, engine, new FakePlayHistoryStore());

        await coordinator.PlayFromAsync([Track(1), Track(2)], 0, Ct);
        var afterStart = resolves;

        // 模拟预取的直链已经过期：第一次加载抛错，协调器应当重新解析后重试一次。
        engine.FailOnce = true;
        await coordinator.NextAsync(Ct);

        Assert.Equal(afterStart + 1, resolves);
        Assert.Equal(2, coordinator.CurrentTrack!.Id);
        Assert.Equal(3, engine.LoadCount);
    }

    [Fact]
    public async Task LastTrack_IsNotFetchedAhead()
    {
        var resolves = 0;
        var api = new PlaybackApiStub
        {
            Resolve = (track, quality, _) =>
            {
                resolves++;
                return Task.FromResult(Playable(track, quality));
            },
        };
        var engine = new FakePlaybackEngine();
        using var coordinator = new PlaybackCoordinator(api, engine, new FakePlayHistoryStore());

        await coordinator.PlayFromAsync([Track(1)], 0, Ct);

        Assert.Equal(1, resolves);
    }

    [Fact]
    public async Task FailingPrefetch_DoesNotBlockPlayback()
    {
        var api = new PlaybackApiStub
        {
            Resolve = (track, quality, _) => track.Id == 1
                ? Task.FromResult(Playable(track, quality))
                : Task.FromException<PlaybackResolution>(new HttpRequestException("synthetic prefetch failure")),
        };
        var engine = new FakePlaybackEngine();
        using var coordinator = new PlaybackCoordinator(api, engine, new FakePlayHistoryStore());
        var blocked = 0;
        coordinator.Blocked += (_, _) => blocked++;

        await coordinator.PlayFromAsync([Track(1), Track(2)], 0, Ct);

        // 预取失败只是少了一份缓存，不应该动到正在播的那首。
        Assert.Equal(1, coordinator.CurrentTrack!.Id);
        Assert.Equal(1, engine.LoadCount);
        Assert.Equal(0, blocked);
    }
}
