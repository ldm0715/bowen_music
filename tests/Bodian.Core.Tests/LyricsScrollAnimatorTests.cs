using Bodian.WinUI.LyricRenderer;
using Xunit;

namespace Bodian.Core.Tests;

public sealed class LyricsScrollAnimatorTests
{
    [Theory]
    [InlineData(60, 0, 78)]
    [InlineData(120, 0, 132)]
    [InlineData(60, 300, 180)]
    [InlineData(120, -400, -270)]
    public void CurrentLine_SettlesMonotonicallyWithoutPassingItsAnchor(int rate, double start, double target)
    {
        var animator = new LyricsScrollAnimator();
        animator.Reset(4, start);
        animator.Retarget(target, 1, TimeSpan.Zero);
        var previous = start;
        for (var frame = 1; frame <= rate * 2; frame++)
        {
            animator.Update(TimeSpan.FromSeconds(frame / (double)rate), 1.0 / rate);
            var current = animator.OffsetAt(1);
            Assert.InRange(current, Math.Min(previous, target) - 0.000001, Math.Max(previous, target) + 0.000001);
            previous = current;
        }
        Assert.Equal(target, animator.OffsetAt(1), precision: 5);
    }

    [Fact]
    public void FastTargetChange_CannotCarryCurrentLinePastANearbyAnchor()
    {
        var animator = new LyricsScrollAnimator();
        animator.Reset(3, 0);
        animator.Retarget(300, 1, TimeSpan.Zero);
        var now = TimeSpan.FromSeconds(0.1);
        animator.Update(now, 0.1);
        var current = animator.OffsetAt(1);
        var nearby = current + 1;
        animator.Retarget(nearby, 1, now);
        for (var frame = 0; frame < 120; frame++)
        {
            now += TimeSpan.FromSeconds(1.0 / 120);
            animator.Update(now, 1.0 / 120);
            Assert.InRange(animator.OffsetAt(1), current - 0.000001, nearby + 0.000001);
            current = animator.OffsetAt(1);
        }
        Assert.Equal(nearby, animator.OffsetAt(1), precision: 5);
    }

    [Fact]
    public void OppositeTargetChange_MovesTowardNewAnchorImmediately()
    {
        var animator = new LyricsScrollAnimator();
        animator.Reset(3, 0);
        animator.Retarget(300, 1, TimeSpan.Zero);
        var now = TimeSpan.FromSeconds(0.1);
        animator.Update(now, 0.1);
        var previous = animator.OffsetAt(1);
        animator.Retarget(-50, 1, now);
        for (var frame = 0; frame < 120; frame++)
        {
            now += TimeSpan.FromSeconds(1.0 / 120);
            animator.Update(now, 1.0 / 120);
            Assert.InRange(animator.OffsetAt(1), -50 - 0.000001, previous + 0.000001);
            previous = animator.OffsetAt(1);
        }
    }

    [Fact]
    public void OtherLines_KeepTheirStaggeredStart()
    {
        var animator = new LyricsScrollAnimator();
        animator.Reset(5, 0);
        animator.Retarget(100, 1, TimeSpan.Zero);
        animator.Update(TimeSpan.FromSeconds(1.0 / 120), 1.0 / 120);

        Assert.True(animator.OffsetAt(1) > 0);
        Assert.Equal(0, animator.OffsetAt(4));
        animator.Update(TimeSpan.FromSeconds(0.2), 0.2 - 1.0 / 120);
        Assert.True(animator.OffsetAt(4) > 0);
    }

    [Fact]
    public void BrowsingWithoutStagger_DoesNotBounceAnyVisibleLine()
    {
        var animator = new LyricsScrollAnimator();
        animator.Reset(4, 0);
        animator.Retarget(150, 1, TimeSpan.Zero, stagger: false);
        var previous = 0.0;
        for (var frame = 1; frame <= 120; frame++)
        {
            animator.Update(TimeSpan.FromSeconds(frame / 120.0), 1.0 / 120);
            for (var index = 0; index < 4; index++) Assert.InRange(animator.OffsetAt(index), previous - 0.000001, 150.000001);
            previous = animator.OffsetAt(1);
        }
    }

    [Fact]
    public void SettledCurrentLine_StaysFixedDuringFurtherFrames()
    {
        var animator = new LyricsScrollAnimator();
        animator.Reset(2, 0);
        animator.Retarget(100, 1, TimeSpan.Zero);
        animator.Update(TimeSpan.FromSeconds(2), 2);
        for (var frame = 1; frame <= 120; frame++)
        {
            animator.Update(TimeSpan.FromSeconds(2 + frame / 120.0), 1.0 / 120);
            Assert.Equal(100, animator.OffsetAt(1));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(4)]
    public void IncomingAndOutgoingLines_DoNotPassTheirAnchorEither(int line)
    {
        var animator = new LyricsScrollAnimator();
        animator.Reset(5, 0);
        animator.Retarget(150, 1, TimeSpan.Zero);
        var previous = 0.0;
        for (var frame = 1; frame <= 240; frame++)
        {
            animator.Update(TimeSpan.FromSeconds(frame / 120.0), 1.0 / 120);
            Assert.InRange(animator.OffsetAt(line), previous - 0.000001, 150.000001);
            previous = animator.OffsetAt(line);
        }
    }

    [Fact]
    public void RapidLineChanges_DoNotReverseAnIncomingLineBeforeItIsHighlighted()
    {
        var animator = new LyricsScrollAnimator();
        animator.Reset(5, 0);
        var previous = 0.0;
        for (var frame = 0; frame < 300; frame++)
        {
            var now = TimeSpan.FromSeconds(frame / 120.0);
            var step = Math.Min(3, frame / 20);
            animator.Retarget(50 + step * 80, step, now, stagger: false);
            animator.Update(now + TimeSpan.FromSeconds(1.0 / 120), 1.0 / 120);
            Assert.InRange(animator.OffsetAt(4), previous - 0.000001, 290.000001);
            previous = animator.OffsetAt(4);
        }
    }

    [Fact]
    public void SettledScroll_DoesNotRequestContinuousFrames()
    {
        var animator = new LyricsScrollAnimator();
        animator.Reset(100, 0);
        Assert.False(animator.IsAnimating);
        animator.Retarget(400, 50, TimeSpan.Zero);
        Assert.True(animator.IsAnimating);
        for (var frame = 1; frame <= 480; frame++)
            animator.Update(TimeSpan.FromSeconds(frame / 120.0), 1.0 / 120);
        Assert.False(animator.IsAnimating);
        animator.Update(TimeSpan.FromSeconds(5), 1);
        Assert.False(animator.HasMoved);
        Assert.False(animator.IsAnimating);
    }

    [Fact]
    public void StaggeredLines_KeepAnimationAliveWhileWaitingToStart()
    {
        var animator = new LyricsScrollAnimator();
        animator.Reset(5, 0);
        animator.Retarget(100, 0, TimeSpan.Zero);
        animator.Update(TimeSpan.FromSeconds(1.0 / 120), 1.0 / 120);
        Assert.Equal(0, animator.OffsetAt(4));
        Assert.True(animator.IsAnimating);
        Assert.True(animator.HasMoved);
    }

    [Fact]
    public void RetargetingSettledScroll_WakesAnimationAgain()
    {
        var animator = new LyricsScrollAnimator();
        animator.Reset(3, 100);
        animator.Retarget(100, 1, TimeSpan.Zero);
        Assert.False(animator.IsAnimating);
        animator.Retarget(250, 1, TimeSpan.FromSeconds(1));
        Assert.True(animator.IsAnimating);
        animator.Update(TimeSpan.FromSeconds(1.1), 0.1);
        Assert.True(animator.HasMoved);
    }

}
