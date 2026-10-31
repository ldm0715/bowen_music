using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace Bowen.WinUI.Controls;

/// <summary>
/// 纯色背景的 HSV 色盘弹窗。歌词页与外壳共用，预览与提示由调用方决定。
/// </summary>
/// <remarks>
/// <para>
/// <b>预览不是固定的。</b> 歌词页要看到歌词在底色上的效果；外壳要看整个界面的缩略图。
/// 调用方传进来一个已经搭好的 <paramref name="preview"/> 和一个只改背景色的回调，
/// 弹窗不关心预览里画的是什么。
/// </para>
/// <para>
/// <b>提示文案也由调用方给。</b> 歌词页永远是白字，深色底才安全；外壳的正文颜色跟随主题，
/// 两种主题下「偏亮」与「偏暗」的含义不同。
/// </para>
/// </remarks>
internal static class ColorPickerDialog
{
    private const double WheelSize = 170;

    /// <summary>
    /// 打开色盘。
    /// </summary>
    /// <param name="current">初始颜色。</param>
    /// <param name="xamlRoot">弹窗挂载的 XamlRoot。</param>
    /// <param name="theme">弹窗主题。</param>
    /// <param name="preview">调用方搭好的预览元素。</param>
    /// <param name="applyPreview">颜色变化时更新预览；只改背景即可。</param>
    /// <param name="hintText">颜色变化时返回对比度提示。</param>
    /// <param name="logger">可选日志。</param>
    /// <returns>确定时返回 <c>0xAARRGGBB</c>；取消返回 <c>null</c>。</returns>
    public static async Task<uint?> ShowAsync(
        Color current,
        XamlRoot xamlRoot,
        ElementTheme theme,
        FrameworkElement preview,
        Action<Color> applyPreview,
        Func<Color, string> hintText,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(xamlRoot);
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(applyPreview);
        ArgumentNullException.ThrowIfNull(hintText);

        // 左侧预览固定成窗口缩略图的比例，不能让它跟着对话框拉伸成一条。
        // 240×170 约 1.41:1，接近主窗口的宽高比。
        preview.Width = 240;
        preview.Height = 170;
        preview.HorizontalAlignment = HorizontalAlignment.Center;
        preview.VerticalAlignment = VerticalAlignment.Center;

        var (hue, saturation, value) = ToHsv(current);

        var wheelImage = new Image
        {
            Source = CreateColorWheelBitmap(256),
            Width = WheelSize,
            Height = WheelSize,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false,
        };
        var marker = new Ellipse
        {
            Width = 14,
            Height = 14,
            Stroke = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)),
            StrokeThickness = 2,
            IsHitTestVisible = false,
        };
        var wheel = new Canvas
        {
            Width = WheelSize,
            Height = WheelSize,
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
        };
        wheel.Children.Add(wheelImage);
        wheel.Children.Add(marker);

        var swatch = new Border
        {
            Width = 56,
            Height = 34,
            CornerRadius = new CornerRadius(6),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0x80, 0x80, 0x80)),
            BorderThickness = new Thickness(1),
        };
        var input = new TextBox
        {
            PlaceholderText = "#RRGGBB、#RGB、#AARRGGBB 或 rgb(12, 34, 56)",
            VerticalAlignment = VerticalAlignment.Center,
        };
        var valueSlider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            StepFrequency = 1,
            Width = 170,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var hint = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromArgb(0xB3, 0x80, 0x80, 0x80)),
        };
        var error = new TextBlock
        {
            FontSize = 12,
            Text = "颜色格式无法识别",
            Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x6B, 0x6B)),
            Visibility = Visibility.Collapsed,
        };

        var valueRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        valueRow.Children.Add(new TextBlock { Text = "明度", VerticalAlignment = VerticalAlignment.Center });
        valueRow.Children.Add(valueSlider);

        var inputRow = new Grid { ColumnSpacing = 8 };
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inputRow.Children.Add(input);
        Grid.SetColumn(swatch, 1);
        inputRow.Children.Add(swatch);

        var pickerRow = new Grid { ColumnSpacing = 12 };
        pickerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pickerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pickerRow.Children.Add(preview);
        Grid.SetColumn(wheel, 1);
        pickerRow.Children.Add(wheel);

        var content = new StackPanel { Spacing = 10, Width = 460 };
        content.Children.Add(pickerRow);
        content.Children.Add(valueRow);
        content.Children.Add(inputRow);
        content.Children.Add(error);
        content.Children.Add(hint);

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "背景颜色",
            Content = content,
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = theme,
        };

        if (Application.Current.Resources.TryGetValue("BowenAcrylicBrush", out var brush) && brush is Brush acrylic)
        {
            dialog.Resources["ContentDialogBackground"] = acrylic;
        }

        var updating = false;
        uint? result = null;

        void UpdateMarker()
        {
            var radius = WheelSize / 2;
            var angle = hue * Math.PI / 180;
            var x = radius + (Math.Cos(angle) * saturation * radius);
            var y = radius + (Math.Sin(angle) * saturation * radius);
            Canvas.SetLeft(marker, x - (marker.Width / 2));
            Canvas.SetTop(marker, y - (marker.Height / 2));
        }

        void ApplyColor(Color color)
        {
            applyPreview(color);
            swatch.Background = new SolidColorBrush(color);
            hint.Text = hintText(color);
            error.Visibility = Visibility.Collapsed;
            dialog.IsPrimaryButtonEnabled = true;
            result = 0xFF000000u | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
        }

        void UpdateFromHsv()
        {
            var color = FromHsv(hue, saturation, value);
            updating = true;
            input.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            valueSlider.Value = value * 100;
            updating = false;

            UpdateMarker();
            ApplyColor(color);
        }

        void PickFromWheel(Point point)
        {
            var radius = WheelSize / 2;
            var dx = point.X - radius;
            var dy = point.Y - radius;
            var distance = Math.Sqrt((dx * dx) + (dy * dy));
            hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
            saturation = Math.Clamp(distance / radius, 0, 1);
            UpdateFromHsv();
        }

        var dragging = false;
        wheel.PointerPressed += (_, e) =>
        {
            dragging = wheel.CapturePointer(e.Pointer);
            PickFromWheel(e.GetCurrentPoint(wheel).Position);
            e.Handled = true;
        };
        wheel.PointerMoved += (_, e) =>
        {
            if (!dragging) return;
            PickFromWheel(e.GetCurrentPoint(wheel).Position);
            e.Handled = true;
        };
        wheel.PointerReleased += (_, e) =>
        {
            if (!dragging) return;
            wheel.ReleasePointerCapture(e.Pointer);
            dragging = false;
        };
        wheel.PointerCanceled += (_, e) =>
        {
            if (!dragging) return;
            wheel.ReleasePointerCapture(e.Pointer);
            dragging = false;
        };

        valueSlider.ValueChanged += (_, e) =>
        {
            if (updating) return;
            value = e.NewValue / 100;
            UpdateFromHsv();
        };

        input.TextChanged += (_, _) =>
        {
            if (updating) return;
            if (!TryParseColor(input.Text, out var color))
            {
                error.Visibility = Visibility.Visible;
                dialog.IsPrimaryButtonEnabled = false;
                result = null;
                return;
            }

            (hue, saturation, value) = ToHsv(color);
            updating = true;
            valueSlider.Value = value * 100;
            updating = false;
            UpdateMarker();
            ApplyColor(color);
        };

        UpdateFromHsv();

        var response = await dialog.ShowAsync();

        // 色盘位图与预览色都是不小的对象，关掉后立即断开引用。
        wheelImage.Source = null;
        wheel.Children.Clear();
        swatch.Background = null;

        return response == ContentDialogResult.Primary ? result : null;
    }

    /// <summary>相对亮度，0..1。供调用方判断底色与正文的对比。</summary>
    internal static double RelativeLuminance(Color color)
        => ((0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B)) / 255;

    /// <summary>生成 HSV 色盘：角度是色相，半径是饱和度，明度固定为 1。</summary>
    private static WriteableBitmap CreateColorWheelBitmap(int size)
    {
        var bitmap = new WriteableBitmap(size, size);
        var pixels = new byte[size * size * 4];
        var radius = size / 2.0;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = x + 0.5 - radius;
                var dy = y + 0.5 - radius;
                var distance = Math.Sqrt((dx * dx) + (dy * dy));
                var index = ((y * size) + x) * 4;

                if (distance > radius)
                {
                    pixels[index + 3] = 0;
                    continue;
                }

                var color = FromHsv(
                    (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360,
                    Math.Clamp(distance / radius, 0, 1),
                    1);
                pixels[index + 0] = color.B;
                pixels[index + 1] = color.G;
                pixels[index + 2] = color.R;
                pixels[index + 3] = 255;
            }
        }

        using (var stream = bitmap.PixelBuffer.AsStream())
        {
            stream.Write(pixels, 0, pixels.Length);
        }

        bitmap.Invalidate();
        return bitmap;
    }

    private static (double Hue, double Saturation, double Value) ToHsv(Color color)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        var hue = 0.0;

        if (delta > 0)
        {
            if (max == r) hue = 60 * (((g - b) / delta) % 6);
            else if (max == g) hue = 60 * (((b - r) / delta) + 2);
            else hue = 60 * (((r - g) / delta) + 4);
        }

        if (hue < 0) hue += 360;

        return (hue, max <= 0 ? 0 : delta / max, max);
    }

    private static Color FromHsv(double hue, double saturation, double value)
    {
        var chroma = value * saturation;
        var section = hue / 60;
        var x = chroma * (1 - Math.Abs((section % 2) - 1));
        var m = value - chroma;

        var (r, g, b) = section switch
        {
            < 1 => (chroma, x, 0.0),
            < 2 => (x, chroma, 0.0),
            < 3 => (0.0, chroma, x),
            < 4 => (0.0, x, chroma),
            < 5 => (x, 0.0, chroma),
            _ => (chroma, 0.0, x),
        };

        return Color.FromArgb(
            255,
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    /// <summary>接受 #RGB / #RRGGBB / #AARRGGBB，以及 rgb(r,g,b) 或 r,g,b。</summary>
    private static bool TryParseColor(string? text, out Color color)
    {
        color = default;
        text = text?.Trim();
        if (string.IsNullOrEmpty(text)) return false;

        if (text[0] == '#')
        {
            var hex = text[1..];
            if (hex.Length == 3)
            {
                hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
            }

            if (hex.Length == 6
                && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
            {
                color = Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
                return true;
            }

            if (hex.Length == 8
                && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var argb))
            {
                color = Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
                return true;
            }

            return false;
        }

        var body = text;
        if (body.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase) && body.EndsWith(')'))
        {
            body = body[4..^1];
        }

        var parts = body.Split([',', ' ', ';', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3
            || !byte.TryParse(parts[0], out var r)
            || !byte.TryParse(parts[1], out var g)
            || !byte.TryParse(parts[2], out var b))
        {
            return false;
        }

        color = Color.FromArgb(255, r, g, b);
        return true;
    }
}
