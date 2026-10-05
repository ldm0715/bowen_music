using Bodian.Core.Models;
using Xunit;

namespace Bodian.Core.Tests;

public sealed class DesktopLyricsLayoutMetricsTests
{
    [Theory]
    [InlineData(20, 60)]
    [InlineData(42, 121.6)]
    [InlineData(96, 60)]
    [InlineData(96, 272.8)]
    [InlineData(42, 24)]
    public void DualLine_BothRowsStayInsideCurrentViewport(double fontSize, double height)
    {
        var metrics = DesktopLyricsLayoutMetrics.Calculate(fontSize, height, dualLine: true);
        var secondBottom = metrics.Top + metrics.LineHeight * 2 + metrics.Gap;

        Assert.True(metrics.FontSize > 0);
        Assert.True(metrics.LineHeight > 0);
        Assert.InRange(secondBottom, 0, height + 0.000001);
    }

    [Fact]
    public void FontGrowthBeforeWindowResize_StillKeepsSecondLineVisible()
    {
        var metrics = DesktopLyricsLayoutMetrics.Calculate(96, 60, dualLine: true);

        Assert.Equal(20, metrics.FontSize, precision: 6);
        Assert.Equal(28, metrics.LineHeight, precision: 6);
        Assert.Equal(60, metrics.Top + metrics.LineHeight * 2 + metrics.Gap, precision: 6);
    }

    [Fact]
    public void GrowThenShrink_RecomputesOriginalRowMetrics()
    {
        var initial = DesktopLyricsLayoutMetrics.Calculate(42, 121.6, dualLine: true);
        var larger = DesktopLyricsLayoutMetrics.Calculate(96, 272.8, dualLine: true);
        var pendingShrink = DesktopLyricsLayoutMetrics.Calculate(96, 121.6, dualLine: true);
        var final = DesktopLyricsLayoutMetrics.Calculate(42, 121.6, dualLine: true);

        Assert.True(larger.FontSize > initial.FontSize);
        Assert.Equal(initial, pendingShrink);
        Assert.Equal(initial, final);
    }

    [Fact]
    public void SingleLine_UsesEntireAvailableHeight()
    {
        var metrics = DesktopLyricsLayoutMetrics.Calculate(96, 56, dualLine: false);

        Assert.Equal(0, metrics.Gap);
        Assert.Equal(40, metrics.FontSize, precision: 6);
        Assert.Equal(56, metrics.LineHeight, precision: 6);
    }
}
