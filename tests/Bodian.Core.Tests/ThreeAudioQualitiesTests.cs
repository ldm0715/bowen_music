using Bodian.Core.Api;
using Bodian.Core.Models;
using Bodian.Core.Services.Implementations;
using Bodian.Core.Tests.Support;
using Xunit;

namespace Bodian.Core.Tests;

public sealed class ThreeAudioQualitiesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void ClientAssembly_ExposesOnlyThreeQualitiesAndNoDecryptor()
    {
        Assert.Equal(new[] { AudioQuality.Standard, AudioQuality.High, AudioQuality.Lossless }, Enum.GetValues<AudioQuality>());
        var assembly = typeof(AudioQuality).Assembly;
        Assert.Null(assembly.GetType("Bodian.Core.Playback.EKeyAudioDecryptor"));
        Assert.Null(assembly.GetType("Bodian.Core.Playback.EncryptedAudioSourceResolver"));
        Assert.Null(assembly.GetType("Bodian.Core.Playback.ResearchAudioSource"));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void PreviouslySavedAdvancedPreference_FallsBackToSq(int previous)
    {
        // 每例只写自己的测试文件，不读取或修改实际用户设置。
        var directory = Path.Combine(Path.GetTempPath(), "bodian-three-quality-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "audio-quality.json");
        File.WriteAllText(path, previous.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var store = new JsonAudioQualitySettingsStore(path);
        Assert.Equal(AudioQuality.Lossless, store.Load());
        store.Save(AudioQuality.High);
        Assert.Equal(AudioQuality.High, store.Load());
        Assert.Throws<ArgumentOutOfRangeException>(() => store.Save((AudioQuality)previous));
    }

    [Fact]
    public void OldHistory_FiltersAdvancedQualitiesAndEncryptedVariants()
    {
        var plain = new AudioVariant(AudioQuality.Lossless, "ff", "flac", 2000, 55396352);
        var advanced = new AudioVariant((AudioQuality)4, "zply", "mflac", 20900, 186982072);
        var mislabeled = new AudioVariant(AudioQuality.Lossless, "ff", "mflac", 20900);
        var previous = new PlayHistoryEntry
        {
            MusicId = 123, Title = "test", PlayedAt = DateTimeOffset.UnixEpoch,
            AvailableQualities = [(AudioQuality)4, (AudioQuality)3, AudioQuality.Lossless, AudioQuality.High],
            AudioVariants = [advanced, mislabeled, plain],
        };
        var replay = previous.ToTrack();
        Assert.Equal(new[] { AudioQuality.Lossless, AudioQuality.High }, replay.AvailableQualities);
        Assert.Equal(plain, Assert.Single(replay.AudioVariants));
        Assert.Equal(plain, AudioQualityTable.SelectVariant(replay));

        var rewritten = PlayHistoryEntry.From(new Track
        {
            Id = 123, Title = "test", AvailableQualities = [(AudioQuality)5, AudioQuality.Lossless],
            AudioVariants = [advanced, plain],
        }, DateTimeOffset.UnixEpoch);
        Assert.Equal(AudioQuality.Lossless, Assert.Single(rewritten.AvailableQualities));
        Assert.Equal(plain, Assert.Single(rewritten.AudioVariants));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task AdvancedPreference_IsRejectedBeforeAnyApiCall(int previous)
    {
        using var handler = new ReplayHandler();
        using var transport = new BodianHttpTransport(handler, new BodianTransportOptions(), BodianSession.CreateAnonymous(),
            new FakeDeviceIdentity(), FixedTimeProvider.Golden);
        var api = new BodianApi(transport, BodianSession.CreateAnonymous(), new FakeDeviceIdentity());
        var track = new Track { Id = 123, Title = "test", AvailableQualities = [AudioQuality.Lossless] };
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => api.ResolvePlaybackAsync(track, (AudioQuality)previous, Ct));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task LegacyEncryptedOnlyTrack_IsNotRequested()
    {
        using var handler = new ReplayHandler { Responder = _ => ReplayHandler.Json("""{"code":200,"data":{"status":4}}""") };
        using var transport = new BodianHttpTransport(handler, new BodianTransportOptions(), BodianSession.CreateAnonymous(),
            new FakeDeviceIdentity(), FixedTimeProvider.Golden);
        var api = new BodianApi(transport, BodianSession.CreateAnonymous(), new FakeDeviceIdentity());
        var track = new Track
        {
            Id = 123, Title = "test", AvailableQualities = [(AudioQuality)4],
            AudioVariants = [new((AudioQuality)4, "zply", "mflac", 20900)],
        };
        Assert.False(track.HasPlayableQuality);
        var denied = Assert.IsType<PlaybackResolution.Denied>(await api.ResolvePlaybackAsync(track, Ct));
        Assert.Equal(PlaybackDenialReason.NoUsableQuality, denied.Reason);
        Assert.DoesNotContain(handler.Requests, r => r.Url.Contains("audioUrl"));
    }

    [Theory]
    [InlineData("mflac")]
    [InlineData("mgg")]
    [InlineData("mmp4")]
    [InlineData("mp4")]
    public async Task UnexpectedEncryptedOrLicensedResponse_IsRejected(string format)
    {
        using var handler = new ReplayHandler
        {
            Responder = request => ReplayHandler.Json(request.RequestUri!.AbsolutePath.EndsWith("audioUrl")
                ? $$$"""{"code":200,"data":{"audioHttpsUrl":"https://cdn.example.invalid/audio","format":"{{{format}}}","bitrate":20900}}"""
                : """{"code":200,"data":{"status":4}}"""),
        };
        using var transport = new BodianHttpTransport(handler, new BodianTransportOptions(), BodianSession.CreateAnonymous(),
            new FakeDeviceIdentity(), FixedTimeProvider.Golden);
        var api = new BodianApi(transport, BodianSession.CreateAnonymous(), new FakeDeviceIdentity());
        var track = new Track { Id = 123, Title = "test", AvailableQualities = [AudioQuality.Lossless] };
        var denied = Assert.IsType<PlaybackResolution.Denied>(await api.ResolvePlaybackAsync(track, AudioQuality.Lossless, Ct));
        Assert.Equal(PlaybackDenialReason.NoUsableQuality, denied.Reason);
        Assert.Contains("br=2000kflac", handler.LastRequest.Url);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task TrackDetails_FilterRealAdvancedDeclarations()
    {
        using var handler = new ReplayHandler { Responder = _ => ReplayHandler.Json(Fixtures.Read("music-info-228908.json")) };
        using var transport = new BodianHttpTransport(handler, new BodianTransportOptions(), BodianSession.CreateAnonymous(),
            new FakeDeviceIdentity(), FixedTimeProvider.Golden);
        var api = new BodianApi(transport, BodianSession.CreateAnonymous(), new FakeDeviceIdentity());
        var track = Assert.IsType<Track>(await api.GetTrackAsync(228908, Ct));
        Assert.Equal(new[] { AudioQuality.Lossless, AudioQuality.High, AudioQuality.Standard }, track.AvailableQualities);
        Assert.NotEmpty(track.AudioVariants);
        Assert.All(track.AudioVariants, v => Assert.True(AudioQualityTable.IsSupportedVariant(v)));
        Assert.DoesNotContain(track.AudioVariants, v => v.Level is "zply" or "bcms" || v.Level.StartsWith("zpga"));
    }
}
