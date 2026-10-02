namespace Bodian.Core.Media;

/// <summary>图片查看的缩放与平移状态；坐标在固定视口内，输入更新不触发布局。</summary>
public sealed class ImageViewportState
{
    public const double MinZoom = 0.25;
    public const double MaxZoom = 5;
    private double _imageWidth;
    private double _imageHeight;

    public double ViewportWidth { get; private set; }
    public double ViewportHeight { get; private set; }
    public double FittedWidth { get; private set; }
    public double FittedHeight { get; private set; }
    public double Zoom { get; private set; } = 1;
    public double OffsetX { get; private set; }
    public double OffsetY { get; private set; }
    public bool CanPan => FittedWidth * Zoom > ViewportWidth + 0.5 || FittedHeight * Zoom > ViewportHeight + 0.5;

    public void Reset()
    {
        _imageWidth = _imageHeight = FittedWidth = FittedHeight = OffsetX = OffsetY = 0;
        Zoom = 1;
    }

    public void SetImageSize(double width, double height)
    {
        ValidateSize(width, nameof(width), positive: true);
        ValidateSize(height, nameof(height), positive: true);
        _imageWidth = width;
        _imageHeight = height;
        Fit();
    }

    public void Resize(double width, double height)
    {
        ValidateSize(width, nameof(width));
        ValidateSize(height, nameof(height));
        var normalizedX = FittedWidth > 0 ? (ViewportWidth / 2 - OffsetX) / (FittedWidth * Zoom) : 0.5;
        var normalizedY = FittedHeight > 0 ? (ViewportHeight / 2 - OffsetY) / (FittedHeight * Zoom) : 0.5;
        ViewportWidth = width;
        ViewportHeight = height;
        UpdateFitSize();
        PanTo(width / 2 - normalizedX * FittedWidth * Zoom, height / 2 - normalizedY * FittedHeight * Zoom);
    }

    public void Fit()
    {
        Zoom = 1;
        UpdateFitSize();
        PanTo((ViewportWidth - FittedWidth) / 2, (ViewportHeight - FittedHeight) / 2);
    }

    public void ZoomTo(double zoom, double anchorX, double anchorY)
    {
        ValidateFinite(zoom, nameof(zoom));
        ValidateFinite(anchorX, nameof(anchorX));
        ValidateFinite(anchorY, nameof(anchorY));
        var imageX = (anchorX - OffsetX) / Zoom;
        var imageY = (anchorY - OffsetY) / Zoom;
        Zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        PanTo(anchorX - imageX * Zoom, anchorY - imageY * Zoom);
    }

    public void PanTo(double x, double y)
    {
        ValidateFinite(x, nameof(x));
        ValidateFinite(y, nameof(y));
        OffsetX = ClampOffset(x, FittedWidth * Zoom, ViewportWidth);
        OffsetY = ClampOffset(y, FittedHeight * Zoom, ViewportHeight);
    }

    public void PanBy(double x, double y) => PanTo(OffsetX + x, OffsetY + y);

    private void UpdateFitSize()
    {
        if (_imageWidth <= 0 || _imageHeight <= 0 || ViewportWidth <= 0 || ViewportHeight <= 0)
        {
            FittedWidth = FittedHeight = 0;
            return;
        }
        var scale = Math.Min(1, Math.Min(Math.Max(1, ViewportWidth - 20) / _imageWidth,
            Math.Max(1, ViewportHeight - 20) / _imageHeight));
        FittedWidth = _imageWidth * scale;
        FittedHeight = _imageHeight * scale;
    }

    private static double ClampOffset(double value, double size, double viewport) => size <= viewport
        ? (viewport - size) / 2 : Math.Clamp(value, viewport - size, 0);

    private static void ValidateSize(double value, string name, bool positive = false)
    {
        ValidateFinite(value, name);
        if (value < 0 || (positive && value == 0)) throw new ArgumentOutOfRangeException(name);
    }

    private static void ValidateFinite(double value, string name)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(name);
    }
}
