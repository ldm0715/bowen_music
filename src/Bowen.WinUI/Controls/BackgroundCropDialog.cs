using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.UI;

namespace Bowen.WinUI.Controls;

/// <summary>
/// 背景图片裁剪弹窗：拖动/缩放后返回保留区域的中心与缩放。
/// </summary>
/// <remarks>
/// <para>
/// <b>歌词页与外壳共用。</b> 两边只是目标宽高比不同：歌词页取背景层的比例，
/// 外壳取窗口内容区的比例。取景算法与交互完全一致，没必要各写一份。
/// </para>
/// <para>
/// <b>预览比例必须与最终渲染比例一致。</b> 否则用户在这里框住的区域，落到背景上会
/// 因为 <c>UniformToFill</c> 再裁一次，构图对不上。调用方传进来的
/// <paramref name="targetAspect"/> 就是最终背景的宽高比。
/// </para>
/// </remarks>
internal static class BackgroundCropDialog
{
    /// <summary>预览的最大宽度（DIP）。</summary>
    private const double PreviewWidth = 480;

    /// <summary>预览高度的上下限（DIP）。超出时按比例回缩。</summary>
    private const double MinimumPreviewHeight = 200;

    private const double MaximumPreviewHeight = 360;

    /// <summary>
    /// 打开裁剪弹窗。
    /// </summary>
    /// <param name="path">已导入到应用目录的图片路径。</param>
    /// <param name="targetAspect">最终背景的宽高比（宽 / 高）。</param>
    /// <param name="focusX">初始水平焦点，0–1。</param>
    /// <param name="focusY">初始垂直焦点，0–1。</param>
    /// <param name="zoom">初始缩放，1–3。</param>
    /// <param name="xamlRoot">弹窗挂载的 XamlRoot。</param>
    /// <param name="theme">弹窗主题；它不在 ShellRoot 的 RequestedTheme 子树里，必须显式给。</param>
    /// <param name="logger">可选日志。</param>
    /// <returns>确定时返回焦点与缩放；取消或加载失败返回 <c>null</c>。</returns>
    public static async Task<(double FocusX, double FocusY, double Zoom)?> ShowAsync(
        string path,
        double targetAspect,
        double focusX,
        double focusY,
        double zoom,
        XamlRoot xamlRoot,
        ElementTheme theme,
        ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(xamlRoot);

        if (!double.IsFinite(targetAspect) || targetAspect <= 0)
        {
            targetAspect = 16.0 / 9;
        }

        double imageWidth;
        double imageHeight;
        BitmapImage bitmap;

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var decodeWidth = Math.Min(1600, decoder.PixelWidth);
            imageWidth = decodeWidth;
            imageHeight = decoder.PixelHeight * (decodeWidth / (double)decoder.PixelWidth);
            bitmap = new BitmapImage
            {
                DecodePixelWidth = (int)decodeWidth,
                UriSource = new Uri(path, UriKind.Absolute),
            };
        }
        catch (Exception exception)
        {
            logger?.LogWarning(exception, "背景图片裁剪预览加载失败：{Path}", path);

            return null;
        }

        var previewWidth = PreviewWidth;
        var previewHeight = previewWidth / targetAspect;

        if (previewHeight > MaximumPreviewHeight)
        {
            previewHeight = MaximumPreviewHeight;
            previewWidth = previewHeight * targetAspect;
        }
        else if (previewHeight < MinimumPreviewHeight)
        {
            previewHeight = MinimumPreviewHeight;
            previewWidth = previewHeight * targetAspect;
        }

        var image = new Image
        {
            Source = bitmap,
            Width = imageWidth,
            Height = imageHeight,
            Stretch = Stretch.Fill,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            RenderTransformOrigin = new Point(0, 0),
        };
        var transform = new CompositeTransform();
        image.RenderTransform = transform;

        var preview = new Canvas
        {
            Width = previewWidth,
            Height = previewHeight,
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(0xFF, 0, 0, 0)),
            Clip = new RectangleGeometry { Rect = new Rect(0, 0, previewWidth, previewHeight) },
        };
        // Canvas 不约束子元素尺寸；换成 Border 会把放大的图片压回预览大小，再缩放就只剩一小块。
        preview.Children.Add(image);

        // 100% 必须正好铺满目标区域。这里不要额外放大：预览的基准与最终背景的
        // UniformToFill 必须完全相同，否则用户在这里框住的区域，落到背景上会差一截。
        var baseScale = Math.Max(previewWidth / imageWidth, previewHeight / imageHeight);
        var currentZoom = Math.Clamp(zoom, 1, 3);
        var scale = baseScale * currentZoom;
        var fx = Math.Clamp(focusX, 0, 1);
        var fy = Math.Clamp(focusY, 0, 1);
        double offsetX = 0;
        double offsetY = 0;

        void ApplyOffsets()
        {
            offsetX = Math.Clamp(offsetX, previewWidth - (imageWidth * scale), 0);
            offsetY = Math.Clamp(offsetY, previewHeight - (imageHeight * scale), 0);
            transform.ScaleX = transform.ScaleY = scale;
            transform.TranslateX = offsetX;
            transform.TranslateY = offsetY;
        }

        void CenterOnFocus()
        {
            offsetX = (previewWidth / 2) - (fx * imageWidth * scale);
            offsetY = (previewHeight / 2) - (fy * imageHeight * scale);
            ApplyOffsets();
        }

        void FocusFromOffsets()
        {
            fx = Math.Clamp(((previewWidth / 2) - offsetX) / (imageWidth * scale), 0, 1);
            fy = Math.Clamp(((previewHeight / 2) - offsetY) / (imageHeight * scale), 0, 1);
        }

        CenterOnFocus();

        var dragging = false;
        Point dragStart = default;
        double dragOffsetX = 0;
        double dragOffsetY = 0;

        preview.PointerPressed += (_, e) =>
        {
            dragging = preview.CapturePointer(e.Pointer);
            dragStart = e.GetCurrentPoint(preview).Position;
            dragOffsetX = offsetX;
            dragOffsetY = offsetY;
        };
        preview.PointerMoved += (_, e) =>
        {
            if (!dragging) return;
            var point = e.GetCurrentPoint(preview).Position;
            offsetX = dragOffsetX + (point.X - dragStart.X);
            offsetY = dragOffsetY + (point.Y - dragStart.Y);
            ApplyOffsets();
            FocusFromOffsets();
        };
        preview.PointerReleased += (_, e) =>
        {
            if (!dragging) return;
            preview.ReleasePointerCapture(e.Pointer);
            dragging = false;
        };
        preview.PointerCanceled += (_, e) =>
        {
            if (!dragging) return;
            preview.ReleasePointerCapture(e.Pointer);
            dragging = false;
        };

        var syncingZoom = false;
        var zoomSlider = new Slider
        {
            Minimum = 100,
            Maximum = 300,
            StepFrequency = 1,
            Value = currentZoom * 100,
            Width = 320,
            VerticalAlignment = VerticalAlignment.Center,
        };
        zoomSlider.ValueChanged += (_, e) =>
        {
            if (syncingZoom) return;
            currentZoom = Math.Clamp(e.NewValue / 100, 1, 3);
            scale = baseScale * currentZoom;
            CenterOnFocus();
        };

        void ZoomAt(double target, double anchorX, double anchorY)
        {
            target = Math.Clamp(target, 1, 3);
            if (Math.Abs(target - currentZoom) < 0.001) return;

            // 以鼠标位置为锚点：缩放前后，指针下的同一个图像点保持不动。
            var imageX = (anchorX - offsetX) / scale;
            var imageY = (anchorY - offsetY) / scale;
            currentZoom = target;
            scale = baseScale * currentZoom;
            offsetX = anchorX - (imageX * scale);
            offsetY = anchorY - (imageY * scale);
            ApplyOffsets();
            FocusFromOffsets();

            // 滚轮与滑杆共用同一份缩放值；同步滑杆时不要再次触发居中。
            syncingZoom = true;
            zoomSlider.Value = currentZoom * 100;
            syncingZoom = false;
        }

        preview.PointerWheelChanged += (_, e) =>
        {
            var point = e.GetCurrentPoint(preview);
            var delta = point.Properties.MouseWheelDelta;
            if (delta == 0) return;

            ZoomAt(currentZoom * Math.Pow(1.1, delta / 120.0), point.Position.X, point.Position.Y);
            e.Handled = true;
        };

        var hint = new TextBlock
        {
            Text = "拖动图片选择保留区域，滚轮或缩放滑杆可以进一步裁切。",
            FontSize = 12,
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var zoomRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        zoomRow.Children.Add(new TextBlock { Text = "缩放", VerticalAlignment = VerticalAlignment.Center });
        zoomRow.Children.Add(zoomSlider);

        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(preview);
        content.Children.Add(hint);
        content.Children.Add(zoomRow);

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "裁剪背景图片",
            Content = content,
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            // 弹窗挂在 XamlRoot 下，不在 ShellRoot 的 RequestedTheme 子树里，必须显式跟主题。
            RequestedTheme = theme,
        };

        // ContentDialog 的模板用主题资源画底，直接设 Background 会被模板覆盖，改资源键。
        if (Application.Current.Resources.TryGetValue("BowenAcrylicBrush", out var brush) && brush is Brush acrylic)
        {
            dialog.Resources["ContentDialogBackground"] = acrylic;
        }

        var result = await dialog.ShowAsync();

        // 预览用的 BitmapImage 解码位图不小；弹窗关闭后立即断开引用，不等 GC。
        image.Source = null;
        preview.Children.Clear();

        if (result != ContentDialogResult.Primary)
        {
            return null;
        }

        FocusFromOffsets();
        return (fx, fy, currentZoom);
    }
}
