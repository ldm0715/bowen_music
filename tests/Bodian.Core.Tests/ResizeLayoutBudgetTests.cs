using Bodian.Core.Performance;
using Bodian.Core.Tests.Support;
using Xunit;

namespace Bodian.Core.Tests;

public sealed class ResizeLayoutBudgetTests
{
    [Fact]
    public void RepeatedSizeRequests_DoNotPostponeTheNextLayout()
    {
        var time = new ManualTimeProvider();
        var budget = new ResizeLayoutBudget(time);
        budget.RecordLayout(TimeSpan.FromMilliseconds(2));
        time.Advance(TimeSpan.FromMilliseconds(4));
        var remaining = budget.Remaining;
        for (var i = 0; i < 100; i++) Assert.Equal(remaining, budget.Remaining);
        time.Advance(remaining);
        Assert.Equal(TimeSpan.Zero, budget.Remaining);
    }

    [Fact]
    public void ExpensiveLayout_IsLimitedButDoesNotFreezeUntilMouseRelease()
    {
        var time = new ManualTimeProvider();
        var budget = new ResizeLayoutBudget(time);
        budget.RecordLayout(TimeSpan.FromMilliseconds(100));
        Assert.Equal(TimeSpan.FromMilliseconds(50), budget.Remaining);
        Assert.False(budget.CanReflowInteractively);
        time.Advance(TimeSpan.FromMilliseconds(50));
        Assert.Equal(TimeSpan.Zero, budget.Remaining);
    }

    [Fact]
    public void CheaperLayouts_GraduallyRecoverThe120HzBudget()
    {
        var time = new ManualTimeProvider();
        var budget = new ResizeLayoutBudget(time);
        budget.RecordLayout(TimeSpan.FromMilliseconds(10));
        Assert.Equal(TimeSpan.FromMilliseconds(30), budget.Interval);
        for (var i = 0; i < 20; i++) budget.RecordLayout(TimeSpan.FromMilliseconds(1));
        Assert.Equal(ResizeLayoutBudget.FrameBudget, budget.Interval);
        Assert.True(budget.CanReflowInteractively);
    }

    [Fact]
    public void LeavingInteractiveResize_CommitsImmediately()
    {
        var budget = new ResizeLayoutBudget(new ManualTimeProvider());
        budget.RecordLayout(TimeSpan.FromMilliseconds(20));
        budget.Reset();
        Assert.Equal(TimeSpan.Zero, budget.Remaining);
        Assert.False(budget.CanReflowInteractively);
    }

    [Fact]
    public void HeavyPageUsesContinuousCompositionPreviewUntilMouseRelease()
    {
        var time = new ManualTimeProvider();
        var budget = new ResizeLayoutBudget(time);
        budget.RecordLayout(TimeSpan.FromMilliseconds(40));
        for (var i = 0; i < 120; i++)
        {
            time.Advance(TimeSpan.FromMilliseconds(1000.0 / 120));
            Assert.False(budget.CanReflowInteractively);
        }
    }
}
