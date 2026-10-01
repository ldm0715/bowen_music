using System.Diagnostics;
using System.Numerics;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;

namespace Bodian.WinUI.Controls;

public sealed class LyricsBackdrop : UserControl
{
    public static readonly DependencyProperty CoverUriProperty = DependencyProperty.Register(
        nameof(CoverUri), typeof(Uri), typeof(LyricsBackdrop), new PropertyMetadata(null, OnCoverChanged));

    private readonly CanvasControl _canvas;
    private readonly DispatcherQueueTimer _fadeTimer;
    private readonly Stopwatch _fadeClock = new();
    private readonly ILogger? _logger;
    private CanvasDevice? _device;
    private CoverSurface? _current;
    private CoverSurface? _previous;
    private Uri? _requestedUri;
    private int _request;
    private bool _loaded;
    private double _fade = 1;

    public LyricsBackdrop()
    {
        IsHitTestVisible = false;
        _canvas = new CanvasControl { ClearColor = Windows.UI.Color.FromArgb(0, 0, 0, 0) };
        Content = _canvas;
        _logger = (Application.Current.Resources["BodianLoggerFactory"] as ILoggerFactory)
            ?.CreateLogger<LyricsBackdrop>();
        _fadeTimer = DispatcherQueue.CreateTimer();
        _fadeTimer.Interval = TimeSpan.FromMilliseconds(16);
        _fadeTimer.Tick += OnFadeTick;
        _canvas.CreateResources += OnCreateResources;
        _canvas.Draw += OnDraw;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public Uri? CoverUri
    {
        get => (Uri?)GetValue(CoverUriProperty);
        set => SetValue(CoverUriProperty, value);
    }

    private static void OnCoverChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => _ = ((LyricsBackdrop)sender).LoadCoverAsync();

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        _loaded = true;
        _canvas.Invalidate();
        _ = LoadCoverAsync();
    }

    private void OnCreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args)
    {
        _request++;
        ClearSurfaces();
        _requestedUri = null;
        _device = sender.Device;
        _ = LoadCoverAsync();
    }

    private async Task LoadCoverAsync()
    {
        if (!_loaded || _device is not { } device) return;
        var uri = CoverUri;
        if (uri == _requestedUri && _current is not null) return;
        _requestedUri = uri;
        var request = ++_request;
        if (uri is null)
        {
            ClearSurfaces();
            _canvas.Invalidate();
            return;
        }

        try
        {
            var bitmap = await CanvasBitmap.LoadAsync(device, uri);
            if (!_loaded || request != _request || device != _device)
            {
                bitmap.Dispose();
                return;
            }
            _fadeTimer.Stop();
            _previous?.Dispose();
            _previous = _current;
            _current = new CoverSurface(bitmap);
            _fade = 0;
            _fadeClock.Restart();
            _fadeTimer.Start();
            _canvas.Invalidate();
        }
        catch (Exception exception)
        {
            if (!_loaded || request != _request) return;
            _logger?.LogWarning(exception, "全屏歌词封面背景加载失败");
            ClearSurfaces();
            _canvas.Invalidate();
        }
    }

    private void OnFadeTick(DispatcherQueueTimer sender, object args)
    {
        var progress = Math.Clamp(_fadeClock.Elapsed.TotalMilliseconds / 600, 0, 1);
        _fade = progress * progress * (3 - 2 * progress);
        _canvas.Invalidate();
        if (progress < 1) return;
        _fadeTimer.Stop();
        _previous?.Dispose();
        _previous = null;
    }

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var session = args.DrawingSession;
        if (_previous is { } previous) DrawSurface(session, previous, 1 - _fade);
        if (_current is { } current) DrawSurface(session, current, _fade);
    }

    private void DrawSurface(CanvasDrawingSession session, CoverSurface surface, double opacity)
    {
        if (opacity <= 0 || _canvas.ActualWidth <= 0 || _canvas.ActualHeight <= 0) return;
        var size = surface.Bitmap.Size;
        var scale = Math.Max(_canvas.ActualWidth / size.Width, _canvas.ActualHeight / size.Height) * 1.1;
        surface.Blur.BlurAmount = (float)(52 / scale);
        var previousTransform = session.Transform;
        session.Transform = Matrix3x2.CreateScale((float)scale)
            * Matrix3x2.CreateTranslation((float)((_canvas.ActualWidth - size.Width * scale) / 2),
                (float)((_canvas.ActualHeight - size.Height * scale) / 2));
        using (session.CreateLayer((float)(opacity * 0.72))) session.DrawImage(surface.Blur);
        session.Transform = previousTransform;
    }

    private void ClearSurfaces()
    {
        _fadeTimer.Stop();
        _current?.Dispose();
        _previous?.Dispose();
        _current = _previous = null;
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        _loaded = false;
        _request++;
        _device = null;
        _requestedUri = null;
        ClearSurfaces();
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
