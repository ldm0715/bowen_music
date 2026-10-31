using System.Numerics;
using Bowen.Core.Media;
using Microsoft.Extensions.Logging;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using Windows.UI;

namespace Bowen.WinUI.Controls;

/// <summary>封面或自定义图片驱动的背景；固定源图，由合成器缩放到整页。</summary>
/// <remarks>
/// <para>
/// 走 <see cref="CompositionCanvasHost"/> 的共用后台设备，不再建 <c>CanvasControl</c>。
/// 后者会拉起 Win2D 的进程级共享 D3D 设备；首次打开歌词页时这台设备与后台设备同时创建，
/// 是静态显存的主要来源之一。
/// </para>
/// <para>
/// 模糊、饱和度和 600 ms 交叉淡入与原实现一致，只是改为直接画到页面大小的合成表面。
/// 自定义图片走本地文件，按最长边缩到 1920 后再上传，避免 4K 原图长期占一张大纹理。
/// </para>
/// </remarks>
public sealed class LyricsBackdrop : UserControl
{
    public static readonly DependencyProperty CoverUriProperty = DependencyProperty.Register(
        nameof(CoverUri), typeof(Uri), typeof(LyricsBackdrop), new PropertyMetadata(null, OnCoverChanged));

    private const double SurfaceSize = 384;
    private const int MaxLocalEdge = 1920;
    private readonly CompositionCanvasHost _canvas;
    private Uri? _coverUri;
    private string _localPath = "";
    private double _blurDip = 52;
    private double _imageOpacity = 0.72;
    private float _saturation = 0.68f;
    private double _focusX = 0.5;
    private double _focusY = 0.5;
    private double _zoom = 1;

    public LyricsBackdrop()
    {
        IsHitTestVisible = false;
        var logger = (Application.Current.Resources["BowenLoggerFactory"] as ILoggerFactory)
            ?.CreateLogger<LyricsBackdrop>();
        _canvas = new CompositionCanvasHost("歌词背景",
            (device, token) => new BackdropRenderer(device, Snapshot, token, logger),
            framesPerSecond: 60);
        Content = _canvas;
    }

    public Uri? CoverUri
    {
        get => (Uri?)GetValue(CoverUriProperty);
        set => SetValue(CoverUriProperty, value);
    }

    /// <summary>自定义背景图片的本地路径；空串表示跟随封面。</summary>
    public string LocalPath
    {
        get => _localPath;
        set
        {
            value ??= string.Empty;
            if (string.Equals(_localPath, value, StringComparison.Ordinal)) return;
            _localPath = value;
            _canvas.Invalidate();
        }
    }

    /// <summary>模糊量，DIP。封面默认 52，自定义图片由外观面板控制。</summary>
    public double BlurDip
    {
        get => _blurDip;
        set
        {
            if (Math.Abs(_blurDip - value) < 0.01) return;
            _blurDip = value;
            _canvas.Invalidate();
        }
    }

    /// <summary>整层绘制不透明度。封面 0.72；自定义图片保持不透明，暗度由上层蒙版控制。</summary>
    public double ImageOpacity
    {
        get => _imageOpacity;
        set
        {
            if (Math.Abs(_imageOpacity - value) < 0.001) return;
            _imageOpacity = value;
            _canvas.Invalidate();
        }
    }

    /// <summary>饱和度。封面压低到 0.68；自定义图片接近原色。</summary>
    public float Saturation
    {
        get => _saturation;
        set
        {
            if (Math.Abs(_saturation - value) < 0.001f) return;
            _saturation = value;
            _canvas.Invalidate();
        }
    }

    /// <summary>水平裁剪中心，0–1；0.5 为居中。把该点对齐到视口中心。</summary>
    public double FocusX
    {
        get => _focusX;
        set
        {
            value = Math.Clamp(value, 0, 1);
            if (Math.Abs(_focusX - value) < 0.001) return;
            _focusX = value;
            _canvas.Invalidate();
        }
    }

    /// <summary>垂直裁剪中心，0–1；0.5 为居中。</summary>
    public double FocusY
    {
        get => _focusY;
        set
        {
            value = Math.Clamp(value, 0, 1);
            if (Math.Abs(_focusY - value) < 0.001) return;
            _focusY = value;
            _canvas.Invalidate();
        }
    }

    /// <summary>在 cover 基础上的额外缩放，1–3；1 表示刚好铺满。</summary>
    public double Zoom
    {
        get => _zoom;
        set
        {
            value = Math.Clamp(value, 1, 3);
            if (Math.Abs(_zoom - value) < 0.001) return;
            _zoom = value;
            _canvas.Invalidate();
        }
    }

    public bool IsPaused
    {
        get => _canvas.IsPaused;
        set => _canvas.IsPaused = value;
    }

    public bool IsResourceSuspended
    {
        get => _canvas.IsResourceSuspended;
        set => _canvas.IsResourceSuspended = value;
    }

    private BackdropState Snapshot()
        => new(Volatile.Read(ref _coverUri), _localPath, _blurDip, _imageOpacity, _saturation,
            _focusX, _focusY, _zoom);

    private static void OnCoverChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var view = (LyricsBackdrop)sender;
        Volatile.Write(ref view._coverUri, (Uri?)args.NewValue);
        view._canvas.Invalidate();
    }

    private sealed record BackdropState(
        Uri? CoverUri, string LocalPath, double BlurDip, double Opacity, float Saturation,
        double FocusX, double FocusY, double Zoom)
    {
        public bool HasLocal => LocalPath.Length > 0;

        public bool SameSource(BackdropState other)
            => HasLocal == other.HasLocal
                && (HasLocal
                    ? string.Equals(LocalPath, other.LocalPath, StringComparison.Ordinal)
                    : CoverUri == other.CoverUri)
                && Math.Abs(Saturation - other.Saturation) < 0.001f;
    }

    private sealed class BackdropRenderer(
        CanvasDevice device, Func<BackdropState> state, CancellationToken stop, ILogger? logger) : ICompositionCanvasRenderer
    {
        private const double FadeSeconds = 0.6;
        private CanvasBitmap? _loadingBitmap;
        private float _loadingSaturation = 0.68f;
        private CoverSurface? _current;
        private CoverSurface? _previous;
        private CancellationTokenSource? _loadCancellation;
        private BackdropState _requested = new(null, "", 52, 0.72, 0.68f, 0.5, 0.5, 1);
        private bool _loading;
        private double _fade = 1;
        private TimeSpan _fadeStarted;
        private bool _fading;

        public bool Draw(CanvasDrawingSession session, double width, double height, TimeSpan now)
        {
            if (width <= 0 || height <= 0) return false;

            var next = state();
            if (!next.SameSource(_requested)) BeginLoad(next);
            _requested = next;

            if (_loadingBitmap is { } bitmap)
            {
                _loadingBitmap = null;
                _loading = false;
                _previous?.Dispose();
                _previous = _current;
                _current = new CoverSurface(bitmap, _loadingSaturation);
                _fade = 0;
                _fadeStarted = now;
                _fading = true;
            }

            if (_fading)
            {
                var progress = Math.Clamp((now - _fadeStarted).TotalSeconds / FadeSeconds, 0, 1);
                _fade = progress * progress * (3 - 2 * progress);
                if (progress >= 1)
                {
                    _fading = false;
                    _previous?.Dispose();
                    _previous = null;
                    _fade = 1;
                }
            }

            var animating = _fading || _loading;
            if (_previous is { } previous) DrawSurface(session, previous, width, height, 1 - _fade);
            if (_current is { } current) DrawSurface(session, current, width, height, _fade);
            return animating;
        }

        private void BeginLoad(BackdropState next)
        {
            CancelLoad();

            // 没有可画的新来源时要把旧图清掉：否则切回封面模糊但没有封面（或封面加载失败）
            // 会继续留着上一张自定义图片及其纹理。
            if (!next.HasLocal && next.CoverUri is null)
            {
                _current?.Dispose();
                _previous?.Dispose();
                _current = _previous = null;
                _fading = false;
                _fade = 1;
                return;
            }

            _loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(stop);
            _loading = true;
            _ = LoadAsync(next, _loadCancellation.Token);
        }

        private async Task LoadAsync(BackdropState source, CancellationToken token)
        {
            CanvasBitmap? bitmap = null;
            try
            {
                if (source.HasLocal)
                {
                    bitmap = await CanvasBitmap.LoadAsync(device, source.LocalPath).AsTask(token);
                    bitmap = Downscale(bitmap);
                }
                else if (source.CoverUri is { } cover)
                {
                    bitmap = await CanvasBitmap.LoadAsync(device, CoverArtUrl.Jpeg(cover, (int)SurfaceSize)).AsTask(token);
                }
                else
                {
                    return;
                }

                if (token.IsCancellationRequested || !source.SameSource(_requested))
                {
                    bitmap.Dispose();
                    bitmap = null;
                    return;
                }

                _loadingBitmap = bitmap;
                _loadingSaturation = source.Saturation;
                bitmap = null;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception exception)
            {
                _loading = false;
                logger?.LogWarning(exception, "全屏歌词背景加载失败");
            }
            finally
            {
                bitmap?.Dispose();
                if (token.IsCancellationRequested || !source.SameSource(_requested)) _loading = false;
            }
        }

        /// <summary>本地图片按最长边缩到 1920，避免 4K 原图长期占用大纹理。</summary>
        private static CanvasBitmap Downscale(CanvasBitmap bitmap)
        {
            var width = bitmap.Size.Width;
            var height = bitmap.Size.Height;
            var longest = Math.Max(width, height);
            if (longest <= MaxLocalEdge) return bitmap;

            var scale = MaxLocalEdge / longest;
            var target = new CanvasRenderTarget(bitmap.Device, (float)(width * scale), (float)(height * scale), 96);
            using (var session = target.CreateDrawingSession()) session.DrawImage(bitmap);
            bitmap.Dispose();
            return target;
        }

        private void DrawSurface(CanvasDrawingSession session, CoverSurface surface, double width, double height,
            double opacity)
        {
            if (opacity <= 0) return;
            var size = surface.Bitmap.Size;
            if (size.Width <= 0 || size.Height <= 0) return;

            // 与原来的 Viewbox UniformToFill 一致：铺满整页后放大 10%。
            // 裁剪中心取用户选的位置，再把偏移夹回「整页仍被图片盖住」的范围内。
            var scale = Math.Max(width / size.Width, height / size.Height) * 1.1 * _requested.Zoom;
            surface.Blur.BlurAmount = (float)(_requested.BlurDip / scale);
            var offsetX = (width / 2) - (_requested.FocusX * size.Width * scale);
            var offsetY = (height / 2) - (_requested.FocusY * size.Height * scale);
            offsetX = Math.Clamp(offsetX, width - (size.Width * scale), 0);
            offsetY = Math.Clamp(offsetY, height - (size.Height * scale), 0);

            var previous = session.Transform;
            session.Transform = Matrix3x2.CreateScale((float)scale)
                * Matrix3x2.CreateTranslation((float)offsetX, (float)offsetY);
            using (session.CreateLayer((float)(opacity * _requested.Opacity))) session.DrawImage(surface.Blur);
            session.Transform = previous;
        }

        private void CancelLoad()
        {
            _loadCancellation?.Cancel();
            _loadCancellation?.Dispose();
            _loadCancellation = null;
            if (_loadingBitmap is { } bitmap)
            {
                _loadingBitmap = null;
                bitmap.Dispose();
            }
            _loading = false;
        }

        public void Dispose()
        {
            CancelLoad();
            _current?.Dispose();
            _previous?.Dispose();
            _current = _previous = null;
        }

        private sealed class CoverSurface(CanvasBitmap bitmap, float saturation) : IDisposable
        {
            public CanvasBitmap Bitmap { get; } = bitmap;
            private readonly SaturationEffect _saturation = new() { Source = bitmap, Saturation = saturation };
            private GaussianBlurEffect? _blur;

            public GaussianBlurEffect Blur => _blur ??= new GaussianBlurEffect
            {
                Source = _saturation,
                BorderMode = EffectBorderMode.Hard,
                Optimization = EffectOptimization.Balanced,
            };

            public void Dispose()
            {
                _blur?.Dispose();
                _saturation.Dispose();
                Bitmap.Dispose();
            }
        }
    }
}
