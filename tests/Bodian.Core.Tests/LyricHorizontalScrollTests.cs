using Bodian.Core.Models.Lyrics;
using Bodian.Core.Playback;
using Bodian.Core.Tests.Support;
using Xunit;

namespace Bodian.Core.Tests;

public sealed class LyricHorizontalScrollTests
{
    [Theory]
    [InlineData(80, 100, 0)]
    [InlineData(80, 100, 0.5)]
    [InlineData(80, 100, 1)]
    [InlineData(100, 100, 1)]
    public void LyricsThatFitDoNotMove(double textWidth, double viewportWidth, double progress)
        => Assert.Equal(0, LyricHorizontalScroll.Offset(textWidth, viewportWidth, progress));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.1, 0)]
    [InlineData(0.5, 100)]
    [InlineData(0.9, 200)]
    [InlineData(1, 200)]
    [InlineData(-1, 0)]
    [InlineData(2, 200)]
    public void LongLyricsHoldTheStartThenRevealTheMiddleAndEnd(double progress, double expected)
        => Assert.Equal(expected, LyricHorizontalScroll.Offset(300, 100, progress), precision: 6);

    [Fact]
    public void CurrentlySungPositionStaysVisibleThroughoutALongLine()
    {
        foreach (var width in new[] { 101.0, 300, 2000 })
        {
            var previous = 0.0;
            for (var frame = 0; frame <= 1200; frame++)
            {
                var progress = frame / 1200.0;
                var offset = LyricHorizontalScroll.Offset(width, 100, progress);
                Assert.InRange(width * progress, offset, offset + 100);
                Assert.InRange(offset, previous, width - 100);
                previous = offset;
            }
            Assert.Equal(width - 100, previous);
        }
    }

    [Theory]
    [InlineData(300, 0, 0.5)]
    [InlineData(300, -100, 0.5)]
    [InlineData(double.NaN, 100, 0.5)]
    [InlineData(double.PositiveInfinity, 100, 0.5)]
    [InlineData(300, double.NaN, 0.5)]
    [InlineData(300, double.PositiveInfinity, 0.5)]
    [InlineData(300, 100, double.NaN)]
    [InlineData(300, 100, double.PositiveInfinity)]
    public void InvalidOrUnmeasuredInputsKeepTheLineAtItsStart(double textWidth, double viewportWidth, double progress)
        => Assert.Equal(0, LyricHorizontalScroll.Offset(textWidth, viewportWidth, progress));

    [Fact]
    public void LineLyricsFollowTheirOwnTimeWindow()
    {
        var line = new LyricLine(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), "长歌词", []);
        Assert.Equal(0, Offset(line, LyricKind.LineByLine, TimeSpan.FromSeconds(9)));
        Assert.Equal(0, Offset(line, LyricKind.LineByLine, line.Start));
        Assert.Equal(100, Offset(line, LyricKind.LineByLine, TimeSpan.FromSeconds(15)));
        Assert.Equal(200, Offset(line, LyricKind.LineByLine, line.End));
        Assert.Equal(200, Offset(line, LyricKind.LineByLine, TimeSpan.FromSeconds(25)));
    }

    [Fact]
    public void WordLyricsFollowSyllableTimingInsteadOfElapsedLineTime()
    {
        var line = new LyricLine(TimeSpan.Zero, TimeSpan.FromSeconds(10), "甲乙丙丁",
        [
            new LyricSyllable("甲", TimeSpan.Zero, TimeSpan.FromSeconds(8)),
            new LyricSyllable("乙丙丁", TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(2)),
        ]);
        Assert.Equal(0, Offset(line, LyricKind.WordByWord, TimeSpan.FromSeconds(4)));
        Assert.Equal(137.5, Offset(line, LyricKind.WordByWord, TimeSpan.FromSeconds(9)));
        Assert.Equal(200, Offset(line, LyricKind.WordByWord, line.End));
    }

    [Fact]
    public void SyllableGapsAndThePauseAfterSingingDoNotKeepScrolling()
    {
        var line = new LyricLine(TimeSpan.Zero, TimeSpan.FromSeconds(20), "甲乙丙丁",
        [
            new LyricSyllable("甲乙", TimeSpan.Zero, TimeSpan.FromSeconds(2)),
            new LyricSyllable("丙丁", TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(2)),
        ]);
        Assert.Equal(100, Offset(line, LyricKind.WordByWord, TimeSpan.FromSeconds(2)));
        Assert.Equal(100, Offset(line, LyricKind.WordByWord, TimeSpan.FromSeconds(7)));
        Assert.Equal(200, Offset(line, LyricKind.WordByWord, TimeSpan.FromSeconds(10)));
        Assert.Equal(200, Offset(line, LyricKind.WordByWord, TimeSpan.FromSeconds(19)));
    }

    [Fact]
    public void MissingWordTimingFallsBackToLineTiming()
    {
        var line = new LyricLine(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), "译文",
            [new LyricSyllable("译文", TimeSpan.Zero, TimeSpan.Zero)]);
        Assert.Equal(0, Offset(line, LyricKind.WordByWord, TimeSpan.FromSeconds(9)));
        Assert.Equal(100, Offset(line, LyricKind.WordByWord, TimeSpan.FromSeconds(15)));
        Assert.Equal(200, Offset(line, LyricKind.WordByWord, line.End));
    }

    [Fact]
    public void ZeroDurationNeverProducesAnInvalidOffset()
    {
        var line = new LyricLine(TimeSpan.FromSeconds(1), TimeSpan.Zero, "歌词", []);
        Assert.Equal(0, Offset(line, LyricKind.LineByLine, TimeSpan.Zero));
        Assert.Equal(200, Offset(line, LyricKind.LineByLine, line.Start));
    }

    [Fact]
    public void PausingFreezesTheScrollAndResumingContinuesFromThatPosition()
    {
        var time = new ManualTimeProvider();
        var clock = new LyricsPlaybackClock(time);
        var line = new LyricLine(TimeSpan.Zero, TimeSpan.FromSeconds(10), "长歌词", []);
        clock.SetPlaying(true);
        time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(100, Offset(line, LyricKind.LineByLine, clock.Position));
        clock.SetPlaying(false);
        time.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(100, Offset(line, LyricKind.LineByLine, clock.Position));
        clock.SetPlaying(true);
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(130, Offset(line, LyricKind.LineByLine, clock.Position));
    }

    [Fact]
    public void SeekingAndChangingLinesRecomputeTheScrollWithoutCarryingOldOffsets()
    {
        var clock = new LyricsPlaybackClock(new ManualTimeProvider());
        var line = new LyricLine(TimeSpan.Zero, TimeSpan.FromSeconds(10), "第一句", []);
        clock.Sync(TimeSpan.FromSeconds(8), force: true);
        Assert.Equal(190, Offset(line, LyricKind.LineByLine, clock.Position));
        clock.Sync(TimeSpan.FromSeconds(1), force: true);
        Assert.Equal(0, Offset(line, LyricKind.LineByLine, clock.Position));
        var next = line with { Start = TimeSpan.FromSeconds(10), Text = "第二句" };
        clock.Sync(next.Start, force: true);
        Assert.Equal(0, Offset(next, LyricKind.LineByLine, clock.Position));
    }

    [Fact]
    public void ResizingKeepsTheSungPositionVisibleAndStopsScrollingWhenTheLineFits()
    {
        Assert.Equal(100, LyricHorizontalScroll.Offset(300, 100, 0.5));
        Assert.Equal(50, LyricHorizontalScroll.Offset(300, 200, 0.5));
        Assert.Equal(0, LyricHorizontalScroll.Offset(300, 400, 0.5));
    }

    private static double Offset(LyricLine line, LyricKind kind, TimeSpan position)
        => LyricHorizontalScroll.Offset(300, 100, LyricHorizontalScroll.ProgressAt(line, kind, position));
}
