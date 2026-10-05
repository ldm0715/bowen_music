using Bodian.Core.Models;
using Xunit;

namespace Bodian.Core.Tests;

public sealed class DesktopLyricsWindowGeometryTests
{
    private static readonly WindowPlacement WorkArea = new(100, 50, 1200, 800);

    [Theory]
    [InlineData(108, 300, 100, 300)]
    [InlineData(117, 300, 117, 300)]
    [InlineData(684, 300, 700, 300)]
    [InlineData(683, 300, 683, 300)]
    [InlineData(400, 66, 400, 50)]
    [InlineData(400, 67, 400, 67)]
    [InlineData(400, 584, 400, 600)]
    [InlineData(400, 583, 400, 583)]
    public void Drag_SnapsOnlyInsideEdgeThreshold(int x, int y, int expectedX, int expectedY)
    {
        var result = DesktopLyricsWindowGeometry.Constrain(new(x, y, 600, 250), WorkArea, 16);

        Assert.Equal(new WindowPlacement(expectedX, expectedY, 600, 250), result);
    }

    [Theory]
    [InlineData(-500, -200, 100, 50)]
    [InlineData(2000, 1500, 700, 600)]
    public void OffScreenWindow_IsKeptEntirelyInsideWorkArea(int x, int y, int expectedX, int expectedY)
    {
        var result = DesktopLyricsWindowGeometry.Constrain(new(x, y, 600, 250), WorkArea);

        Assert.Equal(new WindowPlacement(expectedX, expectedY, 600, 250), result);
    }

    [Fact]
    public void FontSizeGrowth_NearBottomMovesWindowUp()
    {
        var result = DesktopLyricsWindowGeometry.Constrain(new(400, 590, 600, 350), WorkArea);

        Assert.Equal(new WindowPlacement(400, 500, 600, 350), result);
    }

    [Fact]
    public void MonitorLeftOfPrimary_UsesNegativeScreenCoordinates()
    {
        var result = DesktopLyricsWindowGeometry.Constrain(new(-612, 440, 600, 250), new(-1920, 0, 1920, 1040), 16);

        Assert.Equal(new WindowPlacement(-600, 440, 600, 250), result);
    }

    [Fact]
    public void OversizedWindow_FitsSmallWorkArea()
    {
        var result = DesktopLyricsWindowGeometry.Constrain(new(450, 800, 2000, 1000), WorkArea);

        Assert.Equal(WorkArea, result);
    }

    [Fact]
    public void AlreadySnappedWindow_RemainsStableOnRepeatedUpdates()
    {
        var first = DesktopLyricsWindowGeometry.Constrain(new(692, 596, 600, 250), WorkArea, 16);

        Assert.Equal(first, DesktopLyricsWindowGeometry.Constrain(first, WorkArea, 16));
    }

    [Theory]
    [InlineData(-1, 762)]
    [InlineData(0, 763)]
    [InlineData(1, 764)]
    [InlineData(127, 890)]
    public void ResizeFromRight_UsesExactPhysicalPixels(int pointerDelta, int expectedWidth)
    {
        var origin = new WindowPlacement(100, 100, 763, 248);
        var resized = DesktopLyricsWindowGeometry.ResizeFromRight(origin, pointerDelta, 525, 1250,
            new WindowPlacement(0, 0, 1920, 1040));

        Assert.Equal(origin with { Width = expectedWidth }, resized);
    }

    [Theory]
    [InlineData(-500, 525)]
    [InlineData(900, 1000)]
    public void ResizeFromRight_ClampsWidthWithoutMovingLeftEdge(int delta, int expectedWidth)
    {
        var origin = new WindowPlacement(300, 120, 760, 240);
        var resized = DesktopLyricsWindowGeometry.ResizeFromRight(origin, delta, 525, 1000,
            new WindowPlacement(0, 0, 1920, 1040));

        Assert.Equal(origin with { Width = expectedWidth }, resized);
    }

    [Fact]
    public void ResizeFromRight_GrowThenShrinkRestoresExactWindow()
    {
        var origin = new WindowPlacement(100, 100, 763, 248);
        var work = new WindowPlacement(0, 0, 1920, 1040);
        var grown = DesktopLyricsWindowGeometry.ResizeFromRight(origin, 200, 525, 1250, work);
        var restored = DesktopLyricsWindowGeometry.ResizeFromRight(grown, -200, 525, 1250, work);

        Assert.Equal(origin, restored);
    }

    [Fact]
    public void ResizeFromRight_SnapsScreenEdgeThenAllowsMovingBack()
    {
        var origin = new WindowPlacement(1200, 120, 600, 240);
        var work = new WindowPlacement(0, 0, 1920, 1040);
        var snapped = DesktopLyricsWindowGeometry.ResizeFromRight(origin, 110, 525, 1250, work, 20);
        var pulledBack = DesktopLyricsWindowGeometry.ResizeFromRight(origin, 50, 525, 1250, work, 20);

        Assert.Equal(origin with { Width = 720 }, snapped);
        Assert.Equal(origin with { Width = 650 }, pulledBack);
    }

    [Fact]
    public void ResizeFromRight_NegativeMonitorCoordinatesKeepHeightAndAnchor()
    {
        var origin = new WindowPlacement(-1200, 100, 760, 248);
        var resized = DesktopLyricsWindowGeometry.ResizeFromRight(origin, 100, 525, 1250,
            new WindowPlacement(-1920, 0, 1920, 1040));

        Assert.Equal(origin with { Width = 860 }, resized);
    }

}
