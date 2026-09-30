using Bodian.Core.Lyrics;
using Bodian.Core.Models.Lyrics;
using Bodian.Core.Tests.Support;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 歌词解析器。两份样本都是真实响应解出来的（<c>fixtures/lyric-228908-lrcx{0,1}.lrc</c>），
/// 零网络。
/// </summary>
/// <remarks>
/// <b>63 这个数字是这份样本的锚点</b>：逐字版与逐行版都是 63 行带时间戳。
/// 解析器要是少了几行，多半是加了什么「看着合理」的过滤规则 —— 见
/// <see cref="KeepsShortCreditLines"/> 那条。
/// </remarks>
public sealed class BodianLyricParserTests
{
    private const int FixtureLineCount = 63;

    private static LyricDocument ParseWordByWord() => BodianLyricParser.Parse(Fixtures.Read("lyric-228908-lrcx1.lrc"));

    private static LyricDocument ParseLineByLine() => BodianLyricParser.Parse(Fixtures.Read("lyric-228908-lrcx0.lrc"));

    [Fact]
    public void WordByWordFixture_ParsesEveryLine()
    {
        var document = ParseWordByWord();

        Assert.Equal(LyricKind.WordByWord, document.Kind);
        Assert.Equal(FixtureLineCount, document.Lines.Count);
    }

    [Fact]
    public void LineByLineFixture_ParsesEveryLine()
    {
        var document = ParseLineByLine();

        Assert.Equal(LyricKind.LineByLine, document.Kind);
        Assert.Equal(FixtureLineCount, document.Lines.Count);
    }

    /// <summary>行首时间轴要与文件里的 <c>[mm:ss.fff]</c> 完全一致。</summary>
    [Fact]
    public void WordByWordFixture_LineStartsMatchTheTimestamps()
    {
        var document = ParseWordByWord();

        Assert.Equal(TimeSpan.Zero, document.Lines[0].Start);
        Assert.Equal(TimeSpan.FromMilliseconds(2250), document.Lines[1].Start);

        // 文件里 [00:29.264] 那行前面有 7 行元数据（[kuwo:] / [ver:] / [ti:] …），它们不该进结果。
        Assert.Equal(TimeSpan.FromMilliseconds(29264), document.Lines[13].Start);
    }

    /// <summary>
    /// 每行第一个音节的起点等于行首。
    /// </summary>
    /// <remarks>
    /// 这条就是「逐字时间是相对行首偏移，不是绝对值」的实证：绝对时间不可能每行都从行首开始。
    /// 时间系数算错（十进制 vs 八进制）时这条必挂。
    /// </remarks>
    [Fact]
    public void WordByWordFixture_FirstSyllableStartsAtLineStart()
    {
        var document = ParseWordByWord();

        Assert.All(document.Lines, line =>
        {
            Assert.NotEmpty(line.Syllables);
            Assert.Equal(line.Start, line.Syllables[0].Start);
        });
    }

    [Fact]
    public void WordByWordFixture_SyllablesSpellOutTheLine()
    {
        var document = ParseWordByWord();

        Assert.All(document.Lines, line =>
            Assert.Equal(line.Text, string.Concat(line.Syllables.Select(s => s.Text))));
    }

    /// <summary>音节时间轴要落在行窗口内，且不为负。</summary>
    [Fact]
    public void WordByWordFixture_SyllableDurationsAreSane()
    {
        var document = ParseWordByWord();

        Assert.All(document.Lines, line =>
            Assert.All(line.Syllables, syllable =>
            {
                Assert.True(syllable.Duration > TimeSpan.Zero, $"{line.Text} 出现零长音节");
                Assert.True(syllable.Start >= line.Start, $"{line.Text} 的音节早于行首");
            }));
    }

    /// <summary>
    /// 短行不能被丢掉。
    /// </summary>
    /// <remarks>
    /// 参考实现（<c>tomakino/LyricProvider</c>）里有一条「长度 &lt; 6 的行跳过」的清理规则，
    /// <c>docs/lyrics-ui.md</c> 把它列进了待实现清单。**实测表明照搬会把真实歌词删掉**：
    /// 这份样本里有 3 行去标签后只有 5 个字（词：周杰伦 / 曲：周杰伦 / 鼓：陈柏州），
    /// 63 行会变成 60 行。所以本项目只跳过「没有时间戳」的行。
    /// <para>
    /// 这条测试是那个决定的守门人：谁再把那条规则加回来，它会立刻挂。
    /// </para>
    /// </remarks>
    [Fact]
    public void KeepsShortCreditLines()
    {
        var document = ParseWordByWord();

        var shortLines = document.Lines.Where(line => line.Text.Length < 6).ToList();

        Assert.Equal(3, shortLines.Count);
        Assert.All(shortLines, line => Assert.Equal(5, line.Text.Length));
    }

    [Fact]
    public void WordByWordFixture_LastLineIsClosedByTrackDuration()
    {
        var start = ParseWordByWord().Lines[^1].Start;

        var withDuration = BodianLyricParser.Parse(
            Fixtures.Read("lyric-228908-lrcx1.lrc"),
            TimeSpan.FromSeconds(269));

        Assert.Equal(TimeSpan.FromSeconds(269) - start, withDuration.Lines[^1].Duration);

        // 不给曲目时长时退回兜底窗口，不能是零。
        Assert.True(ParseWordByWord().Lines[^1].Duration > TimeSpan.Zero);
    }

    [Fact]
    public void LineByLineFixture_HasNoWordMarkers()
    {
        var document = ParseLineByLine();

        Assert.All(document.Lines, line =>
        {
            Assert.DoesNotContain('<', line.Text);

            // 逐行版也要有音节（整行一个），渲染层才不必分两套。
            var syllable = Assert.Single(line.Syllables);
            Assert.Equal(line.Text, syllable.Text);
            Assert.Equal(line.Start, syllable.Start);
            Assert.Equal(line.Duration, syllable.Duration);
        });
    }

    [Fact]
    public void Parse_MetadataAndBlankLinesAreSkipped()
    {
        var document = BodianLyricParser.Parse(
            """
            [kuwo:127]
            [ver:v1.0]
            [ti:测试]
            [ar:某人]
            [by:]

            [00:01.000]<1120,-1120>你<2400,160>好
            [00:03.000]<3150,-3150>再<6750,450>见
            """);

        Assert.Equal(2, document.Lines.Count);
        Assert.Equal(TimeSpan.FromSeconds(1), document.Lines[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(3), document.Lines[1].Start);

        // 「你好」「再见」都是两个音节 —— 系数 [kuwo:127] → 8/7 没有把标记吃掉。
        Assert.All(document.Lines, line => Assert.Equal(2, line.Syllables.Count));
    }

    /// <summary>一行挂多个时间戳时，要展开成多行（老的副歌重复写法）。</summary>
    [Fact]
    public void Parse_GluedTimestampsExpandIntoOneLineEach()
    {
        var document = BodianLyricParser.Parse("[00:01.00][00:05.00]同一句");

        Assert.Equal(2, document.Lines.Count);
        Assert.All(document.Lines, line => Assert.Equal("同一句", line.Text));
        Assert.Equal(TimeSpan.FromSeconds(1), document.Lines[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(5), document.Lines[1].Start);
    }

    /// <summary>小数位按位数定标：两位是厘秒，不是毫秒。</summary>
    [Theory]
    [InlineData("[00:01.5]词", 1500)]
    [InlineData("[00:01.50]词", 1500)]
    [InlineData("[00:01.500]词", 1500)]
    [InlineData("[00:01:50]词", 1500)]
    [InlineData("[01:30.000]词", 90000)]
    public void Parse_TimestampFractionIsScaledByDigitCount(string text, int expectedMs)
    {
        var document = BodianLyricParser.Parse(text);

        Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), Assert.Single(document.Lines).Start);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n  ")]
    [InlineData("[ti:只有元数据]")]
    public void Parse_WithoutLyricLines_IsEmpty(string? input)
    {
        var document = BodianLyricParser.Parse(input);

        Assert.True(document.IsEmpty);
        Assert.Equal(LyricKind.None, document.Kind);
    }

    [Fact]
    public void IndexOfLineAt_FindsTheLineCoveringThePosition()
    {
        // 样本的第一行从 0 秒开始，测不出「还没到第一行」这一档，所以另起一份。
        var document = BodianLyricParser.Parse("[00:01.00]第一行\n[00:03.00]第二行");

        Assert.Equal(-1, document.IndexOfLineAt(TimeSpan.FromMilliseconds(999)));

        // 行首那一刻就算这一行，不是上一行。
        Assert.Equal(0, document.IndexOfLineAt(TimeSpan.FromSeconds(1)));
        Assert.Equal(0, document.IndexOfLineAt(TimeSpan.FromMilliseconds(2999)));
        Assert.Equal(1, document.IndexOfLineAt(TimeSpan.FromSeconds(3)));

        // 曲末之后停在最后一行，不要清空。
        Assert.Equal(1, document.IndexOfLineAt(TimeSpan.FromHours(1)));
    }
}
