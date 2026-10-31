using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using Bowen.WinUI.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Bowen.WinUI.Controls;

/// <summary>
/// 外壳自定义图片背景：一次性把取景、模糊算成一张位图，再由普通 Image 铺满。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么不常驻 Win2D 合成表面。</b> 外壳背景是静态的；常驻的合成表面、
/// 模糊效果链与共享设备缓存会在不播放、不进歌词页时也占着内存。
/// 这里只在图片/模糊/焦点/窗口尺寸变化时渲染一次，之后只剩一张位图。
/// </para>
/// <para>
/// <b>为什么不会露缝。</b> 不再手写 Image 的 Width/Height/位移，也不在控件内部裁剪；
/// <see cref="Stretch.UniformToFill"/> 保证任何窗口比例下都铺满，多出的部分被父层裁掉。
/// </para>
/// </remarks>
public sealed partial class ShellImageBackdrop : UserControl
{
    /// <summary>源图与输出位图的最长边。背景本身柔和，1280 足够。</summary>
    private const int MaxEdge = 1280;

    private static readonly TimeSpan RenderDebounce = TimeSpan.FromMilliseconds(150);

    private readonly ILogger<ShellImageBackdrop>? _logger;
    private readonly DispatcherQueueTimer _renderTimer;

    private string _path = string.Empty;
    private double _blur;
    private double _focusX = 0.5;
    private double _focusY = 0.5;
    private double _zoom = 1;
    private int _renderVersion;
    private bool _loaded;

    public ShellImageBackdrop()
    {
        InitializeComponent();

        _logger = (Application.Current.Resources["BowenLoggerFactory"] as ILoggerFactory)
            ?.CreateLogger<ShellImageBackdrop>();

        _renderTimer = DispatcherQueue.CreateTimer();
        _renderTimer.Interval = RenderDebounce;
        _renderTimer.IsRepeating = false;
        _renderTimer.Tick += (_, _) =>
        {
            _renderTimer.Stop();
            _ = RenderAsync();
        };

        Loaded += (_, _) =>
        {
            _loaded = true;
            _ = RenderAsync();
        };
        Unloaded += (_, _) =>
        {
            _loaded = false;
            _renderVersion++;
            _renderTimer.Stop();
            Picture.Source = null;
        };
        SizeChanged += (_, _) => ScheduleRender();
    }

    /// <summary>设置图片与取景参数。参数变化时重新渲染；路径不变也会更新取景。</summary>
    public void SetImage(string path, double blur, double focusX, double focusY, double zoom)
    {
        path ??= string.Empty;
        blur = double.IsFinite(blur) ? Math.Clamp(blur, 0, 40) : 16;
        focusX = double.IsFinite(focusX) ? Math.Clamp(focusX, 0, 1) : 0.5;
        focusY = double.IsFinite(focusY) ? Math.Clamp(focusY, 0, 1) : 0.5;
        zoom = double.IsFinite(zoom) ? Math.Clamp(zoom, 1, 3) : 1;

        if (string.Equals(_path, path, StringComparison.Ordinal)
            && Math.Abs(_blur - blur) < 0.01
            && Math.Abs(_focusX - focusX) < 0.001
            && Math.Abs(_focusY - focusY) < 0.001
            && Math.Abs(_zoom - zoom) < 0.001)
        {
            return;
        }

        _path = path;
        _blur = blur;
        _focusX = focusX;
        _focusY = focusY;
        _zoom = zoom;

        // 首次显示立即出图，其余变化（模糊滑杆等）防抖，避免一拖就连续整窗渲染。
        if (Picture.Source is null)
        {
            _ = RenderAsync();
        }
        else
        {
            ScheduleRender();
        }
    }

    /// <summary>清空图片并断开位图引用。</summary>
    public void Clear()
    {
        _renderVersion++;
        _path = string.Empty;
        Picture.Source = null;
        _renderTimer.Stop();
    }

    private void ScheduleRender()
    {
        _renderTimer.Stop();
        _renderTimer.Start();
    }

    private async Task RenderAsync()
    {
        if (!_loaded || _path.Length == 0)
        {
            return;
        }

        var width = ActualWidth;
        var height = ActualHeight;

        if (width <= 0 || height <= 0)
        {
            return;
        }

        var version = ++_renderVersion;

        // 输出位图的长边也压到 1280：模糊/背景不需要更高分辨率。
        var outputScale = Math.Min(1, MaxEdge / Math.Max(width, height));
        var outputWidth = Math.Max(1, (int)Math.Round(width * outputScale));
        var outputHeight = Math.Max(1, (int)Math.Round(height * outputScale));

        try
        {
            using var lease = BackgroundCanvasDeviceLease.Acquire();
            var device = lease.Device;

            using var source = await LoadSourceAsync(device, _path);
            if (source is null || version != _renderVersion)
            {
                return;
            }

            using var target = new CanvasRenderTarget(device, outputWidth, outputHeight, 96);

            var scale = Math.Max((double)outputWidth / source.Size.Width, (double)outputHeight / source.Size.Height) * _zoom;
            var offsetX = (outputWidth / 2.0) - (_focusX * source.Size.Width * scale);
            var offsetY = (outputHeight / 2.0) - (_focusY * source.Size.Height * scale);
            offsetX = Math.Clamp(offsetX, outputWidth - (source.Size.Width * scale), 0);
            offsetY = Math.Clamp(offsetY, outputHeight - (source.Size.Height * scale), 0);

            using var blur = new GaussianBlurEffect
            {
                Source = source,
                BlurAmount = (float)(_blur * outputScale),
            };

            using (var session = target.CreateDrawingSession())
            {
                session.Transform = Matrix3x2.CreateScale((float)scale)
                    * Matrix3x2.CreateTranslation((float)offsetX, (float)offsetY);
                session.DrawImage(blur);
            }

            using var stream = new InMemoryRandomAccessStream();
            await target.SaveAsync(stream, CanvasBitmapFileFormat.Png);

            if (version != _renderVersion)
            {
                return;
            }

            stream.Seek(0);
            var image = new BitmapImage();
            await image.SetSourceAsync(stream);

            if (version != _renderVersion)
            {
                return;
            }

            Picture.Source = image;
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "外壳背景图片渲染失败：{Path}", _path);
        }
        finally
        {
            // 一次渲染结束后把驱动侧缓存还回去；静止时只保留 Picture 上那张位图。
            BackgroundCanvasDeviceLease.TrimShared();
        }
    }

    /// <summary>按最长边 1280 直接解码本地图片，避免先载入 4K 原图。</summary>
    private static async Task<CanvasBitmap?> LoadSourceAsync(CanvasDevice device, string path)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        using var stream = await file.OpenReadAsync();
        var decoder = await BitmapDecoder.CreateAsync(stream);

        var longest = Math.Max(decoder.PixelWidth, decoder.PixelHeight);
        var scale = longest > MaxEdge ? (double)MaxEdge / longest : 1;

        var transform = new BitmapTransform
        {
            ScaledWidth = (uint)Math.Max(1, Math.Round(decoder.PixelWidth * scale)),
            ScaledHeight = (uint)Math.Max(1, Math.Round(decoder.PixelHeight * scale)),
        };

        using var software = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            transform,
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.ColorManageToSRgb);

        return CanvasBitmap.CreateFromSoftwareBitmap(device, software);
    }
}
