using Bodian.Core.Lyrics;
using Bodian.Core.Models.Lyrics;
using Bodian.Core.Tests.Support;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 音节级时间轴。手搓的小文档负责钉死边界，真实 fixture 负责一次覆盖全部音节。
/// </summary>
/// <remarks>
/// 这里守的是「渲染层可以无条件相信 <see cref="LyricTimeline.Locate"/>」这条契约 ——
/// 尤其是 seek 之后不能残留上一帧高亮，靠的就是它是纯函数。
/// </remarks>
public sealed class LyricTimelineTests
{
    // ── 曲头 / 行首 ─────────────────────────────────────────────────────────

    /// <summary>第一行之前是曲头空白段，界面据此不高亮任何一行。</summary>
    [Fact]
    public void BeforeFirstLine_ReportsNoLine()
    {
        var document = TwoLines();

        var cursor = document.Locate(TimeSpan.FromMilliseconds(500));

        Assert.Equal(LyricCursor.None, cursor);
        Assert.False(cursor.HasLine);
        Assert.False(cursor.HasSyllable);
    }

    /// <summary>行首那一 tick 落在第一个音节上，但进度是 0（还没开口）。</summary>
    [Fact]
    public void AtLineStart_LandsOnFirstSyllableWithZeroProgress()
    {
        var document = TwoLines();

        var cursor = document.Locate(TimeSpan.FromMilliseconds(1000));

        Assert.Equal(0, cursor.LineIndex);
        Assert.Equal(0, cursor.SyllableIndex);
        Assert.Equal(0, cursor.SyllableProgress);
        Assert.Equal(0, cursor.LineProgress);
    }

    [Fact]
    public void InsideSyllable_ReportsFractionalProgress()
    {
        var document = TwoLines();

        // 第一个音节 1000–1500，中点
        var cursor = document.Locate(TimeSpan.FromMilliseconds(1250));

        Assert.Equal(0, cursor.LineIndex);
        Assert.Equal(0, cursor.SyllableIndex);
        Assert.Equal(0.5, cursor.SyllableProgress, precision: 6);
    }

    // ── 行内音节之间的空隙 ──────────────────────────────────────────────────

    /// <summary>
    /// 长音唱完到下一个字之间的空隙：高亮**停在原地**，不继续往前爬。
    /// </summary>
    /// <remarks>
    /// 这是卡拉OK 的正确观感 —— 光标跟着声音走，不该在间奏里匀速滑行。
    /// 报「上一个音节 + 进度 1」就自然得到这个效果，不需要渲染层特判。
    /// </remarks>
    [Fact]
    public void BetweenSyllables_StopsOnTheFinishedOne()
    {
        var document = GapDocument();

        var cursor = document.Locate(TimeSpan.FromMilliseconds(1500));

        Assert.Equal(0, cursor.LineIndex);
        Assert.Equal(0, cursor.SyllableIndex);          // 仍是第一个音节
        Assert.Equal(1, cursor.SyllableProgress);       // 且是满的
    }

    [Fact]
    public void AtSecondSyllableStart_MovesOn()
    {
        var document = GapDocument();

        var cursor = document.Locate(TimeSpan.FromMilliseconds(2000));

        Assert.Equal(1, cursor.SyllableIndex);
        Assert.Equal(0, cursor.SyllableProgress);
    }

    // ── 行边界 / 曲末 ───────────────────────────────────────────────────────

    /// <summary>行边界归下一行，与 <see cref="LyricDocument.IndexOfLineAt"/> 一致。</summary>
    [Fact]
    public void AtNextLineStart_AdvancesToTheNextLine()
    {
        var document = TwoLines();

        var cursor = document.Locate(TimeSpan.FromMilliseconds(2000));

        Assert.Equal(1, cursor.LineIndex);
        Assert.Equal(0, cursor.SyllableIndex);
        Assert.Equal(0, cursor.SyllableProgress);
    }

    /// <summary>
    /// 曲末之后保持末行满高亮 —— 清空比留着更难看。
    /// </summary>
    [Fact]
    public void PastTheEnd_KeepsTheLastLineFullyPlayed()
    {
        var document = TwoLines();

        var cursor = document.Locate(TimeSpan.FromMinutes(5));

        Assert.Equal(1, cursor.LineIndex);
        Assert.Equal(1, cursor.SyllableIndex);
        Assert.Equal(1, cursor.SyllableProgress);
        Assert.Equal(1, cursor.LineProgress);
    }

    // ── 退化与异常数据 ──────────────────────────────────────────────────────

    [Fact]
    public void EmptyDocument_ReportsNoLine()
    {
        var cursor = LyricDocument.Empty.Locate(TimeSpan.FromSeconds(3));

        Assert.Equal(LyricCursor.None, cursor);
    }

    /// <summary>时长为零的音节（异常数据）不能变成除零。</summary>
    [Fact]
    public void ZeroDurationSyllable_DoesNotProduceNaN()
    {
        var line = new LyricLine(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
            "x",
            [new LyricSyllable("x", TimeSpan.FromSeconds(1), TimeSpan.Zero)]);

        var document = new LyricDocument([line], LyricKind.WordByWord);

        Assert.Equal(0, document.Locate(TimeSpan.FromMilliseconds(999)).SyllableProgress);
        Assert.Equal(1, document.Locate(TimeSpan.FromSeconds(1)).SyllableProgress);
        Assert.Equal(1, document.Locate(TimeSpan.FromSeconds(2)).SyllableProgress);
    }

    /// <summary>时长为零的**行**同理。</summary>
    [Fact]
    public void ZeroDurationLine_DoesNotProduceNaN()
    {
        var line = new LyricLine(
            TimeSpan.FromSeconds(1),
            TimeSpan.Zero,
            "x",
            [new LyricSyllable("x", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1))]);

        var document = new LyricDocument([line], LyricKind.WordByWord);

        Assert.Equal(1, document.Locate(TimeSpan.FromSeconds(1)).LineProgress);
    }

    // ── 纯函数性 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 同一位置反复查询结果完全一致。seek 之后不残留上一帧的高亮，靠的就是这条。
    /// </summary>
    [Fact]
    public void Locate_IsPure()
    {
        var document = BodianLyricParser.Parse(Fixtures.Read("lyric-228908-lrcx1.lrc"));
        var position = TimeSpan.FromMilliseconds(42137);

        var first = document.Locate(position);

        for (var i = 0; i < 1000; i++)
        {
            Assert.Equal(first, document.Locate(position));
        }
    }

    // ── 真实 fixture ────────────────────────────────────────────────────────

    /// <summary>
    /// <b>本组最有价值的一条。</b> 逐字版每一行的每一个音节的起点，都必须恰好落在它自己身上。
    /// </summary>
    /// <remarks>
    /// 一次覆盖 63 行 × 全部音节：二分写错、绝对/相对偏移算错、音节边界差一，
    /// 都会在这条上立刻挂掉。
    /// </remarks>
    [Fact]
    public void WordByWordFixture_EverySyllableStartLandsExactlyOnItself()
    {
        var document = BodianLyricParser.Parse(Fixtures.Read("lyric-228908-lrcx1.lrc"));
        var checkedSyllables = 0;

        for (var lineIndex = 0; lineIndex < document.Lines.Count; lineIndex++)
        {
            var line = document.Lines[lineIndex];

            for (var syllableIndex = 0; syllableIndex < line.Syllables.Count; syllableIndex++)
            {
                var syllable = line.Syllables[syllableIndex];
                var cursor = document.Locate(syllable.Start);

                Assert.Equal(lineIndex, cursor.LineIndex);
                Assert.Equal(0, cursor.SyllableProgress);

                // 用起点断言而不是下标：重复起点的音节会解析到后一个，下标断言会误报。
                Assert.Equal(syllable.Start, line.Syllables[cursor.SyllableIndex].Start);

                checkedSyllables++;
            }
        }

        Assert.True(checkedSyllables > 200, $"只覆盖了 {checkedSyllables} 个音节，样本可能没读对");
    }

    /// <summary>
    /// 逐行版整行就是一个音节，于是逐字进度与整行进度必然同步 ——
    /// 扫光自然退化成「整行一次性填满」，渲染层不需要为两种版式分叉。
    /// </summary>
    [Fact]
    public void LineByLineFixture_SyllableProgressTracksTheLine()
    {
        var document = BodianLyricParser.Parse(Fixtures.Read("lyric-228908-lrcx0.lrc"));
        var samples = 0;

        foreach (var line in document.Lines)
        {
            Assert.Single(line.Syllables);

            for (var offset = 0; offset <= 4; offset++)
            {
                var position = line.Start + TimeSpan.FromTicks(line.Duration.Ticks * offset / 4);
                var cursor = document.Locate(position);

                Assert.Equal(cursor.LineProgress, cursor.SyllableProgress, precision: 6);
                samples++;
            }
        }

        Assert.True(samples > 200, $"只采样了 {samples} 个位置，样本可能没读对");
    }

    // ── SpeechEnd ───────────────────────────────────────────────────────────

    /// <summary>本行唱完的时刻是最后一个音节的结束，**不是**行窗口的结束。</summary>
    [Fact]
    public void SpeechEnd_StopsAtTheLastSyllable()
    {
        var document = GapDocument();
        var line = document.Lines[0];

        // 行窗口到 4000，但最后一个音节 2000–2800 就唱完了
        Assert.Equal(TimeSpan.FromSeconds(4), line.End);
        Assert.Equal(TimeSpan.FromMilliseconds(2800), line.SpeechEnd());
    }

    /// <summary>音节的结束可能越出行窗口（时长来自编解码器，窗口来自下一行行首），要钳回去。</summary>
    [Fact]
    public void SpeechEnd_ClampsIntoTheLineWindow()
    {
        var line = new LyricLine(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1),
            "x",
            [new LyricSyllable("x", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(9))]);

        Assert.Equal(TimeSpan.FromSeconds(2), line.SpeechEnd());
    }

    // ── 脚手架 ──────────────────────────────────────────────────────────────

    /// <summary>两行，每行两个音节，行窗口首尾相接（1000–2000 / 2000–3000）。</summary>
    private static LyricDocument TwoLines() => new(
        [
            Line(1000, 1000, ("a", 1000, 500), ("b", 1500, 500)),
            Line(2000, 1000, ("c", 2000, 500), ("d", 2500, 500)),
        ],
        LyricKind.WordByWord);

    /// <summary>第一个音节唱完后有一段空隙：1000–1500 唱，2000 才唱下一个。</summary>
    private static LyricDocument GapDocument() => new(
        [Line(1000, 3000, ("a", 1000, 500), ("b", 2000, 800))],
        LyricKind.WordByWord);

    /// <summary>行：起点与窗口长度（毫秒），行文本由音节拼出来（解析器保证的不变量）。</summary>
    private static LyricLine Line(
        int startMs,
        int durationMs,
        params (string Text, int StartMs, int DurationMs)[] syllables) => new(
        TimeSpan.FromMilliseconds(startMs),
        TimeSpan.FromMilliseconds(durationMs),
        string.Concat(syllables.Select(s => s.Text)),
        [.. syllables.Select(s => new LyricSyllable(
            s.Text,
            TimeSpan.FromMilliseconds(s.StartMs),
            TimeSpan.FromMilliseconds(s.DurationMs)))]);
}
