using System.ComponentModel;
using System.Diagnostics;
using System.Numerics;
using Bodian.Core.Playback;
using Bodian.WinUI.LyricRenderer;
using Bodian.WinUI.Playback;
using Bodian.WinUI.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Composition;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Hosting;
using Windows.Foundation;
using Microsoft.Graphics.DirectX;
using Microsoft.UI.Xaml.Media;

namespace Bodian.WinUI.Controls;

public sealed partial class LyricsCanvasView : UserControl
{
    private readonly LyricsViewModel _viewModel;
    private readonly IPlaybackService _engine;
    private readonly ILogger<LyricsCanvasView> _logger;
    private readonly LyricsPlaybackClock _clock = new(TimeProvider.System);
    private readonly Stopwatch _animationClock = Stopwatch.StartNew();
    private readonly LyricsRenderer _renderer;
    private bool _loaded;
    private bool _rendering;
    private bool _paused;
    private bool _broken;
    private CanvasDevice? _canvasDevice;
    private CompositionGraphicsDevice? _graphicsDevice;
    private CompositionDrawingSurface? _surface;
    private CompositionSurfaceBrush? _surfaceBrush;
    private SpriteVisual? _surfaceVisual;
    private double _rasterizationScale = 1;
    private double _surfaceWidth;
    private double _surfaceHeight;
    private bool _browsing;
    private uint? _pressedPointer;
    private double _pressY;
    private double _lastPointerY;
    private int _pressedLine = -1;
    private bool _dragging;
    private bool _tapSuppressed;
    private readonly bool _diagnostics = Environment.GetEnvironmentVariable("BODIAN_LYRICS_DIAGNOSTICS") == "1";
    private long _statsStarted;
    private int _statsFrames;
    private double _statsDrawMilliseconds;
    private double _statsMaxMilliseconds;
    private double _statsMaxFrameInterval;
    private long _previousDraw;

    public LyricsCanvasView(LyricsViewModel viewModel, IPlaybackService engine, ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(engine);
        var factory = loggerFactory ?? NullLoggerFactory.Instance;
        _viewModel = viewModel;
        _engine = engine;
        _logger = factory.CreateLogger<LyricsCanvasView>();
        _renderer = new LyricsRenderer(LyricsRenderSettings.Default, factory.CreateLogger<LyricsRenderer>());
        InitializeComponent();
        Canvas.AddHandler(PointerWheelChangedEvent, new PointerEventHandler(OnPointerWheelChanged), true);
        Canvas.AddHandler(PointerPressedEvent, new PointerEventHandler(OnPointerPressed), true);
        Canvas.AddHandler(PointerMovedEvent, new PointerEventHandler(OnPointerMoved), true);
        Canvas.AddHandler(PointerExitedEvent, new PointerEventHandler(OnPointerExited), true);
        Canvas.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnPointerReleased), true);
        Canvas.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnPointerCaptureLost), true);
        Canvas.AddHandler(PointerCanceledEvent, new PointerEventHandler(OnPointerCanceled), true);
        Canvas.AddHandler(TappedEvent, new TappedEventHandler(OnTapped), true);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public event EventHandler? BrowsingChanged;
    public bool IsBrowsing => _browsing;
    public bool IsPaused
    {
        get => _paused;
        set
        {
            _paused = value;
            UpdateRenderingSubscription();
        }
    }

    public void SetFontSize(double fontSize) => _renderer.SetFontSize(fontSize);
    public void ResumeFollowing() => _renderer.ResumeFollowing();

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_loaded) return;
        _loaded = true;
        _broken = false;
        _clock.SetDuration(_engine.Duration);
        _clock.Sync(_engine.Position, force: true);
        _clock.SetPlaying(_engine.State == PlaybackState.Playing);
        _renderer.SetDocument(_viewModel.Document);
        _renderer.SetViewport(Canvas.ActualWidth, Canvas.ActualHeight);
        _engine.PositionChanged += OnPositionChanged;
        _engine.StateChanged += OnStateChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        XamlRoot.Changed += OnXamlRootChanged;
        Guarded(nameof(CreateSurface), CreateSurface);
        UpdateRenderingSubscription();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        _loaded = false;
        UpdateRenderingSubscription();
        _engine.PositionChanged -= OnPositionChanged;
        _engine.StateChanged -= OnStateChanged;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ResetPointerGesture();
        XamlRoot.Changed -= OnXamlRootChanged;
        ReleaseSurface();
    }

    private void UpdateRenderingSubscription()
    {
        var shouldRender = _loaded && !_paused && !_broken;
        if (_rendering == shouldRender) return;
        _rendering = shouldRender;
        if (shouldRender) CompositionTarget.Rendering += OnRendering;
        else CompositionTarget.Rendering -= OnRendering;
    }

    private void CreateSurface()
    {
        _canvasDevice = CanvasDevice.GetSharedDevice();
        _canvasDevice.DeviceLost += OnDeviceLost;
        var compositor = ElementCompositionPreview.GetElementVisual(Canvas).Compositor;
        _graphicsDevice = CanvasComposition.CreateCompositionGraphicsDevice(compositor, _canvasDevice);
        _rasterizationScale = XamlRoot.RasterizationScale;
        _surfaceWidth = Math.Max(1, Math.Ceiling(Canvas.ActualWidth * _rasterizationScale));
        _surfaceHeight = Math.Max(1, Math.Ceiling(Canvas.ActualHeight * _rasterizationScale));
        _surface = _graphicsDevice.CreateDrawingSurface(new Size(_surfaceWidth, _surfaceHeight),
            DirectXPixelFormat.B8G8R8A8UIntNormalized, DirectXAlphaMode.Premultiplied);
        _surfaceBrush = compositor.CreateSurfaceBrush(_surface);
        _surfaceBrush.Stretch = CompositionStretch.Fill;
        _surfaceVisual = compositor.CreateSpriteVisual();
        _surfaceVisual.RelativeSizeAdjustment = Vector2.One;
        _surfaceVisual.Brush = _surfaceBrush;
        ElementCompositionPreview.SetElementChildVisual(Canvas, _surfaceVisual);
        _renderer.RebuildDeviceResources(_canvasDevice);
        _renderer.SetDpi((float)(_rasterizationScale * 96));
        _renderer.SetDocument(_viewModel.Document);
        _renderer.SetViewport(Canvas.ActualWidth, Canvas.ActualHeight);
    }

    private void ReleaseSurface()
    {
        if (_canvasDevice is not null) _canvasDevice.DeviceLost -= OnDeviceLost;
        ElementCompositionPreview.SetElementChildVisual(Canvas, null);
        _renderer.Dispose();
        _surfaceVisual?.Dispose();
        _surfaceBrush?.Dispose();
        _surface?.Dispose();
        _graphicsDevice?.Dispose();
        _surfaceVisual = null;
        _surfaceBrush = null;
        _surface = null;
        _graphicsDevice = null;
        _canvasDevice = null;
    }

    private void OnDeviceLost(CanvasDevice sender, object args)
        => DispatcherQueue.TryEnqueue(() =>
        {
            if (!_loaded) return;
            Guarded(nameof(OnDeviceLost), () => { ReleaseSurface(); CreateSurface(); });
        });

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => ResizeSurface();

    private void ResizeSurface()
    {
        if (_surface is null || XamlRoot is null) return;
        var scale = XamlRoot.RasterizationScale;
        var width = Math.Max(1, Math.Ceiling(Canvas.ActualWidth * scale));
        var height = Math.Max(1, Math.Ceiling(Canvas.ActualHeight * scale));
        if (width != _surfaceWidth || height != _surfaceHeight)
        {
            CanvasComposition.Resize(_surface, new Size(width, height));
            _surfaceWidth = width;
            _surfaceHeight = height;
        }
        _rasterizationScale = scale;
        _renderer.SetDpi((float)(scale * 96));
        _renderer.SetViewport(Canvas.ActualWidth, Canvas.ActualHeight);
    }

    private void OnRendering(object? sender, object args)
        => Guarded(nameof(OnRendering), () =>
        {
            if (_surface is null || Canvas.ActualWidth <= 0 || Canvas.ActualHeight <= 0) return;
            var drawStarted = Stopwatch.GetTimestamp();
            _renderer.Update(_clock.Position, _animationClock.Elapsed, _clock.JumpCount);
            using (var session = CanvasComposition.CreateDrawingSession(_surface,
                new Rect(0, 0, _surfaceWidth, _surfaceHeight), (float)(_rasterizationScale * 96)))
            {
                session.Clear(Windows.UI.Color.FromArgb(0, 0, 0, 0));
                _renderer.Draw(session);
            }
            if (_diagnostics) RecordFrame(drawStarted);
            if (_browsing != _renderer.IsBrowsing)
            {
                _browsing = _renderer.IsBrowsing;
                BrowsingChanged?.Invoke(this, EventArgs.Empty);
            }
        });

    private void RecordFrame(long drawStarted)
    {
        if (_statsStarted == 0) _statsStarted = drawStarted;
        var elapsed = Stopwatch.GetElapsedTime(drawStarted).TotalMilliseconds;
        _statsDrawMilliseconds += elapsed;
        _statsMaxMilliseconds = Math.Max(_statsMaxMilliseconds, elapsed);
        if (_previousDraw != 0)
            _statsMaxFrameInterval = Math.Max(_statsMaxFrameInterval, Stopwatch.GetElapsedTime(_previousDraw, drawStarted).TotalMilliseconds);
        _previousDraw = drawStarted;
        _statsFrames++;
        var window = Stopwatch.GetElapsedTime(_statsStarted).TotalSeconds;
        if (window < 5) return;
        _logger.LogInformation("歌词绘制统计：{Fps:F1} fps，平均绘制 {Average:F2} ms，最慢绘制 {Maximum:F2} ms，最大帧间隔 {Interval:F2} ms",
            _statsFrames / window, _statsDrawMilliseconds / _statsFrames, _statsMaxMilliseconds, _statsMaxFrameInterval);
        _statsStarted = _previousDraw = 0;
        _statsFrames = 0;
        _statsDrawMilliseconds = _statsMaxMilliseconds = _statsMaxFrameInterval = 0;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs args) => ResizeSurface();

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs args)
    {
        var properties = args.GetCurrentPoint(Canvas).Properties;
        if (properties.IsHorizontalMouseWheel || properties.MouseWheelDelta == 0) return;
        _renderer.ScrollBy(-properties.MouseWheelDelta * 0.85, _animationClock.Elapsed);
        args.Handled = true;
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs args)
    {
        var point = args.GetCurrentPoint(Canvas);
        if (args.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse
            && !point.Properties.IsLeftButtonPressed) return;
        _pressedPointer = args.Pointer.PointerId;
        _pressY = _lastPointerY = point.Position.Y;
        _pressedLine = _renderer.LineIndexAt(point.Position.Y);
        _dragging = false;
        _tapSuppressed = false;
        Canvas.CapturePointer(args.Pointer);
        args.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs args)
    {
        var point = args.GetCurrentPoint(Canvas);
        _renderer.SetPointerY(point.Position.Y, _animationClock.Elapsed);
        if (_pressedPointer != args.Pointer.PointerId) return;
        if (Math.Abs(point.Position.Y - _pressY) > 6) _dragging = _tapSuppressed = true;
        if (_dragging)
        {
            _renderer.ScrollBy(_lastPointerY - point.Position.Y, _animationClock.Elapsed);
            args.Handled = true;
        }
        _lastPointerY = point.Position.Y;
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs args)
        => _renderer.SetPointerY(null, _animationClock.Elapsed);

    private void OnPointerReleased(object sender, PointerRoutedEventArgs args)
    {
        if (_pressedPointer != args.Pointer.PointerId) return;
        ResetPointerGesture();
        Canvas.ReleasePointerCapture(args.Pointer);
        args.Handled = true;
    }

    private async void OnTapped(object sender, TappedRoutedEventArgs args)
    {
        if (_tapSuppressed) return;
        var line = _renderer.LineIndexAt(args.GetPosition(Canvas).Y);
        if (line < 0 || line >= _viewModel.Document.Lines.Count) return;
        args.Handled = true;
        var position = _viewModel.Document.Lines[line].Start;
        await _viewModel.SeekToLineCommand.ExecuteAsync(line);
        _clock.Sync(position, force: true);
        _renderer.ResumeFollowing();
        _logger.LogInformation("歌词点击跳转：行 {Line}，时间 {Position:F2} 秒", line, position.TotalSeconds);
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs args) => ResetPointerGesture();
    private void OnPointerCanceled(object sender, PointerRoutedEventArgs args)
    {
        _tapSuppressed = true;
        ResetPointerGesture();
    }
    private void ResetPointerGesture()
    {
        _pressedPointer = null;
        _pressedLine = -1;
        _dragging = false;
    }

    private void OnPositionChanged(object? sender, PlaybackPositionChangedEventArgs args)
    {
        _clock.SetDuration(args.Duration);
        _clock.Sync(args.Position);
    }
    private void OnStateChanged(object? sender, PlaybackStateChangedEventArgs args)
        => _clock.SetPlaying(args.State == PlaybackState.Playing);
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(LyricsViewModel.Document)) _renderer.SetDocument(_viewModel.Document);
    }
    private void Guarded(string callback, Action action)
    {
        if (_broken) return;
        try { action(); }
        catch (Exception exception)
        {
            _broken = true;
            UpdateRenderingSubscription();
            _logger.LogCritical(exception, "全屏歌词渲染在 {Callback} 中失败，已暂停绘制", callback);
        }
    }
}
