using Bodian.Core.Models;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 音质选档。纯函数测试，另有一条用真实 fixture 的映射验证在 <see cref="BodianApiTests"/>。
/// </summary>
public sealed class AudioQualityTests
{
    // ── 请求链 ──────────────────────────────────────────────────────────────

    /// <summary>s 与 h 属于标准档，必须去重——不去重会对同一档发两次请求。</summary>
    [Fact]
    public void BuildRequestChain_DeduplicatesStandardLevels()
    {
        var chain = AudioQualityTable.BuildRequestChain(["ff", "p", "h", "s"]);

        Assert.Equal(
            new[] { AudioQuality.Lossless, AudioQuality.High, AudioQuality.Standard },
            chain);
    }

    [Fact]
    public void BuildRequestChain_IsOrderedHighToLow()
    {
        var chain = AudioQualityTable.BuildRequestChain(["s", "ff", "h"]);

        Assert.Equal(new[] { AudioQuality.Lossless, AudioQuality.Standard }, chain);
    }

    [Fact]
    public void BuildRequestChain_SingleLossless()
    {
        Assert.Equal(new[] { AudioQuality.Lossless }, AudioQualityTable.BuildRequestChain(["ff"]));
    }

    /// <summary>空交集 = 这首歌本客户端播不了。</summary>
    [Fact]
    public void BuildRequestChain_EmptyLevels_YieldsEmptyChain()
    {
        Assert.Empty(AudioQualityTable.BuildRequestChain([]));
    }

    /// <summary>只支持基础三档：剔除所有加密、占位和授权解码档位。</summary>
    [Fact]
    public void BuildRequestChain_ExcludesPlaceholderAndLicensedLevels()
    {
        Assert.Empty(AudioQualityTable.BuildRequestChain(["zp", "bcms", "zply", "zpga201", "zpga501", "zpga714", "ac4", "dd4", "dtsx", "hr"]));
    }

    /// <summary>不认识的档位被丢弃，认识的照常保留。</summary>
    [Fact]
    public void BuildRequestChain_IgnoresUnknownLevels()
    {
        Assert.Equal(
            new[] { AudioQuality.Lossless, AudioQuality.Standard },
            AudioQualityTable.BuildRequestChain(["zp", "ff", "bcms", "s"]));
    }

    [Fact]
    public void TryParseLevel_MapsHToStandardAndPToHigh()
    {
        Assert.True(AudioQualityTable.TryParseLevel("h", out var fromH));
        Assert.True(AudioQualityTable.TryParseLevel("p", out var fromP));

        Assert.Equal(AudioQuality.Standard, fromH);
        Assert.Equal(AudioQuality.High, fromP);
    }

    [Fact]
    public void TryParseLevel_RejectsUnknownAndNull()
    {
        Assert.False(AudioQualityTable.TryParseLevel("zp", out _));
        Assert.False(AudioQualityTable.TryParseLevel(null, out _));
        Assert.False(AudioQualityTable.TryParseLevel("FF", out _));   // 大小写敏感
    }

    // ── 请求参数表 ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(AudioQuality.Standard, "128kmp3", "mp3", 128)]
    [InlineData(AudioQuality.High, "320kmp3", "mp3", 320)]
    [InlineData(AudioQuality.Lossless, "2000kflac", "flac", 2000)]
    public void Table_MapsEachQuality(AudioQuality quality, string br, string format, int bitrate)
    {
        Assert.Equal(br, AudioQualityTable.RequestBitrate(quality));
        Assert.Equal(format, AudioQualityTable.ExpectedFormat(quality));
        Assert.Equal(bitrate, AudioQualityTable.ExpectedBitrateKbps(quality));
    }

    // ── 降级判定 ────────────────────────────────────────────────────────────

    /// <summary>实测场景：请求无损，服务端在 code 200 的前提下只给了 320k mp3。</summary>
    [Fact]
    public void MatchesServed_DetectsTheRealDowngrade()
    {
        Assert.False(AudioQualityTable.MatchesServed(AudioQuality.Lossless, "mp3", 320));
    }

    [Fact]
    public void MatchesServed_AcceptsHonoredRequest()
    {
        Assert.True(AudioQualityTable.MatchesServed(AudioQuality.Lossless, "flac", 2000));
        Assert.True(AudioQualityTable.MatchesServed(AudioQuality.High, "mp3", 320));
        Assert.True(AudioQualityTable.MatchesServed(AudioQuality.Standard, "mp3", 128));
    }

    /// <summary>格式比较忽略大小写：服务端给 <c>FLAC</c> 也算作兑现。</summary>
    [Fact]
    public void MatchesServed_FormatComparisonIsCaseInsensitive()
    {
        Assert.True(AudioQualityTable.MatchesServed(AudioQuality.Lossless, "FLAC", 2000));
    }

    /// <summary>服务端没给 bitrate 时只按格式判定，不能把有效音源误判成降级。</summary>
    [Fact]
    public void MatchesServed_ToleratesMissingBitrate()
    {
        Assert.True(AudioQualityTable.MatchesServed(AudioQuality.High, "mp3", 0));
        Assert.False(AudioQualityTable.MatchesServed(AudioQuality.High, "aac", 0));
    }

    [Fact]
    public void MatchesServed_FormatMissing_CountsAsDowngraded()
    {
        Assert.False(AudioQualityTable.MatchesServed(AudioQuality.Lossless, null, 2000));
        Assert.False(AudioQualityTable.MatchesServed(AudioQuality.Lossless, "", 2000));
    }

    /// <summary>码率明显低于期望也算降级，即使格式对得上。</summary>
    [Fact]
    public void MatchesServed_DetectsBitrateShortfall()
    {
        Assert.False(AudioQualityTable.MatchesServed(AudioQuality.High, "mp3", 128));
    }

    // ── AudioSource 的派生属性 ──────────────────────────────────────────────

    [Fact]
    public void AudioSource_WasDowngraded_FollowsRequestedQuality()
    {
        var downgraded = new AudioSource
        {
            Url = new Uri("https://example.invalid/a.flac"),
            RequestedQuality = AudioQuality.Lossless,
            Format = "mp3",
            BitrateKbps = 320,
        };

        Assert.True(downgraded.WasDowngraded);

        var honored = downgraded with { Format = "flac", BitrateKbps = 2000 };

        Assert.False(honored.WasDowngraded);
    }

    /// <summary>试听片段不是按档位选的，没有「降级」这回事。</summary>
    [Fact]
    public void AudioSource_AuditionIsNeverDowngraded()
    {
        var audition = new AudioSource
        {
            Url = new Uri("https://example.invalid/a.mp3"),
            RequestedQuality = null,
            Format = "mp3",
            BitrateKbps = 128,
        };

        Assert.False(audition.WasDowngraded);
    }
}
