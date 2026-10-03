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

    /// <summary>
    /// cover 语义：把图铺满视口、不留白，代价是可能裁掉一边。
    /// </summary>
    /// <remarks>
    /// <b>裁剪器专用</b>。默认是 contain 语义（整张图都看得见、可能留白）——
    /// 图片查看器要的正是那个，所以这是一个显式开关而不是把默认改掉。
    /// 打开后 <see cref="Fit"/> 与 <see cref="Resize"/> 都按 cover 计算。
    /// </remarks>
    public bool CoverMode { get; set; }

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

    /// <summary>
    /// 当前视口对应的**源图像素**矩形。裁剪器用它反算要截哪一块。
    /// </summary>
    /// <remarks>
    /// 视口只有一个缩放系数（<c>FittedWidth * Zoom / _imageWidth</c>），宽高比因此与视口一致 ——
    /// 裁剪器把视口做成目标比例，截出来的矩形就自然是那个比例。
    /// 图片还没装载时返回全零。
    /// </remarks>
    public (double X, double Y, double Width, double Height) SourceRect()
    {
        if (_imageWidth <= 0 || _imageHeight <= 0 || FittedWidth <= 0 || ViewportWidth <= 0 || ViewportHeight <= 0)
        {
            return (0, 0, 0, 0);
        }

        var scale = FittedWidth * Zoom / _imageWidth;
        var width = Math.Min(ViewportWidth / scale, _imageWidth);
        var height = Math.Min(ViewportHeight / scale, _imageHeight);

        // 平移越界时钳回图内，避免截到图外的空白。
        var x = Math.Clamp(-OffsetX / scale, 0, _imageWidth - width);
        var y = Math.Clamp(-OffsetY / scale, 0, _imageHeight - height);

        return (x, y, width, height);
    }

    private void UpdateFitSize()
    {
        if (_imageWidth <= 0 || _imageHeight <= 0 || ViewportWidth <= 0 || ViewportHeight <= 0)
        {
            FittedWidth = FittedHeight = 0;
            return;
        }

        double scale;

        if (CoverMode)
        {
            // 铺满：取较大的那个比例，短边因此会溢出视口 —— 这正是「可裁」的来源。
            scale = Math.Max(ViewportWidth / _imageWidth, ViewportHeight / _imageHeight);
        }
        else
        {
            scale = Math.Min(1, Math.Min(Math.Max(1, ViewportWidth - 20) / _imageWidth,
                Math.Max(1, ViewportHeight - 20) / _imageHeight));
        }

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
