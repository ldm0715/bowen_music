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

    // ── 裁剪器用的 cover 语义与源矩形 ──────────────────────────────────────

    /// <summary>cover 铺满视口：1.25:1 的视口下，方图会按宽度撑满、上下溢出。</summary>
    [Fact]
    public void CoverMode_FillsTheViewportAndOverflowsTheOtherAxis()
    {
        var viewport = new ImageViewportState { CoverMode = true };
        viewport.Resize(500, 400);      // 1.25:1
        viewport.SetImageSize(1000, 1000);

        Assert.Equal(500, viewport.FittedWidth);
        Assert.Equal(500, viewport.FittedHeight);

        // 溢出的一轴居中：上下各露出 50。
        Assert.Equal(0, viewport.OffsetX);
        Assert.Equal(-50, viewport.OffsetY);
        Assert.True(viewport.CanPan);
    }

    /// <summary>小图也要铺满（cover 允许放大），这是它与 contain 最直观的区别。</summary>
    [Fact]
    public void CoverMode_UpscalesSmallImages()
    {
        var viewport = new ImageViewportState { CoverMode = true };
        viewport.Resize(500, 400);
        viewport.SetImageSize(100, 100);

        Assert.Equal(500, viewport.FittedWidth);
        Assert.Equal(500, viewport.FittedHeight);
    }

    /// <summary>视口矩形换算回源图像素：比例与视口一致，且整体不越出图外。</summary>
    [Fact]
    public void SourceRect_MapsViewportBackToImagePixels()
    {
        var viewport = new ImageViewportState { CoverMode = true };
        viewport.Resize(500, 400);
        viewport.SetImageSize(1000, 1000);

        var rect = viewport.SourceRect();

        // 视口 500×400，缩放 0.5 → 源矩形 1000×800，居中 → y 从 100 起。
        Assert.Equal(0, rect.X);
        Assert.Equal(100, rect.Y);
        Assert.Equal(1000, rect.Width);
        Assert.Equal(800, rect.Height);
    }

    /// <summary>放大后源矩形变小；比例始终跟着视口走。</summary>
    [Fact]
    public void SourceRect_ShrinksWhenZoomedIn()
    {
        var viewport = new ImageViewportState { CoverMode = true };
        viewport.Resize(500, 400);
        viewport.SetImageSize(1000, 1000);
        viewport.ZoomTo(2, 250, 200);

        var rect = viewport.SourceRect();

        Assert.Equal(500, rect.Width);
        Assert.Equal(400, rect.Height);
        Assert.Equal(500d / 400d, rect.Width / rect.Height, 6);
    }

    /// <summary>平移到底再往前拖，源矩形不许越出图外（越界会截到空白）。</summary>
    [Fact]
    public void SourceRect_StaysInsideTheImageWhenPannedToTheEdge()
    {
        var viewport = new ImageViewportState { CoverMode = true };
        viewport.Resize(500, 400);
        viewport.SetImageSize(1000, 1000);
        viewport.ZoomTo(2, 250, 200);

        // 往右下拖到底 → 看到的是图的左上角。
        viewport.PanBy(10_000, 10_000);
        var topLeft = viewport.SourceRect();
        Assert.Equal(0, topLeft.X);
        Assert.Equal(0, topLeft.Y);

        // 往左上拖到底 → 右下角，且右/下边正好贴住图边。
        viewport.PanBy(-10_000, -10_000);
        var bottomRight = viewport.SourceRect();
        Assert.Equal(500, bottomRight.X);
        Assert.Equal(600, bottomRight.Y);
        Assert.Equal(1000, bottomRight.X + bottomRight.Width);
        Assert.Equal(1000, bottomRight.Y + bottomRight.Height);
    }

    /// <summary>图还没装载时给出全零，调用方据此跳过这一次裁剪。</summary>
    [Fact]
    public void SourceRect_IsEmptyBeforeTheImageLoads()
    {
        var viewport = new ImageViewportState();
        viewport.Resize(500, 400);
        Assert.Equal((0d, 0d, 0d, 0d), viewport.SourceRect());
    }
}
