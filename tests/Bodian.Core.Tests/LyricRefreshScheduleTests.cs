using Bodian.Core.Models.Lyrics;
using Xunit;

namespace Bodian.Core.Tests;

public sealed class LyricRefreshScheduleTests
{
    private static LyricDocument WordLyrics => new([
        new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), "甲乙", [
            new("甲", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2)),
            new("乙", TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(2)),
        ]),
        new(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(10), "丙", [
            new("丙", TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(1)),
        ]),
    ], LyricKind.WordByWord);

    [Fact]
    public void EmptyOrPausedLyrics_DoNotWakeTheRenderer()
    {
        Assert.Null(Delay(LyricDocument.Empty, 0));
        Assert.Null(Delay(WordLyrics, 6, playing: false));
    }

    [Fact]
    public void BeforeTheFirstLine_WaitsForItsStart()
        => Assert.Equal(TimeSpan.FromSeconds(3), Delay(WordLyrics, 2));

    [Fact]
    public void ActiveSweep_UsesSixtyFramesPerSecond()
        => Assert.Equal(LyricRefreshSchedule.AnimationInterval, Delay(WordLyrics, 6));

    [Fact]
    public void BetweenSyllables_WaitsForTheNextSyllable()
        => Assert.Equal(TimeSpan.FromSeconds(4), Delay(WordLyrics, 8));

    [Fact]
    public void AfterSinging_WaitsForTheNextLine()
        => Assert.Equal(TimeSpan.FromSeconds(6), Delay(WordLyrics, 14));

    [Fact]
    public void LastLineAfterSinging_DoesNotPollForever()
        => Assert.Null(Delay(WordLyrics, 22));

    [Fact]
    public void FittingMiniLyrics_OnlyWaitForTimelineChanges()
        => Assert.Equal(TimeSpan.FromSeconds(6), Delay(WordLyrics, 6, highlight: false));

    [Fact]
    public void OverflowingMiniLyrics_AnimateDuringSingingAndWaitInGaps()
    {
        Assert.Equal(LyricRefreshSchedule.AnimationInterval, Delay(WordLyrics, 6, highlight: false, overflow: true));
        Assert.Equal(TimeSpan.FromSeconds(4), Delay(WordLyrics, 8, highlight: false, overflow: true));
    }

    [Fact]
    public void LineLyrics_AnimateOnlyWhenTheyOverflow()
    {
        var document = new LyricDocument([
            new(TimeSpan.Zero, TimeSpan.FromSeconds(10), "line", []),
            new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), "next", []),
        ], LyricKind.LineByLine);
        Assert.Equal(TimeSpan.FromSeconds(5), Delay(document, 5));
        Assert.Equal(LyricRefreshSchedule.AnimationInterval, Delay(document, 5, overflow: true));
        Assert.Null(Delay(document, 25, overflow: true));
    }

    [Fact]
    public void ZeroDurationSyllables_WakeAtStartWithoutContinuousFrames()
    {
        var document = new LyricDocument([
            new(TimeSpan.Zero, TimeSpan.FromSeconds(10), "甲", [new("甲", TimeSpan.FromSeconds(3), TimeSpan.Zero)]),
        ], LyricKind.WordByWord);
        Assert.Equal(TimeSpan.FromSeconds(1), Delay(document, 2));
        Assert.Null(Delay(document, 3));
    }

    [Fact]
    public void MissingWordTiming_StillScrollsLongLines()
    {
        var document = new LyricDocument([
            new(TimeSpan.Zero, TimeSpan.FromSeconds(10), "missing timing", []),
        ], LyricKind.WordByWord);
        Assert.Equal(LyricRefreshSchedule.AnimationInterval, Delay(document, 5, highlight: false, overflow: true));
    }

    private static TimeSpan? Delay(LyricDocument document, double seconds, bool playing = true,
        bool highlight = true, bool overflow = false)
        => LyricRefreshSchedule.NextUpdateDelay(document, TimeSpan.FromSeconds(seconds), playing, highlight, overflow);
}
