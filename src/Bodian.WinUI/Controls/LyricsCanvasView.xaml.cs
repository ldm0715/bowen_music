using System.ComponentModel;
using System.Diagnostics;
using System.Numerics;
using Bodian.Core.Playback;
using Bodian.WinUI.LyricRenderer;
using Bodian.WinUI.Playback;
using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graphics.DirectX;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Composition;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Hosting;
using Windows.Foundation;

namespace Bodian.WinUI.Controls;

public sealed partial class LyricsCanvasView : UserControl
{
    private readonly LyricsViewModel _viewModel;
    private readonly IPlaybackService _engine;
    private readonly ILogger<LyricsCanvasView> _logger;
    private readonly LyricsPlaybackClock _clock = new(TimeProvider.System);
    private readonly Stopwatch _animationClock = Stopwatch.StartNew();
    private readonly ILoggerFactory _loggerFactory;
    private LyricsRenderLoop? _renderLoop;
    private XamlRoot? _subscribedRoot;
    private double _fontSize = 40;
    private bool _loaded;
    private bool _paused;
    private bool _resourcesSuspended;
    private bool _broken;
    private CanvasDevice? _canvasDevice;
    private CompositionSurfaceBrush? _surfaceBrush;
    private SpriteVisual? _surfaceVisual;
    private bool _browsing;
    private uint? _pressedPointer;
    private double _pressY;
    private double _lastPointerY;
    private int _pressedLine = -1;
    private bool _dragging;
    private bool _tapSuppressed;

    public LyricsCanvasView(LyricsViewModel viewModel, IPlaybackService engine, ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(engine);
        var factory = loggerFactory ?? NullLoggerFactory.Instance;
        _viewModel = viewModel;
        _engine = engine;
        _logger = factory.CreateLogger<LyricsCanvasView>();
        _loggerFactory = factory;
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
            if (_paused == value) return;
            _paused = value;
            UpdateRenderingSubscription();
        }
    }

    public bool IsResourceSuspended
    {
        get => _resourcesSuspended;
        set
        {
            if (_resourcesSuspended == value) return;
            _resourcesSuspended = value;
            if (!_loaded) return;
            if (value) ReleaseSurface();
            else
            {
                Guarded(nameof(CreateSurface), CreateSurface);
                UpdateRenderingSubscription();
            }
        }
    }

    public void SetFontSize(double fontSize)
    {
        _fontSize = fontSize;
        ResizeSurface();
    }
    public void ResumeFollowing() => _renderLoop?.Send(renderer => renderer.ResumeFollowing());

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_loaded) return;
        _loaded = true;
        _broken = false;
        _clock.SetDuration(_engine.Duration);
        _clock.Sync(_engine.Position, force: true);
        _clock.SetPlaying(_engine.State == PlaybackState.Playing);
        _engine.PositionChanged += OnPositionChanged;
        _engine.StateChanged += OnStateChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _subscribedRoot = XamlRoot;
        if (_subscribedRoot is not null) _subscribedRoot.Changed += OnXamlRootChanged;
        if (!_resourcesSuspended) Guarded(nameof(CreateSurface), CreateSurface);
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
        if (_subscribedRoot is not null) _subscribedRoot.Changed -= OnXamlRootChanged;
        _subscribedRoot = null;
        ReleaseSurface();
    }

    private void UpdateRenderingSubscription()
        => _renderLoop?.SetPaused(!_loaded || _paused || _broken);

    private void CreateSurface()
    {
        // 后台绘图共用设备，避免歌词、频谱、倒影各建一套 D3D 驱动资源。
        var deviceLease = BackgroundCanvasDeviceLease.Acquire();
        _canvasDevice = deviceLease.Device;
        _canvasDevice.DeviceLost += OnDeviceLost;
        var compositor = ElementCompositionPreview.GetElementVisual(Canvas).Compositor;
        var graphicsDevice = CanvasComposition.CreateCompositionGraphicsDevice(compositor, _canvasDevice);
        var surface = graphicsDevice.CreateDrawingSurface(new Size(1, 1),
            DirectXPixelFormat.B8G8R8A8UIntNormalized, DirectXAlphaMode.Premultiplied);
        _surfaceBrush = compositor.CreateSurfaceBrush(surface);
        _surfaceBrush.Stretch = CompositionStretch.Fill;
        _surfaceVisual = compositor.CreateSpriteVisual();
        _surfaceVisual.RelativeSizeAdjustment = Vector2.Zero;
        _surfaceVisual.Brush = _surfaceBrush;
        ElementCompositionPreview.SetElementChildVisual(Canvas, _surfaceVisual);
        LyricsRenderLoop? loop = null;
        loop = new LyricsRenderLoop(_canvasDevice, graphicsDevice, surface, _clock, _animationClock, _loggerFactory,
            browsing => DispatcherQueue.TryEnqueue(() =>
            {
                if (!ReferenceEquals(_renderLoop, loop)) return;
                _browsing = browsing;
                BrowsingChanged?.Invoke(this, EventArgs.Empty);
            }),
            () => DispatcherQueue.TryEnqueue(() =>
            {
                if (!ReferenceEquals(_renderLoop, loop)) return;
                _broken = true;
                UpdateRenderingSubscription();
            }),
            () => DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    try { surface.Dispose(); }
                    finally { try { graphicsDevice.Dispose(); } finally { deviceLease.Dispose(); } }
                }
                catch (Exception exception) { _logger.LogWarning(exception, "释放歌词合成资源失败"); }
            }));
        _renderLoop = loop;
        loop.SetDocument(_viewModel.Document);
        ResizeSurface();
        loop.Start();
    }

    private void ReleaseSurface()
    {
        if (_canvasDevice is not null) _canvasDevice.DeviceLost -= OnDeviceLost;
        var loop = _renderLoop;
        _renderLoop = null;
        loop?.Stop();
        ElementCompositionPreview.SetElementChildVisual(Canvas, null);
        _surfaceVisual?.Dispose();
        _surfaceBrush?.Dispose();
        _surfaceVisual = null;
        _surfaceBrush = null;
        _canvasDevice = null;
        if (_browsing)
        {
            _browsing = false;
            BrowsingChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnDeviceLost(CanvasDevice sender, object args)
        => DispatcherQueue.TryEnqueue(() =>
        {
            if (!_loaded || !ReferenceEquals(sender, _canvasDevice)) return;
            _broken = false;
            Guarded(nameof(OnDeviceLost), () => { ReleaseSurface(); CreateSurface(); });
            UpdateRenderingSubscription();
        });

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => ResizeSurface();

    private void ResizeSurface()
    {
        if (!_loaded || _renderLoop is null || XamlRoot is null) return;
        var scale = XamlRoot.RasterizationScale;
        var width = Math.Ceiling(Canvas.ActualWidth * scale) / scale;
        var height = Math.Ceiling(Canvas.ActualHeight * scale) / scale;
        if (_surfaceVisual is not null) _surfaceVisual.Size = new Vector2((float)width, (float)height);
        _renderLoop.SetViewport(width, height, scale, _fontSize);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs args) => ResizeSurface();

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs args)
    {
        var properties = args.GetCurrentPoint(Canvas).Properties;
        if (properties.IsHorizontalMouseWheel || properties.MouseWheelDelta == 0) return;
        var delta = -properties.MouseWheelDelta * 0.85;
        var now = _animationClock.Elapsed;
        _renderLoop?.Send(renderer => renderer.ScrollBy(delta, now));
        args.Handled = true;
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs args)
    {
        var point = args.GetCurrentPoint(Canvas);
        if (args.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse
            && !point.Properties.IsLeftButtonPressed) return;
        _pressedPointer = args.Pointer.PointerId;
        _pressY = _lastPointerY = point.Position.Y;
        _pressedLine = _renderLoop?.LineIndexAt(point.Position.Y) ?? -1;
        _dragging = false;
        _tapSuppressed = false;
        Canvas.CapturePointer(args.Pointer);
        args.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs args)
    {
        var point = args.GetCurrentPoint(Canvas);
        var pointerY = point.Position.Y;
        var now = _animationClock.Elapsed;
        _renderLoop?.Send(renderer => renderer.SetPointerY(pointerY, now));
        if (_pressedPointer != args.Pointer.PointerId) return;
        if (Math.Abs(point.Position.Y - _pressY) > 6) _dragging = _tapSuppressed = true;
        if (_dragging)
        {
            var delta = _lastPointerY - pointerY;
            _renderLoop?.Send(renderer => renderer.ScrollBy(delta, now));
            args.Handled = true;
        }
        _lastPointerY = point.Position.Y;
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs args)
    {
        var now = _animationClock.Elapsed;
        _renderLoop?.Send(renderer => renderer.SetPointerY(null, now));
    }

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
        var line = _renderLoop?.LineIndexAt(args.GetPosition(Canvas).Y) ?? -1;
        if (line < 0 || line >= _viewModel.Document.Lines.Count) return;
        args.Handled = true;
        var position = _viewModel.Document.Lines[line].Start;
        await _viewModel.SeekToLineCommand.ExecuteAsync(line);
        _clock.Sync(position, force: true);
        ResumeFollowing();
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
        _renderLoop?.Invalidate();
    }
    private void OnStateChanged(object? sender, PlaybackStateChangedEventArgs args)
    {
        _clock.SetPlaying(args.State == PlaybackState.Playing);
        _renderLoop?.Invalidate();
    }
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(LyricsViewModel.Document)) _renderLoop?.SetDocument(_viewModel.Document);
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
