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

/// <summary>封面驱动的模糊背景；固定 384 px 源图，由合成器缩放到整页。</summary>
/// <remarks>
/// <para>
/// 走 <see cref="CompositionCanvasHost"/> 的共用后台设备，不再建 <c>CanvasControl</c>。
/// 后者会拉起 Win2D 的进程级共享 D3D 设备；首次打开歌词页时这台设备与后台设备同时创建，
/// 是静态显存的主要来源之一。
/// </para>
/// <para>
/// 模糊、饱和度和 600 ms 交叉淡入与原实现一致，只是改为直接画到页面大小的合成表面。
/// </para>
/// </remarks>
public sealed class LyricsBackdrop : UserControl
{
    public static readonly DependencyProperty CoverUriProperty = DependencyProperty.Register(
        nameof(CoverUri), typeof(Uri), typeof(LyricsBackdrop), new PropertyMetadata(null, OnCoverChanged));

    private const double SurfaceSize = 384;
    private readonly CompositionCanvasHost _canvas;
    private Uri? _coverUri;

    public LyricsBackdrop()
    {
        IsHitTestVisible = false;
        var logger = (Application.Current.Resources["BowenLoggerFactory"] as ILoggerFactory)
            ?.CreateLogger<LyricsBackdrop>();
        _canvas = new CompositionCanvasHost("歌词背景",
            (device, token) => new BackdropRenderer(device, () => Volatile.Read(ref _coverUri), token, logger),
            framesPerSecond: 60);
        Content = _canvas;
    }

    public Uri? CoverUri
    {
        get => (Uri?)GetValue(CoverUriProperty);
        set => SetValue(CoverUriProperty, value);
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

    private static void OnCoverChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var view = (LyricsBackdrop)sender;
        Volatile.Write(ref view._coverUri, (Uri?)args.NewValue);
        view._canvas.Invalidate();
    }

    private sealed class BackdropRenderer(
        CanvasDevice device, Func<Uri?> cover, CancellationToken stop, ILogger? logger) : ICompositionCanvasRenderer
    {
        private const double FadeSeconds = 0.6;

        private CanvasBitmap? _loadingBitmap;
        private CoverSurface? _current;
        private CoverSurface? _previous;
        private CancellationTokenSource? _loadCancellation;
        private Uri? _requested;
        private bool _loading;
        private double _fade = 1;
        private TimeSpan _fadeStarted;
        private bool _fading;

        public bool Draw(CanvasDrawingSession session, double width, double height, TimeSpan now)
        {
            if (width <= 0 || height <= 0) return false;

            var uri = cover();
            if (uri != _requested) BeginLoad(uri);

            if (_loadingBitmap is { } bitmap)
            {
                _loadingBitmap = null;
                _loading = false;
                _previous?.Dispose();
                _previous = _current;
                _current = new CoverSurface(bitmap);
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

        private void BeginLoad(Uri? uri)
        {
            CancelLoad();
            _requested = uri;
            if (uri is null)
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
            _ = LoadAsync(uri, _loadCancellation.Token);
        }

        private async Task LoadAsync(Uri uri, CancellationToken token)
        {
            try
            {
                var bitmap = await CanvasBitmap.LoadAsync(device, CoverArtUrl.Jpeg(uri, (int)SurfaceSize)).AsTask(token);
                if (token.IsCancellationRequested || uri != _requested)
                {
                    bitmap.Dispose();
                    return;
                }

                _loadingBitmap = bitmap;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception exception)
            {
                _loading = false;
                logger?.LogWarning(exception, "全屏歌词封面背景加载失败");
            }
            finally
            {
                if (token.IsCancellationRequested || uri != _requested) _loading = false;
            }
        }

        private void DrawSurface(CanvasDrawingSession session, CoverSurface surface, double width, double height,
            double opacity)
        {
            if (opacity <= 0) return;
            var size = surface.Bitmap.Size;
            if (size.Width <= 0 || size.Height <= 0) return;

            // 与原来的 Viewbox UniformToFill 一致：铺满整页后放大 10%，居中。
            var scale = Math.Max(width / size.Width, height / size.Height) * 1.1;
            surface.Blur.BlurAmount = (float)(52 / scale);
            var previous = session.Transform;
            session.Transform = Matrix3x2.CreateScale((float)scale)
                * Matrix3x2.CreateTranslation((float)((width - size.Width * scale) / 2),
                    (float)((height - size.Height * scale) / 2));
            using (session.CreateLayer((float)(opacity * 0.72))) session.DrawImage(surface.Blur);
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

        private sealed class CoverSurface(CanvasBitmap bitmap) : IDisposable
        {
            public CanvasBitmap Bitmap { get; } = bitmap;
            private readonly SaturationEffect _saturation = new() { Source = bitmap, Saturation = 0.68f };
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
