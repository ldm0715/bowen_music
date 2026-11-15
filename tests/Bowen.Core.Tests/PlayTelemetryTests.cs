using System.Text.Json;
using Bowen.Core.Api;
using Bowen.Core.Models;
using Bowen.Core.Services.Abstractions;
using Bowen.Core.Services.Implementations;
using Bowen.Core.Tests.Support;
using Bowen.WinUI.Playback;
using Xunit;

namespace Bowen.Core.Tests;

/// <summary>
/// 播放统计上报：报文形状、operate 比例与协调器触发点。零真实网络。
/// </summary>
/// <remarks>
/// 协议见 <c>docs/play-stats.md</c>；2026-11-14 实网确认单位是秒、服务端会去重。
/// </remarks>
public sealed class PlayTelemetryTests
{
    private const string Uid = "36828743";

    // ── API 报文 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddPlayRecord_PostsExpectedShape()
    {
        var handler = new ReplayHandler();
        var session = BodianSession.CreateAnonymous();
        using var transport = new BodianHttpTransport(
            handler, new BodianTransportOptions(), session, new FakeDeviceIdentity(), FixedTimeProvider.Golden);
        var api = new BodianApi(transport, session, new FakeDeviceIdentity());

        session.Set(Uid, "test-token");
        handler.Responder = _ => ReplayHandler.Json("""{"code":200,"msg":"success","data":{}}""");

        await api.AddPlayRecordAsync(118989, durationSeconds: 240, playedSeconds: 300, operate: 0, Ct);

        var request = handler.LastRequest;
        Assert.Equal("POST", request.Method);
        Assert.Contains("service/music/record", request.Url, StringComparison.Ordinal);

        var body = JsonDocument.Parse(request.Body!).RootElement;
        Assert.Equal(0, body.GetProperty("operate").GetInt32());

        var item = body.GetProperty("musicList")[0];
        Assert.Equal(118989, item.GetProperty("id").GetInt64());
        Assert.False(item.GetProperty("src").GetBoolean());
        Assert.Equal(240, item.GetProperty("currentDur").GetInt32());
        Assert.Equal(240, item.GetProperty("currentSpt").GetInt32());
        Assert.Equal("", item.GetProperty("pSrc").GetString());
    }

    [Fact]
    public async Task AddPlayRecord_RequiresLogin()
    {
        var handler = new ReplayHandler();
        var session = BodianSession.CreateAnonymous();
        using var transport = new BodianHttpTransport(
            handler, new BodianTransportOptions(), session, new FakeDeviceIdentity(), FixedTimeProvider.Golden);
        var api = new BodianApi(transport, session, new FakeDeviceIdentity());

        await Assert.ThrowsAsync<BodianNotSignedInException>(
            () => api.AddPlayRecordAsync(1, 100, 50, 0, Ct));
    }

    // ── operate 比例 ────────────────────────────────────────────────────────

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Track Track(int id, int seconds = 200) => new()
    {
        Id = id,
        Title = $"Track {id}",
        Duration = TimeSpan.FromSeconds(seconds),
        AvailableQualities = [AudioQuality.Lossless, AudioQuality.High, AudioQuality.Standard],
    };

    private static (PlayTelemetry Telemetry, List<(int Dur, int Spt, int Op)> Calls) CreateService(
        bool authenticated)
    {
        var session = BodianSession.CreateAnonymous();
        if (authenticated) { session.Set(Uid, "test-token"); }

        var calls = new List<(int, int, int)>();
        var api = new PlaybackApiStub
        {
            AddPlayRecord = (_, dur, spt, op, _) =>
            {
                calls.Add((dur, spt, op));
                return Task.CompletedTask;
            },
        };

        return (new PlayTelemetry(api, session), calls);
    }

    [Theory]
    [InlineData(58, 1)]   // 29%
    [InlineData(60, 5)]   // 30%
    [InlineData(100, 5)]  // 50%
    [InlineData(180, 5)]  // 90%
    [InlineData(182, 0)]  // 91%
    [InlineData(200, 0)]  // 100%
    public void ReportSwitched_MapsRatioToOperate(int playedSeconds, int expected)
    {
        var (telemetry, calls) = CreateService(authenticated: true);

        telemetry.ReportSwitched(Track(1, 200), TimeSpan.FromSeconds(playedSeconds));

        var call = Assert.Single(calls);
        Assert.Equal(200, call.Dur);
        Assert.Equal(playedSeconds, call.Spt);
        Assert.Equal(expected, call.Op);
    }

    [Fact]
    public void ReportCompleted_UsesCompleteAndFullPosition()
    {
        var (telemetry, calls) = CreateService(authenticated: true);

        telemetry.ReportCompleted(Track(1, 240));

        var call = Assert.Single(calls);
        Assert.Equal(240, call.Dur);
        Assert.Equal(240, call.Spt);
        Assert.Equal(0, call.Op);
    }

    [Fact]
    public void Report_WhenAnonymous_SendsNothing()
    {
        var (telemetry, calls) = CreateService(authenticated: false);

        telemetry.ReportCompleted(Track(1));
        telemetry.ReportSwitched(Track(1), TimeSpan.FromSeconds(100));

        Assert.Empty(calls);
    }

    [Fact]
    public void Report_WhenTransportFails_DoesNotThrow()
    {
        var session = BodianSession.CreateAnonymous();
        session.Set(Uid, "test-token");
        var api = new PlaybackApiStub
        {
            AddPlayRecord = (_, _, _, _, _) => Task.FromException(new HttpRequestException("boom")),
        };
        var telemetry = new PlayTelemetry(api, session);

        telemetry.ReportCompleted(Track(1));
    }

    // ── 协调器触发点 ────────────────────────────────────────────────────────

    private static PlaybackApiStub Api() => new()
    {
        Resolve = (track, quality, _) => Task.FromResult<PlaybackResolution>(new PlaybackResolution.Playable(new AudioSource
        {
            Url = new Uri($"https://audio.example.invalid/{track.Id}/{quality}"),
            RequestedQuality = quality, Format = AudioQualityTable.ExpectedFormat(quality),
            BitrateKbps = AudioQualityTable.ExpectedBitrateKbps(quality),
        })),
    };

    private sealed class FakePlayTelemetry : IPlayTelemetry
    {
        public List<long> Completed { get; } = [];
        public List<(long Id, TimeSpan Position)> Switched { get; } = [];

        public void ReportCompleted(Track track) => Completed.Add(track.Id);

        public void ReportSwitched(Track track, TimeSpan position)
            => Switched.Add((track.Id, position));
    }

    [Fact]
    public async Task SwitchingTracks_ReportsOutgoingTrackAndPosition()
    {
        var telemetry = new FakePlayTelemetry();
        var engine = new FakePlaybackEngine();
        using var coordinator = new PlaybackCoordinator(
            Api(), engine, new FakePlayHistoryStore(), telemetry: telemetry);

        await coordinator.PlayFromAsync([Track(1), Track(2)], 0, Ct);
        engine.Position = TimeSpan.FromSeconds(60);

        await coordinator.NextAsync(Ct);

        var report = Assert.Single(telemetry.Switched);
        Assert.Equal(1, report.Id);
        Assert.Equal(TimeSpan.FromSeconds(60), report.Position);
        Assert.Empty(telemetry.Completed);
    }

    [Fact]
    public async Task NaturalEnd_ReportsCompletedOnce_NotSwitch()
    {
        var telemetry = new FakePlayTelemetry();
        var engine = new FakePlaybackEngine();
        using var coordinator = new PlaybackCoordinator(
            Api(), engine, new FakePlayHistoryStore(), telemetry: telemetry);

        await coordinator.PlayFromAsync([Track(1), Track(2)], 0, Ct);

        engine.RaiseEnded();

        // 自动续播是异步的，等它真的切到第二首。
        await WaitUntilAsync(() => coordinator.CurrentTrack?.Id == 2, Ct);

        Assert.Equal(new long[] { 1 }, telemetry.Completed);
        Assert.Empty(telemetry.Switched);
    }

    [Fact]
    public async Task QualitySwitch_DoesNotReport()
    {
        var telemetry = new FakePlayTelemetry();
        var engine = new FakePlaybackEngine();
        using var coordinator = new PlaybackCoordinator(
            Api(), engine, new FakePlayHistoryStore(), telemetry: telemetry);

        await coordinator.PlayFromAsync([Track(1)], 0, Ct);
        engine.Position = TimeSpan.FromSeconds(100);

        await coordinator.SwitchQualityAsync(AudioQuality.High, Ct);

        Assert.Empty(telemetry.Completed);
        Assert.Empty(telemetry.Switched);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) { throw new TimeoutException("等待条件超时"); }
            await Task.Delay(10, cancellationToken);
        }
    }
}
