using System.Text;
using System.Text.RegularExpressions;
using Bodian.Core.Lyrics;
using Bodian.Core.Tests.Support;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 歌词入口：query 构造、一次 Base64 解码、八进制系数还原。
/// </summary>
/// <remarks>
/// <b>P1 不做 AWLRC 解析与统一歌词模型</b>（那是 P4）。这里只把「一次解码」和
/// 「八进制」两条最容易白干的决策用测试钉死，让 P4 接手时不必重新怀疑它们。
/// </remarks>
public sealed partial class LyricTests
{
    /// <summary>测试脚手架：把 <c>&lt;a,b&gt;</c> 取出来。<b>不是解析器</b>，P4 才写正式的。</summary>
    [GeneratedRegex(@"<(-?\d+),(-?\d+)>")]
    private static partial Regex WordMarker();

    private static (long A, long B)[] Markers(string text)
        => [.. WordMarker().Matches(text).Select(m => (long.Parse(m.Groups[1].Value), long.Parse(m.Groups[2].Value)))];

    // ── 请求构造 ────────────────────────────────────────────────────────────

    [Fact]
    public void Payload_MatchesTheDocumentedForm()
    {
        var payload = BodianLyricPayload.BuildRequestPayload(228908, lrcx: 1);

        Assert.Equal(
            "type=lyric&req=2&lrcx=1&rid=228908&songname=&artist=&corp=kuwo&fromchannel=bodian",
            payload);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Payload_CarriesRequestedLrcx(int lrcx)
        => Assert.Contains($"lrcx={lrcx}", BodianLyricPayload.BuildRequestPayload(228908, lrcx));

    /// <summary>
    /// <c>rid</c> 填的是**波点的 musicId**，不是酷我 rid。
    /// </summary>
    [Fact]
    public void Uri_UsesTheBodianMusicId_AndTheLyricHost()
    {
        var uri = BodianLyricPayload.BuildRequestUri(228908, lrcx: 1);

        Assert.Equal("https", uri.Scheme);
        Assert.Equal("mlyric.kuwo.cn", uri.Host);
        Assert.Equal("/mobi.s", uri.AbsolutePath);
        Assert.StartsWith("?f=bodian&q=", uri.Query);

        // 手工取 q，避免为一行断言引入 System.Web.HttpUtility
        var base64 = Uri.UnescapeDataString(uri.Query["?f=bodian&q=".Length..]);
        var payload = Encoding.UTF8.GetString(Convert.FromBase64String(base64));

        Assert.Contains("rid=228908", payload);
    }

    // ── 一次 Base64 解码 ────────────────────────────────────────────────────

    [Fact]
    public void DecodeContent_RoundTrips()
    {
        const string text = "[00:00.00]晴天 - 周杰伦";
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

        Assert.Equal(text, BodianLyricPayload.DecodeContent(base64));
    }

    /// <summary>
    /// 该曲没有逐字轨时服务端返回**空串**，业务码仍是 200——空串不是错误。
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void DecodeContent_HandlesEmpty(string? input)
        => Assert.Equal(string.Empty, BodianLyricPayload.DecodeContent(input));

    [Fact]
    public void DecodeContent_ToleratesEmbeddedWhitespace()
    {
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("abc"));
        var withNewlines = base64[..4] + "\n" + base64[4..];

        Assert.Equal("abc", BodianLyricPayload.DecodeContent(withNewlines));
    }

    /// <summary>
    /// 只要一次解码。若有人搬来老酷我那套 zlib + XOR，这份 fixture 解出来会是乱码，
    /// 而不是当前这个合法时间戳。
    /// </summary>
    [Fact]
    public void DecodeContent_YieldsPlainLrc_NotDoubleEncoded()
    {
        var text = BodianLyricPayload.DecodeContent(
            Convert.ToBase64String(Encoding.UTF8.GetBytes(Fixtures.Read("lyric-228908-lrcx0.lrc"))));

        Assert.StartsWith("[ti:晴天]", text);
        Assert.Contains("[00:00.00]晴天", text);
    }

    // ── 八进制系数 ──────────────────────────────────────────────────────────

    /// <summary>
    /// **本组最关键的一条。** <c>[kuwo:127]</c> 按八进制是 87，factor 8/7；
    /// 按十进制是 127，factor 12/7 —— 后者会算出非整数、忽快忽慢的时间轴，而且不报错。
    /// </summary>
    [Fact]
    public void FactorTag_IsParsedAsOctal()
    {
        var text = Fixtures.Read("lyric-228908-lrcx1.lrc");

        Assert.StartsWith("[kuwo:127]", text);
        Assert.True(KuwoFactorCodec.TryParseTag(text, out var startFactor, out var durationFactor));

        Assert.Equal(8, startFactor);      // 87 / 10 —— 不是 127 / 10 = 12
        Assert.Equal(7, durationFactor);   // 87 % 10
    }

    [Theory]
    [InlineData("[kuwo:127]\n[00:00.0]x", 8, 7)]    // 八进制 127 = 87 → 8/7
    [InlineData("[kuwo:13]\n[00:00.0]x", 1, 1)]     // 八进制 13 = 11 → 1/1
    [InlineData("[kuwo:20]\n[00:00.0]x", 1, 6)]     // 八进制 20 = 16 → 1/6
    public void FactorTag_TableOfValues(string text, int expectedStart, int expectedDuration)
    {
        Assert.True(KuwoFactorCodec.TryParseTag(text, out var s, out var d));
        Assert.Equal(expectedStart, s);
        Assert.Equal(expectedDuration, d);
    }

    /// <summary>八进制与十进制两种解读会给出不同的系数，而错的那个不报错。</summary>
    [Fact]
    public void FactorTag_OctalDiffersFromDecimal()
    {
        // 用另一套实现交叉验证：转换基数为 8
        var octalValue = Convert.ToInt32("127", 8);
        Assert.Equal(87, octalValue);
        Assert.Equal(8, octalValue / 10);

        Assert.True(KuwoFactorCodec.TryParseTag("[kuwo:127]x", out var start, out var duration));
        Assert.Equal(8, start);
        Assert.NotEqual(127 / 10, start);      // 按十进制解读会得到 12
        Assert.Equal(7, duration);

        // 八进制 11 = 9 → startFactor 0，即无有效逐字轨；按十进制会得到 1/1 并误判为「有逐字」
        Assert.Equal(0, Convert.ToInt32("11", 8) / 10);
        Assert.False(KuwoFactorCodec.TryParseTag("[kuwo:11]x", out _, out _));
    }

    /// <summary>任一条数为 0 就没有有效逐字轨，应当回退行歌词。</summary>
    [Theory]
    [InlineData("[kuwo:10]x")]   // 八进制 8 → 0/8，startFactor 为 0
    [InlineData("[kuwo:0]x")]    // 0 → 0/0
    public void FactorTag_ZeroFactorMeansNoWordTrack(string text)
        => Assert.False(KuwoFactorCodec.TryParseTag(text, out _, out _));

    /// <summary>无标签不是异常边界——逐行版本来就不带它。</summary>
    [Fact]
    public void FactorTag_AbsentForLineByLineLyrics()
    {
        var text = Fixtures.Read("lyric-228908-lrcx0.lrc");

        Assert.False(KuwoFactorCodec.TryParseTag(text, out _, out _));
        Assert.DoesNotContain("[kuwo:", text);
    }

    /// <summary>
    /// 只摘标签本身，**不动周围的换行**——换行属于行结构，由解析器处理，不是编解码器的职责。
    /// </summary>
    [Fact]
    public void StripTag_RemovesOnlyTheTag()
    {
        var stripped = KuwoFactorCodec.StripTag("[kuwo:127]\n[00:00.000]<1120,-1120>晴");

        Assert.Equal("\n[00:00.000]<1120,-1120>晴", stripped);
        Assert.DoesNotContain("[kuwo:", stripped);
    }

    [Fact]
    public void StripTag_LeavesTextWithoutTagUntouched()
    {
        const string text = "[00:00.00]晴天";

        Assert.Equal(text, KuwoFactorCodec.StripTag(text));
    }

    // ── 词时间还原 ──────────────────────────────────────────────────────────

    [Fact]
    public void DecodeWord_MatchesTheFormula()
    {
        // 首词 <1120,-1120>：start = |1120 + -1120| / (2*8) = 0；duration = |1120 - -1120| / (2*7) = 160
        Assert.Equal((0L, 160L), KuwoFactorCodec.DecodeWord(1120, -1120, startFactor: 8, durationFactor: 7));

        // <2400,160>：start = 2560/16 = 160；duration = 2240/14 = 160
        Assert.Equal((160L, 160L), KuwoFactorCodec.DecodeWord(2400, 160, 8, 7));
    }

    /// <summary>
    /// 用真实 fixture 验证「八进制 → 干净 160ms 网格」。
    /// 若按十进制解（factor 12/7），首行会得到 0 / 106 / 213 / 320 这种非整数节奏。
    /// </summary>
    [Fact]
    public void DecodeWord_ProducesACleanGridOnRealFixture()
    {
        var text = Fixtures.Read("lyric-228908-lrcx1.lrc");
        Assert.True(KuwoFactorCodec.TryParseTag(text, out var s, out var d));

        var firstLine = KuwoFactorCodec.StripTag(text)
            .Split('\n')
            .First(line => line.StartsWith("[00:00.000]", StringComparison.Ordinal));

        var decoded = Markers(firstLine)
            .Select(m => KuwoFactorCodec.DecodeWord(m.A, m.B, s, d))
            .ToArray();

        Assert.Equal([0L, 160L, 320L, 480L, 640L], decoded.Take(5).Select(x => x.StartMs));
        Assert.All(decoded, x => Assert.Equal(160L, x.DurationMs));
    }

    // ── 用真实 fixture 守住两版的形状 ───────────────────────────────────────

    /// <summary>逐字版：63 行全部带逐字标记，且**每行首词的 start 都是 0**（时间是相对行首的偏移）。</summary>
    [Fact]
    public void WordByWordFixture_HasRelativeOffsetsOnEveryLine()
    {
        var text = Fixtures.Read("lyric-228908-lrcx1.lrc");
        Assert.True(KuwoFactorCodec.TryParseTag(text, out var s, out var d));

        var lines = KuwoFactorCodec.StripTag(text)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => Markers(line).Length > 0)
            .ToArray();

        Assert.Equal(63, lines.Length);

        foreach (var line in lines)
        {
            var first = Markers(line)[0];
            var (start, _) = KuwoFactorCodec.DecodeWord(first.A, first.B, s, d);
            Assert.Equal(0, start);   // 绝对时间不可能每行都从 0 开始
        }
    }

    /// <summary>逐行版：一个逐字标记都没有，也没有系数标签。</summary>
    [Fact]
    public void LineByLineFixture_HasNoWordMarkers()
    {
        var text = Fixtures.Read("lyric-228908-lrcx0.lrc");

        Assert.Empty(Markers(text));
        Assert.DoesNotContain("[kuwo:", text);
        Assert.Contains("[00:00.00]", text);
    }
}
