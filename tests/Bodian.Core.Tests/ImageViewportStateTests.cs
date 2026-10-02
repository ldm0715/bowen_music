using Bodian.Core.Media;
using Xunit;

namespace Bodian.Core.Tests;

public sealed class ImageViewportStateTests
{
    private static ImageViewportState Square()
    {
        var viewport = new ImageViewportState();
        viewport.Resize(420, 420);
        viewport.SetImageSize(1000, 1000);
        return viewport;
    }

    [Fact]
    public void Fit_CentersTheImageWithoutUpscalingSmallImages()
    {
        var viewport = Square();
        Assert.Equal(400, viewport.FittedWidth);
        Assert.Equal(400, viewport.FittedHeight);
        Assert.Equal(10, viewport.OffsetX);
        Assert.Equal(10, viewport.OffsetY);
        Assert.False(viewport.CanPan);
        viewport.SetImageSize(100, 50);
        Assert.Equal(100, viewport.FittedWidth);
        Assert.Equal(50, viewport.FittedHeight);
        Assert.Equal(160, viewport.OffsetX);
        Assert.Equal(185, viewport.OffsetY);
    }

    [Fact]
    public void ZoomAroundTheCenter_AllowsDraggingInEitherDirection()
    {
        var viewport = Square();
        viewport.ZoomTo(2, 210, 210);
        Assert.Equal(-190, viewport.OffsetX);
        Assert.Equal(-190, viewport.OffsetY);
        Assert.True(viewport.CanPan);
        viewport.PanBy(60, -40);
        Assert.Equal(-130, viewport.OffsetX);
        Assert.Equal(-230, viewport.OffsetY);
        Assert.Equal(400, viewport.FittedWidth);
        Assert.Equal(400, viewport.FittedHeight);
    }

    [Fact]
    public void Zoom_KeepsTheImagePointUnderThePointer()
    {
        var viewport = Square();
        var imageX = (120 - viewport.OffsetX) / viewport.Zoom;
        var imageY = (130 - viewport.OffsetY) / viewport.Zoom;
        viewport.ZoomTo(2.5, 120, 130);
        Assert.Equal(imageX, (120 - viewport.OffsetX) / viewport.Zoom, 10);
        Assert.Equal(imageY, (130 - viewport.OffsetY) / viewport.Zoom, 10);
    }

    [Fact]
    public void Pan_ClampsAllEdgesAndRecentersAxesThatFit()
    {
        var viewport = Square();
        viewport.ZoomTo(2, 210, 210);
        viewport.PanTo(10000, 10000);
        Assert.Equal(0, viewport.OffsetX);
        Assert.Equal(0, viewport.OffsetY);
        viewport.PanTo(-10000, -10000);
        Assert.Equal(-380, viewport.OffsetX);
        Assert.Equal(-380, viewport.OffsetY);
        viewport.SetImageSize(1000, 100);
        viewport.ZoomTo(2, 210, 210);
        viewport.PanTo(-10000, -10000);
        Assert.Equal(-380, viewport.OffsetX);
        Assert.Equal(170, viewport.OffsetY);
    }

    [Fact]
    public void RepeatedDragging_DoesNotChangeImageSizeOrZoom()
    {
        var viewport = Square();
        viewport.ZoomTo(3, 210, 210);
        for (var i = 0; i < 10000; i++) viewport.PanBy(i % 2 == 0 ? 0.25 : -0.25, i % 2 == 0 ? -0.5 : 0.5);
        Assert.Equal(400, viewport.FittedWidth);
        Assert.Equal(400, viewport.FittedHeight);
        Assert.Equal(3, viewport.Zoom);
        Assert.Equal(-390, viewport.OffsetX, 10);
        Assert.Equal(-390, viewport.OffsetY, 10);
    }

    [Fact]
    public void Resize_PreservesZoomAndTheVisibleImageCenter()
    {
        var viewport = Square();
        viewport.ZoomTo(2, 210, 210);
        viewport.PanBy(35, -20);
        var x = (210 - viewport.OffsetX) / (viewport.FittedWidth * viewport.Zoom);
        var y = (210 - viewport.OffsetY) / (viewport.FittedHeight * viewport.Zoom);
        viewport.Resize(320, 320);
        Assert.Equal(2, viewport.Zoom);
        Assert.Equal(x, (160 - viewport.OffsetX) / (viewport.FittedWidth * viewport.Zoom), 10);
        Assert.Equal(y, (160 - viewport.OffsetY) / (viewport.FittedHeight * viewport.Zoom), 10);
    }

    [Theory]
    [InlineData(-100, ImageViewportState.MinZoom)]
    [InlineData(100, ImageViewportState.MaxZoom)]
    public void Zoom_IsBounded(double requested, double expected)
    {
        var viewport = Square();
        viewport.ZoomTo(requested, 210, 210);
        Assert.Equal(expected, viewport.Zoom);
    }

    [Fact]
    public void LoadingBeforeLayout_CanFitAfterViewportBecomesAvailable()
    {
        var viewport = new ImageViewportState();
        viewport.SetImageSize(1000, 1000);
        Assert.Equal(0, viewport.FittedWidth);
        viewport.Resize(420, 420);
        Assert.Equal(400, viewport.FittedWidth);
        Assert.Equal(10, viewport.OffsetX);
        viewport.Reset();
        Assert.False(viewport.CanPan);
        Assert.Equal(1, viewport.Zoom);
        viewport.SetImageSize(100, 50);
        Assert.Equal(100, viewport.FittedWidth);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidInput_IsRejectedWithoutCorruptingTheTransform(double value)
    {
        var viewport = Square();
        Assert.Throws<ArgumentOutOfRangeException>(() => viewport.ZoomTo(value, 210, 210));
        Assert.Throws<ArgumentOutOfRangeException>(() => viewport.Resize(value, 420));
        Assert.Throws<ArgumentOutOfRangeException>(() => viewport.PanTo(value, 0));
        Assert.Equal(1, viewport.Zoom);
        Assert.Equal(400, viewport.FittedWidth);
        Assert.Equal(10, viewport.OffsetX);
    }
}
