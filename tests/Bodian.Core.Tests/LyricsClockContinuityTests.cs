using Bodian.Core.Models.Lyrics;
using Bodian.Core.Playback;
using Bodian.Core.Tests.Support;
using Xunit;

namespace Bodian.Core.Tests;

public sealed class LyricsClockContinuityTests
{
    [Theory]
    [InlineData(-250)]
    [InlineData(-80)]
    [InlineData(-30)]
    [InlineData(30)]
    [InlineData(100)]
    [InlineData(250)]
    public void OrdinaryReportsDoNotMoveTheDisplayedPositionImmediately(int deviationMilliseconds)
    {
        var time = new ManualTimeProvider();
        var clock = new LyricsPlaybackClock(time);
        clock.Sync(TimeSpan.FromSeconds(10), force: true);
        clock.SetPlaying(true);
        time.Advance(TimeSpan.FromMilliseconds(200));
        var before = clock.Position;

        clock.Sync(before + TimeSpan.FromMilliseconds(deviationMilliseconds));

        Assert.Equal(before, clock.Position);
        Assert.Equal(1, clock.JumpCount);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(80)]
    [InlineData(250)]
    public void FrequentAudioReportsDoNotFreezeFastSyllableHighlight(int reportLatencyMilliseconds)
    {
        var time = new ManualTimeProvider();
        var clock = new LyricsPlaybackClock(time);
        var start = TimeSpan.FromSeconds(10);
        clock.Sync(start, force: true);
        clock.SetPlaying(true);
        var frameInterval = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60);
        var highlighted = clock.Position;
        var previousPosition = highlighted;
        var previousSyllable = -1;
        var frameJitter = new[] { 0, 15, -10, 5 };

        for (var frame = 1; frame <= 240; frame++)
        {
            time.Advance(frameInterval);
            if (frame % 12 == 0)
            {
                var reported = start + frameInterval * frame
                    - TimeSpan.FromMilliseconds(reportLatencyMilliseconds + frameJitter[(frame / 12) % frameJitter.Length]);
                var beforeSync = clock.Position;
                clock.Sync(reported);
                Assert.Equal(beforeSync, clock.Position);
            }

            var position = clock.Position;
            var frameAdvance = (position - previousPosition).TotalMilliseconds;
            Assert.InRange(frameAdvance, 12.4, 20.9);
            highlighted = LyricMotionMath.AdvanceHighlight(highlighted, position, discontinuity: false);
            Assert.True(highlighted > previousPosition, $"Highlight stopped at frame {frame}.");
            var syllable = (int)((highlighted - start).TotalMilliseconds / 80);
            Assert.InRange(syllable - previousSyllable, 0, 1);
            previousSyllable = syllable;
            previousPosition = position;
        }

        Assert.Equal(1, clock.JumpCount);
    }

    [Fact]
    public void ANewReportPreservesTheUnfinishedCorrection()
    {
        var time = new ManualTimeProvider();
        var clock = new LyricsPlaybackClock(time);
        clock.Sync(TimeSpan.FromSeconds(10), force: true);
        clock.SetPlaying(true);
        time.Advance(TimeSpan.FromMilliseconds(200));
        clock.Sync(TimeSpan.FromMilliseconds(9950));
        time.Advance(TimeSpan.FromMilliseconds(200));
        var presented = clock.Position;

        clock.Sync(TimeSpan.FromMilliseconds(10180));

        Assert.Equal(presented, clock.Position);
        time.Advance(TimeSpan.FromMilliseconds(16));
        Assert.True(clock.Position > presented);
    }

    [Fact]
    public void PausedClockDoesNotContinueConsumingTheCorrection()
    {
        var time = new ManualTimeProvider();
        var clock = new LyricsPlaybackClock(time);
        clock.Sync(TimeSpan.FromSeconds(10), force: true);
        clock.SetPlaying(true);
        time.Advance(TimeSpan.FromMilliseconds(200));
        clock.Sync(TimeSpan.FromMilliseconds(10100));
        clock.SetPlaying(false);
        var frozen = clock.Position;

        time.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(frozen, clock.Position);
    }

    [Theory]
    [InlineData(9.85)]
    [InlineData(10.15)]
    public void ExplicitSeekStillChangesThePositionImmediately(double targetSeconds)
    {
        var time = new ManualTimeProvider();
        var clock = new LyricsPlaybackClock(time);
        clock.Sync(TimeSpan.FromSeconds(10), force: true);
        clock.SetPlaying(true);

        clock.Sync(TimeSpan.FromSeconds(targetSeconds), force: true);

        Assert.Equal(TimeSpan.FromSeconds(targetSeconds), clock.Position);
        Assert.Equal(2, clock.JumpCount);
    }
}
