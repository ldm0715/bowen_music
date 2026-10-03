using Bodian.Core.Api;
using Bodian.Core.Diagnostics;
using Bodian.Core.Models;
using Bodian.Core.Tests.Support;
using Xunit;

namespace Bodian.Core.Tests;

public sealed class AudioQualitySelectionTests
{
    [Theory]
    [InlineData("s", "aac", "48", AudioQuality.Standard)]
    [InlineData("h", "ogg", "100", AudioQuality.Standard)]
    [InlineData("p", "ogg", "192", AudioQuality.High)]
    [InlineData("ff", "flac", "2000", AudioQuality.Lossless)]
    public async Task Request_UsesTrackVariantInsteadOfHardcodedParameters(string level, string format, string bitrate, AudioQuality quality)
    {
        using var handler = new ReplayHandler();
        using var transport = new BodianHttpTransport(handler, new BodianTransportOptions(), BodianSession.CreateAnonymous(),
            new FakeDeviceIdentity(), FixedTimeProvider.Golden);
        var api = new BodianApi(transport, BodianSession.CreateAnonymous(), new FakeDeviceIdentity());
        handler.Responder = request => ReplayHandler.Json(request.RequestUri!.AbsolutePath.Contains("audioUrl")
            ? $$$"""{"code":200,"data":{"audioHttpsUrl":"https://cdn.example.invalid/a","format":"{{{format}}}","bitrate":{{{bitrate}}},"size":"151.79Mb","ekey":"synthetic-only"}}"""
            : """{"code":200,"data":{"status":4}}""");
        var variant = AudioQualityTable.ParseVariant(level, format, bitrate, "151.79Mb")!;
        var track = new Track { Id = 123, Title = "测试", AudioVariants = [variant], AvailableQualities = [quality] };
        var result = Assert.IsType<PlaybackResolution.Playable>(await api.ResolvePlaybackAsync(track, quality, TestContext.Current.CancellationToken));
        Assert.Contains($"br={bitrate}k{format}", handler.LastRequest.Url);
        Assert.Contains($"\"br\":\"{bitrate}k{format}\"", handler.LastRequest.Body!);
        Assert.DoesNotContain("\"format\"", handler.LastRequest.Body!);
        Assert.False(result.Source.WasDowngraded);
        Assert.Equal(AudioQualityTable.ParseSize("151.79Mb"), result.Source.SizeBytes);
    }

    [Theory]
    [InlineData("zp", "zp", "20000", "zpMb")]
    [InlineData("hr", "flac", "4000", "88Mb")]
    [InlineData("dd4", "mp4", "11500", "60Mb")]
    [InlineData("ff", "flac", "2000", "unknown")]
    [InlineData("ff", "flac", "invalid", "52Mb")]
    [InlineData("zply", "mflac", "20900", "151.79Mb")]
    [InlineData("zpga201", "mflac", "20201", "29.72Mb")]
    [InlineData("zpga501", "mflac", "20501", "76.71Mb")]
    [InlineData("zpga714", "mgg", "24000", "34.94Mb")]
    [InlineData("bcms", "mgg", "22000", "88.1Mb")]
    [InlineData("ff", "mflac", "20900", "151.79Mb")]
    [InlineData("p", "mgg", "22000", "88.1Mb")]
    public void InvalidOrUnsupportedVariant_IsFiltered(string level, string format, string bitrate, string size)
        => Assert.Null(AudioQualityTable.ParseVariant(level, format, bitrate, size));

    [Fact]
    public void ProductVariants_PickDeclaredSizeAndBitrate()
    {
        var low = AudioQualityTable.ParseVariant("p", "ogg", "192", "5.59Mb")!;
        var high = AudioQualityTable.ParseVariant("p", "mp3", "320", "10.29Mb")!;
        var lossless = AudioQualityTable.ParseVariant("ff", "flac", "2000", "52.83Mb")!;
        var track = new Track { Id = 1, Title = "test", AudioVariants = [low, high, lossless] };
        Assert.Equal(high, AudioQualityTable.SelectVariant(track, AudioQuality.High));
        Assert.Equal(lossless, AudioQualityTable.SelectVariant(track, AudioQuality.Lossless));
        Assert.Equal("52.83 MB", AudioQualityTable.FormatSize(lossless.SizeBytes));
    }

    [Fact]
    public void Display_UsesOnlyProductNamesSizeAndDowngradeNotice()
    {
        var source = new AudioSource
        {
            Url = new Uri("https://cdn.example.invalid/a"), RequestedQuality = AudioQuality.Lossless,
            RequestedVariant = new(AudioQuality.Lossless, "ff", "flac", 2000),
            Format = "mp3", BitrateKbps = 320, SizeBytes = AudioQualityTable.ParseSize("10.29Mb"),
        };
        var text = AudioQualityTable.Describe(source);
        Assert.Equal("HQ（SQ不可用） · 10.29 MB", text);
        Assert.DoesNotContain("mp3", text);
        Assert.DoesNotContain("320", text);
        Assert.DoesNotContain("synthetic", source.ToString());
        Assert.Equal("SQ", AudioQualityTable.DisplayName(AudioQuality.Lossless));
    }

    [Theory]
    [InlineData("151.79Mb", "151.79 MB")]
    [InlineData("1.5Gb", "1.5 GB")]
    [InlineData("512Kb", "512 KB")]
    [InlineData("bad", "大小未知")]
    public void Size_UsesRealValueOrUnknown(string input, string expected)
        => Assert.Equal(expected, AudioQualityTable.FormatSize(AudioQualityTable.ParseSize(input)));

    [Fact]
    public void Ekey_IsRedactedInQueryJsonAndRecordText()
    {
        Assert.Equal("ekey=<redacted>", LogRedactor.Redact("ekey=synthetic"));
        Assert.Equal("\"eKey\":\"<redacted>\"", LogRedactor.Redact("\"eKey\":\"synthetic\""));
        var source = new AudioSource { Url = new Uri("https://example.invalid/secret"), Format = "flac" };
        Assert.DoesNotContain("synthetic", source.ToString());
        Assert.DoesNotContain("secret", source.ToString());
    }

    [Fact]
    public void History_PreservesVariantMetadataForReplay()
    {
        var variant = AudioQualityTable.ParseVariant("ff", "flac", "2000", "52.83Mb")!;
        var track = new Track { Id = 123, Title = "test", AudioVariants = [variant], AvailableQualities = [AudioQuality.Lossless] };
        var replay = PlayHistoryEntry.From(track, DateTimeOffset.UtcNow).ToTrack();
        Assert.Equal(variant, Assert.Single(replay.AudioVariants));
    }
}
