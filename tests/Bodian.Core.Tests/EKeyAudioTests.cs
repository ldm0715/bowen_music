using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Bodian.Core.Models;
using Bodian.Core.Playback;
using Bodian.Core.Tests.Support;
using Xunit;

namespace Bodian.Core.Tests;

public sealed class EKeyAudioTests
{
    private static string Vector(string name)
    {
        using var doc = JsonDocument.Parse(Fixtures.Read("ekey-synthetic.json"));
        return doc.RootElement.GetProperty(name).GetString()!;
    }

    [Fact]
    public void Tea_IndependentKnownCiphertext()
    {
        var cipher = Convert.FromHexString("91095162e3f5b6dc6b414b50d1a5b84ec50d0c1b1196fd3c");
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, EKeyAudioDecryptor.DecryptTea(cipher, "12345678ABCDEFGH"u8));
        cipher[^1] ^= 0xff;
        Assert.Throws<InvalidDataException>(() => EKeyAudioDecryptor.DecryptTea(cipher, "12345678ABCDEFGH"u8));
    }

    [Theory]
    [InlineData("ekey")]
    [InlineData("ekeyV2")]
    public void Rc4_MatchesIndependentPythonVectorsAtHeaderAndSegmentBoundaries(string name)
    {
        using var decoder = EKeyAudioDecryptor.Create(Vector(name));
        using var doc = JsonDocument.Parse(Fixtures.Read("ekey-synthetic.json"));
        foreach (var vector in doc.RootElement.GetProperty("rc4Zeros").EnumerateObject())
        {
            var expected = Convert.FromHexString(vector.Value.GetString()!);
            var actual = new byte[expected.Length];
            decoder.Decrypt(actual, long.Parse(vector.Name));
            Assert.Equal(expected, actual);
        }
    }

    [Theory]
    [InlineData(0, "3f8ac1493f49c18a3f8ac1493f49c18a")]
    [InlineData(32759, "8a3f8ac1493f49c18a8ac1493f49c18a")]
    public void Map_MatchesIndependentVectors(long offset, string expected)
    {
        using var decoder = EKeyAudioDecryptor.Create(Vector("mapEkey"));
        var data = new byte[16];
        decoder.Decrypt(data, offset);
        Assert.Equal(Convert.FromHexString(expected), data);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid-key")]
    [InlineData("AQID")]
    public void InvalidEkey_IsRejectedWithoutLeakingKey(string? key)
        => Assert.Throws<InvalidDataException>(() => EKeyAudioDecryptor.Create(key));

    [Fact]
    public async Task Proxy_DecryptsRangesAndRetiresOldSession()
    {
        var encrypted = File.ReadAllBytes(Fixtures.PathOf("ekey-synthetic.bin"));
        using var upstreamHandler = new RangeHandler(encrypted);
        using var upstream = new HttpClient(upstreamHandler);
        using var resolver = new EncryptedAudioSourceResolver(upstream);
        using var prepared = await resolver.PrepareAsync(new ResearchAudioSource
        {
            Url = new Uri("https://cdn.example.invalid/audio"), Format = "mflac", EKey = Vector("ekey"),
        }, TestContext.Current.CancellationToken);
        using var downstream = new HttpClient(new SocketsHttpHandler { UseProxy = false });
        foreach (var (start, end) in new[] { (0, 100), (112, 160), (5100, 5180), (320000, 350000), (599900, 599999) })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, prepared.Url);
            request.Headers.Range = new RangeHeaderValue(start, end);
            using var response = await downstream.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
            Assert.Equal(start, response.Content.Headers.ContentRange!.From);
            Assert.Equal(end, response.Content.Headers.ContentRange.To);
            var data = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
            Assert.Equal(end - start + 1, data.Length);
            for (var i = 0; i < data.Length; i++)
            {
                var pos = start + i;
                var expected = pos < 4 ? "fLaC"u8[pos] : (byte)((pos - 4) * 17 + 3);
                Assert.Equal(expected, data[i]);
            }
        }
        Assert.Equal(600000, prepared.SizeBytes);
        Assert.All(upstreamHandler.Ranges, range => Assert.Equal(0, range.Start % EKeyAudioDecryptor.SegmentSize));
        Assert.All(upstreamHandler.Ranges, range => Assert.True(range.End - range.Start + 1 <= 327680));
        using var head = await downstream.SendAsync(new HttpRequestMessage(HttpMethod.Head, prepared.Url), TestContext.Current.CancellationToken);
        Assert.Equal(600000, head.Content.Headers.ContentLength);
        using var bad = new HttpRequestMessage(HttpMethod.Get, prepared.Url);
        bad.Headers.Range = new RangeHeaderValue(600000, null);
        using var rejected = await downstream.SendAsync(bad, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, rejected.StatusCode);
        prepared.Dispose();
        using var retired = await downstream.GetAsync(prepared.Url, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, retired.StatusCode);
    }

    [Fact]
    public async Task Proxy_RejectsWrongKeyBeforeReplacingPlayback()
    {
        using var handler = new RangeHandler(File.ReadAllBytes(Fixtures.PathOf("ekey-synthetic.bin")));
        using var http = new HttpClient(handler);
        using var resolver = new EncryptedAudioSourceResolver(http);
        await Assert.ThrowsAsync<InvalidDataException>(() => resolver.PrepareAsync(new ResearchAudioSource
        {
            Url = new Uri("https://cdn.example.invalid/audio"), Format = "mflac", EKey = Vector("mapEkey"),
        }, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("bytes=0-9", true, 0, 9)]
    [InlineData("bytes=10-", true, 10, 99)]
    [InlineData("bytes=-10", true, 90, 99)]
    [InlineData("bytes=50-200", true, 50, 99)]
    [InlineData("bytes=100-", false, 0, 0)]
    [InlineData("bytes=8-2", false, 0, 0)]
    [InlineData("bytes=0-1,3-4", false, 0, 0)]
    public void Range_UsesStandardHttpSemantics(string range, bool valid, long start, long end)
    {
        Assert.Equal(valid, EncryptedAudioSourceResolver.TryRange(range, 100, out var actualStart, out var actualEnd));
        if (valid) { Assert.Equal(start, actualStart); Assert.Equal(end, actualEnd); }
    }

    private sealed class RangeHandler(byte[] encrypted) : HttpMessageHandler
    {
        public List<(long Start, long End)> Ranges { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var range = Assert.Single(request.Headers.Range!.Ranges);
            var start = range.From!.Value;
            var end = Math.Min(range.To!.Value, encrypted.Length - 1);
            Ranges.Add((start, end));
            var content = new ByteArrayContent(encrypted, (int)start, (int)(end - start + 1));
            content.Headers.ContentRange = new ContentRangeHeaderValue(start, end, encrypted.Length);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content });
        }
    }
}
