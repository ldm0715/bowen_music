using System.Text;
using Bowen.Core.Api;
using Bowen.Core.Models;
using Bowen.Core.Services.Abstractions;
using Bowen.Core.Services.Implementations;
using Bowen.Core.Tests.Support;
using Bowen.WinUI.Playback;
using Xunit;

namespace Bowen.Core.Tests;

/// <summary>
/// 播放统计：music.yl 日志字段、编码与协调器触发点。零真实网络。
/// </summary>
/// <remarks>
/// 格式来自 2026-11-18 对官方 Android 5.9.8 的实抓；2026-11-19 对照实验确认
/// 移动端格式的 music.yl 会被统计采纳。见 <c>docs/play-stats.md</c>。
/// </remarks>
public sealed class PlayTelemetryTests
{
    private const string Uid = "36828743";
    private const string DeviceId = "0123456789abcdef0123456789abcdef";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Track Track(int id, int seconds = 200) => new()
    {
        Id = id,
        Title = $"Track {id}",
        ArtistText = "Artist",
        Duration = TimeSpan.FromSeconds(seconds),
        AvailableQualities = [AudioQuality.Lossless, AudioQuality.High, AudioQuality.Standard],
    };

    private static string Decode(string encoded)
    {
        var bytes = Convert.FromBase64String(encoded);
        var key = Encoding.UTF8.GetBytes("Z9o+Lw@H");

        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] ^= key[i % key.Length];
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static string StripPrefix(string log) => log["2%09<".Length..];

    private static Dictionary<string, string> Fields(string log)
    {
        var body = StripPrefix(log).TrimEnd('>');
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);

        // SRC 在官方日志里出现两次（客户端标识与推荐链），保留第一次即可。
        foreach (var part in body.Split('|'))
        {
            var pair = part.Split(':', 2);
            if (pair.Length == 2)
            {
                fields.TryAdd(pair[0], pair[1]);
            }
        }

        return fields;
    }

    // ── 日志字段 ────────────────────────────────────────────────────────────

    [Fact]
    public void Composer_BuildsOfficialFields()
    {
        var log = PlayLogComposer.Build(
            Track(118989, 252),
            TimeSpan.FromSeconds(224),
            Uid,
            DeviceId,
            DateTimeOffset.Parse("2026-11-18T13:00:48+08:00"));

        Assert.StartsWith("2%09<", log);
        Assert.EndsWith(">", log);

        var fields = Fields(log);
        Assert.Equal("BOD_PLAY_MUSIC", fields["ACT"]);
        Assert.Equal("118989", fields["RID"]);
        Assert.Equal("252", fields["DUR"]);
        Assert.Equal("224", fields["PT"]);
        Assert.Equal("224", fields["PLAY_TIME"]);
        Assert.Equal("36828743", fields["UI"]);
        Assert.Equal(DeviceId, fields["U"]);
        Assert.Equal("2026-11-18 13:00:48", fields["CT"]);
    }

    [Fact]
    public void Encode_IsXorThenBase64()
    {
        var log = PlayLogComposer.Build(
            Track(1, 100), TimeSpan.FromSeconds(50), Uid, DeviceId, DateTimeOffset.UnixEpoch);

        Assert.Equal(log, Decode(PlayLogComposer.Encode(log)));
    }

    // ── 发送 ────────────────────────────────────────────────────────────────

    private static PlayTelemetry CreateService(
        ReplayHandler handler,
        bool authenticated = true,
        string uid = Uid)
    {
        var session = BodianSession.CreateAnonymous();
        if (authenticated) { session.Set(uid, "test-token"); }

        return new PlayTelemetry(
            session,
            new FakeDeviceIdentity(DeviceId),
            FixedTimeProvider.Golden,
            logger: null,
            handler: handler);
    }

    [Fact]
    public async Task ReportCompleted_PostsMusicLog()
    {
        var handler = new ReplayHandler();
        var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.Responder = _ =>
        {
            sent.TrySetResult();
            return ReplayHandler.Json("""{"code":200}""");
        };
        using var telemetry = CreateService(handler);

        telemetry.ReportCompleted(Track(118989, 252));

        await sent.Task.WaitAsync(TimeSpan.FromSeconds(2), Ct);

        var request = handler.LastRequest;
        Assert.Equal("POST", request.Method);
        Assert.Contains("log.kuwo.cn/music.yl?encv=1", request.Url, StringComparison.Ordinal);

        var log = Decode(request.Body!);
        var fields = Fields(log);
        Assert.Equal("252", fields["DUR"]);
        Assert.Equal("252", fields["PLAY_TIME"]);
        Assert.Equal(Uid, fields["UI"]);
    }

    [Fact]
    public async Task ReportSwitched_PostsPlayedPosition()
    {
        var handler = new ReplayHandler();
        var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.Responder = _ =>
        {
            sent.TrySetResult();
            return ReplayHandler.Json("""{"code":200}""");
        };
        using var telemetry = CreateService(handler);

        telemetry.ReportSwitched(Track(1, 200), TimeSpan.FromSeconds(60));

        await sent.Task.WaitAsync(TimeSpan.FromSeconds(2), Ct);

        var fields = Fields(Decode(handler.LastRequest.Body!));
        Assert.Equal("200", fields["DUR"]);
        Assert.Equal("60", fields["PT"]);
        Assert.Equal("60", fields["PLAY_TIME"]);
    }

    [Fact]
    public void Report_WhenAnonymous_SendsNothing()
    {
        var handler = new ReplayHandler();
        using var telemetry = CreateService(handler, authenticated: false);

        telemetry.ReportCompleted(Track(1));
        telemetry.ReportSwitched(Track(1), TimeSpan.FromSeconds(100));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Report_WhenTransportFails_DoesNotThrow()
    {
        var handler = new ReplayHandler();
        var attempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.Responder = _ =>
        {
            attempted.TrySetResult();
            return new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError);
        };
        using var telemetry = CreateService(handler);

        telemetry.ReportCompleted(Track(1));

        await attempted.Task.WaitAsync(TimeSpan.FromSeconds(2), Ct);
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
