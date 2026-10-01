using Bodian.Core.Models.Lyrics;
using Xunit;

namespace Bodian.Core.Tests;

public sealed class LyricMotionMathTests
{
    [Fact]
    public void BrowsingFocusFollowsTheLineAtTheDisplayAnchor()
    {
        double[] beforeScroll = [100, 200, 300, 400];
        double[] afterScroll = [-100, 0, 100, 200];
        Assert.Equal(1, LyricMotionMath.NearestLine(beforeScroll, 210));
        Assert.Equal(3, LyricMotionMath.NearestLine(afterScroll, 210));
    }

    [Fact]
    public void EmptyLyricsHaveNoBrowsingFocus()
        => Assert.Equal(-1, LyricMotionMath.NearestLine([], 100));

    [Fact]
    public void PlaybackCorrectionsCannotRewindAnAlreadyHighlightedSyllable()
    {
        var syllable = new LyricSyllable("长", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        var presented = TimeSpan.Zero;
        var previousProgress = 0.0;
        foreach (var milliseconds in new[] { 1000, 1500, 1490, 1900, 1860, 2500, 3000, 2960, 3100 })
        {
            presented = LyricMotionMath.AdvanceHighlight(presented, TimeSpan.FromMilliseconds(milliseconds), discontinuity: false);
            var progress = syllable.ProgressAt(presented);
            Assert.True(progress >= previousProgress);
            previousProgress = progress;
        }
        Assert.Equal(1, previousProgress);
    }

    [Fact]
    public void PlaybackCorrectionAtALineBoundaryCannotMakeTheCurrentLineOscillate()
    {
        var document = new LyricDocument(new[]
        {
            new LyricLine(TimeSpan.Zero, TimeSpan.FromSeconds(2), "第一句", []),
            new LyricLine(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), "第二句", []),
        }, LyricKind.LineByLine);
        var presented = LyricMotionMath.AdvanceHighlight(TimeSpan.Zero, TimeSpan.FromMilliseconds(2010), false);
        presented = LyricMotionMath.AdvanceHighlight(presented, TimeSpan.FromMilliseconds(1980), false);
        Assert.Equal(1, document.IndexOfLineAt(presented));
    }

    [Fact]
    public void DeliberateSeekAllowsHighlightToStartFromTheNewPosition()
    {
        var presented = LyricMotionMath.AdvanceHighlight(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5), true);
        Assert.Equal(TimeSpan.FromSeconds(5), presented);
    }

    [Theory]
    [InlineData(0.82)]
    [InlineData(1.0)]
    public void SpringProducesSameMotionAtDifferentFrameRates(double damping)
    {
        var direct = LyricMotionMath.AdvanceSpring(30, -20, 240, 0.3, damping: damping);
        var position = 30.0;
        var velocity = -20.0;
        for (var frame = 0; frame < 18; frame++)
        {
            (position, velocity) = LyricMotionMath.AdvanceSpring(
                position, velocity, 240, 1.0 / 60, damping: damping);
        }
        Assert.Equal(direct.Position, position, precision: 6);
        Assert.Equal(direct.Velocity, velocity, precision: 6);
    }

    [Fact]
    public void SpringPreservesItsVelocityWhenTheTargetChanges()
    {
        var moving = LyricMotionMath.AdvanceSpring(0, 0, 120, 0.08);
        var continued = LyricMotionMath.AdvanceSpring(moving.Position, moving.Velocity, 240, 0.000001);
        Assert.InRange(Math.Abs(continued.Position - moving.Position), 0, 0.01);
        Assert.InRange(Math.Abs(continued.Velocity - moving.Velocity), 0, 0.1);
        Assert.True(continued.Velocity > 0);
    }

    [Fact]
    public void SpringSettlesAfterAFrameGap()
    {
        var settled = LyricMotionMath.AdvanceSpring(-120, 500, 400, 10);
        Assert.Equal(400, settled.Position);
        Assert.Equal(0, settled.Velocity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SpringDoesNotAdvanceWithNonpositiveTime(double seconds)
    {
        var unchanged = LyricMotionMath.AdvanceSpring(12, 34, 99, seconds);
        Assert.Equal(12, unchanged.Position);
        Assert.Equal(34, unchanged.Velocity);
    }

    [Theory]
    [InlineData(0, 0, 20, 60, 0)]
    [InlineData(0.25, 0, 20, 60, 0.75)]
    [InlineData(0.25, 20, 40, 60, 0)]
    [InlineData(0.5, 0, 20, 60, 1)]
    [InlineData(0.5, 20, 40, 60, 0.25)]
    [InlineData(1, 20, 40, 60, 1)]
    [InlineData(2, 0, 20, 60, 1)]
    [InlineData(-1, 0, 20, 60, 0)]
    public void SweepAdvancesAcrossGlyphsUsingTheirActualWidths(
        double progress, double offset, double width, double totalWidth, double expected)
        => Assert.Equal(expected, LyricMotionMath.GlyphProgress(progress, offset, width, totalWidth), precision: 6);

    [Fact]
    public void ZeroWidthGlyphDoesNotProduceAnInvalidNumber()
        => Assert.Equal(0.5, LyricMotionMath.GlyphProgress(0.5, 0, 0, 0));
}
