using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace Bowen.WinUI.Controls;

/// <summary>
/// 可嵌入的 HSV 色盘。
/// </summary>
/// <remarks>
/// <para>
/// 从 <see cref="ColorPickerDialog"/> 里拆出来，是为了能在海报对话框（本身是 ContentDialog）
/// 之上用 Flyout 打开 —— WinUI 不允许同时开两个 ContentDialog。
/// </para>
/// <para>
/// 预览元素与提示文案仍由调用方提供；面板只负责色轮、明度、十六进制输入与有效性。
/// </para>
/// </remarks>
internal sealed class ColorPickerPanel : IDisposable
{
    private const double WheelSize = 170;

    private readonly Image _wheelImage;
    private readonly Canvas _wheel;
    private readonly Border _swatch;
    private readonly TextBox _input;
    private readonly Slider _valueSlider;
    private readonly TextBlock _hint;
    private readonly TextBlock _error;
    private readonly Action<Color> _applyPreview;
    private readonly Func<Color, string> _hintText;

    private double _hue;
    private double _saturation;
    private double _value;
    private bool _updating;
    private uint? _result;

    public ColorPickerPanel(
        Color current,
        FrameworkElement preview,
        Action<Color> applyPreview,
        Func<Color, string> hintText,
        double previewWidth = 240,
        double previewHeight = 170)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(applyPreview);
        ArgumentNullException.ThrowIfNull(hintText);

        _applyPreview = applyPreview;
        _hintText = hintText;

        preview.Width = previewWidth;
        preview.Height = previewHeight;
        preview.HorizontalAlignment = HorizontalAlignment.Center;
        preview.VerticalAlignment = VerticalAlignment.Center;

        (_hue, _saturation, _value) = ToHsv(current);

        _wheelImage = new Image
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

        _wheel = new Canvas
        {
            Width = WheelSize,
            Height = WheelSize,
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
        };
        _wheel.Children.Add(_wheelImage);
        _wheel.Children.Add(marker);

        _swatch = new Border
        {
            Width = 56,
            Height = 34,
            CornerRadius = new CornerRadius(6),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0x80, 0x80, 0x80)),
            BorderThickness = new Thickness(1),
        };

        _input = new TextBox
        {
            PlaceholderText = "#RRGGBB、#RGB、#AARRGGBB 或 rgb(12, 34, 56)",
            VerticalAlignment = VerticalAlignment.Center,
        };

        _valueSlider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            StepFrequency = 1,
            Width = 170,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _hint = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromArgb(0xB3, 0x80, 0x80, 0x80)),
        };

        _error = new TextBlock
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
        valueRow.Children.Add(_valueSlider);

        var inputRow = new Grid { ColumnSpacing = 8 };
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inputRow.Children.Add(_input);
        Grid.SetColumn(_swatch, 1);
        inputRow.Children.Add(_swatch);

        var pickerRow = new Grid { ColumnSpacing = 12 };
        pickerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pickerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pickerRow.Children.Add(preview);
        Grid.SetColumn(_wheel, 1);
        pickerRow.Children.Add(_wheel);

        var content = new StackPanel { Spacing = 10, Width = 460 };
        content.Children.Add(pickerRow);
        content.Children.Add(valueRow);
        content.Children.Add(inputRow);
        content.Children.Add(_error);
        content.Children.Add(_hint);
        Content = content;

        void UpdateMarker()
        {
            var radius = WheelSize / 2;
            var angle = _hue * Math.PI / 180;
            var x = radius + (Math.Cos(angle) * _saturation * radius);
            var y = radius + (Math.Sin(angle) * _saturation * radius);
            Canvas.SetLeft(marker, x - (marker.Width / 2));
            Canvas.SetTop(marker, y - (marker.Height / 2));
        }

        void ApplyColor(Color color)
        {
            _applyPreview(color);
            _swatch.Background = new SolidColorBrush(color);
            _hint.Text = _hintText(color);
            _error.Visibility = Visibility.Collapsed;
            SetResult(0xFF000000u | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B);
        }

        void UpdateFromHsv()
        {
            var color = FromHsv(_hue, _saturation, _value);
            _updating = true;
            _input.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            _valueSlider.Value = _value * 100;
            _updating = false;

            UpdateMarker();
            ApplyColor(color);
        }

        void PickFromWheel(Point point)
        {
            var radius = WheelSize / 2;
            var dx = point.X - radius;
            var dy = point.Y - radius;
            var distance = Math.Sqrt((dx * dx) + (dy * dy));
            _hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
            _saturation = Math.Clamp(distance / radius, 0, 1);
            UpdateFromHsv();
        }

        var dragging = false;
        _wheel.PointerPressed += (_, e) =>
        {
            dragging = _wheel.CapturePointer(e.Pointer);
            PickFromWheel(e.GetCurrentPoint(_wheel).Position);
            e.Handled = true;
        };
        _wheel.PointerMoved += (_, e) =>
        {
            if (!dragging) return;
            PickFromWheel(e.GetCurrentPoint(_wheel).Position);
            e.Handled = true;
        };
        _wheel.PointerReleased += (_, e) =>
        {
            if (!dragging) return;
            _wheel.ReleasePointerCapture(e.Pointer);
            dragging = false;
        };
        _wheel.PointerCanceled += (_, e) =>
        {
            if (!dragging) return;
            _wheel.ReleasePointerCapture(e.Pointer);
            dragging = false;
        };

        _valueSlider.ValueChanged += (_, e) =>
        {
            if (_updating) return;
            _value = e.NewValue / 100;
            UpdateFromHsv();
        };

        _input.TextChanged += (_, _) =>
        {
            if (_updating) return;
            if (!TryParseColor(_input.Text, out var color))
            {
                _error.Visibility = Visibility.Visible;
                SetResult(null);
                return;
            }

            (_hue, _saturation, _value) = ToHsv(color);
            _updating = true;
            _valueSlider.Value = _value * 100;
            _updating = false;
            UpdateMarker();
            ApplyColor(color);
        };

        UpdateFromHsv();
    }

    /// <summary>面板根元素。宿主负责把它放进 ContentDialog 或 Flyout。</summary>
    public FrameworkElement Content { get; }

    /// <summary>当前有效颜色；输入非法时为 <c>null</c>。</summary>
    public uint? Result => _result;

    public bool IsValid => _result.HasValue;

    /// <summary>有效性变化。宿主据此启用 / 禁用「确定」。</summary>
    public event EventHandler? ValidityChanged;

    public void Dispose()
    {
        _wheelImage.Source = null;
        _wheel.Children.Clear();
        _swatch.Background = null;
    }

    private void SetResult(uint? value)
    {
        var wasValid = IsValid;
        _result = value;

        if (wasValid != IsValid)
        {
            ValidityChanged?.Invoke(this, EventArgs.Empty);
        }
    }

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
