using System.ComponentModel;
using System.Numerics;
using Bodian.Core.Playback;
using Bodian.WinUI.LyricRenderer;
using Bodian.WinUI.Playback;
using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Composition;
using Microsoft.Graphics.DirectX;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

namespace Bodian.WinUI.Controls;

/// <summary>桌面歌词的合成表面；与歌词页共用播放时钟、字形测量和后台帧节奏器。</summary>
public sealed class DesktopLyricsCanvasView : Grid, IDisposable
{
    private readonly DesktopLyricsViewModel _settings;
    private readonly LyricsViewModel _lyrics;
    private readonly IPlaybackService _engine;
    private readonly ILogger _logger;
    private readonly LyricsPlaybackClock _clock = new(TimeProvider.System);
    private DesktopLyricsRenderLoop? _loop;
    private DispatcherQueueTimer? _resizeTimer;
    private CanvasDevice? _device;
    private BackgroundCanvasDeviceLease? _deviceLease;
    private CompositionSurfaceBrush? _brush;
    private SpriteVisual? _visual;
    private XamlRoot? _subscribedRoot;
    private bool _loaded, _paused, _broken, _disposed;
    private bool _resourcesSuspended = true;

    public DesktopLyricsCanvasView(DesktopLyricsViewModel settings, LyricsViewModel lyrics,
        IPlaybackService engine, ILoggerFactory loggerFactory)
    {
        _settings = settings;
        _lyrics = lyrics;
        _engine = engine;
        _logger = loggerFactory.CreateLogger<DesktopLyricsCanvasView>();
        Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += (_, _) => OnViewportChanged();
    }

    public bool IsPaused
    {
        get => _paused;
        set
        {
            _paused = value;
            if (!_paused && _broken && _loaded) RecreateSurface();
            _loop?.SetPaused(_paused || _broken || !_loaded);
        }
    }

    public bool IsResourceSuspended
    {
        get => _resourcesSuspended;
        set
        {
            if (_resourcesSuspended == value || _disposed) return;
            _resourcesSuspended = value;
            if (value)
            {
                Unload();
                ReleaseSurface();
            }
            else if (IsLoaded) OnLoaded(this, new RoutedEventArgs());
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_loaded || _disposed || _resourcesSuspended) return;
        _loaded = true;
        _clock.SetDuration(_engine.Duration);
        _clock.Sync(_engine.Position, force: true);
        _clock.SetPlaying(_engine.State == PlaybackState.Playing);
        _engine.PositionChanged += OnPositionChanged;
        _engine.StateChanged += OnStateChanged;
        _settings.PropertyChanged += OnSettingsChanged;
        _lyrics.PropertyChanged += OnLyricsChanged;
        _subscribedRoot = XamlRoot;
        if (_subscribedRoot is not null) _subscribedRoot.Changed += OnXamlRootChanged;
        CreateSurface();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args) => Unload();

    private void Unload()
    {
        if (!_loaded) return;
        _loaded = false;
        _resizeTimer?.Stop();
        _engine.PositionChanged -= OnPositionChanged;
        _engine.StateChanged -= OnStateChanged;
        _settings.PropertyChanged -= OnSettingsChanged;
        _lyrics.PropertyChanged -= OnLyricsChanged;
        if (_subscribedRoot is not null) _subscribedRoot.Changed -= OnXamlRootChanged;
        _subscribedRoot = null;
        ReleaseSurface();
    }

    private void CreateSurface()
    {
        try
        {
            _broken = false;
            var deviceLease = BackgroundCanvasDeviceLease.Acquire();
            _deviceLease = deviceLease;
            var device = deviceLease.Device;
            _device = device;
            device.DeviceLost += OnDeviceLost;
            var compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
            var graphics = CanvasComposition.CreateCompositionGraphicsDevice(compositor, device);
            var surface = graphics.CreateDrawingSurface(new Size(1, 1),
                DirectXPixelFormat.B8G8R8A8UIntNormalized, DirectXAlphaMode.Premultiplied);
            _brush = compositor.CreateSurfaceBrush(surface);
            _brush.Stretch = CompositionStretch.Fill;
            _brush.BitmapInterpolationMode = CompositionBitmapInterpolationMode.NearestNeighbor;
            _visual = compositor.CreateSpriteVisual();
            _visual.RelativeSizeAdjustment = Vector2.Zero;
            _visual.Brush = _brush;
            ElementCompositionPreview.SetElementChildVisual(this, _visual);
            DesktopLyricsRenderLoop? loop = null;
            loop = new DesktopLyricsRenderLoop(device, surface, _clock, _logger,
                () => DispatcherQueue.TryEnqueue(() =>
                {
                    if (!ReferenceEquals(_loop, loop)) return;
                    _broken = true;
                    _loop?.SetPaused(true);
                }),
                () => DispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        try { surface.Dispose(); }
                        finally { try { graphics.Dispose(); } finally { deviceLease.Dispose(); } }
                    }
                    catch (Exception exception) { _logger.LogWarning(exception, "释放桌面歌词合成资源失败"); }
                }));
            _loop = loop;
            UpdateInput();
            loop.SetPaused(_paused || !_loaded);
            loop.Start();
        }
        catch (Exception exception)
        {
            _broken = true;
            ReleaseSurface();
            _logger.LogError(exception, "创建桌面歌词合成表面失败");
        }
    }

    private void ReleaseSurface()
    {
        if (_device is not null) _device.DeviceLost -= OnDeviceLost;
        var loop = _loop;
        _loop = null;
        loop?.Stop();
        ElementCompositionPreview.SetElementChildVisual(this, null);
        _visual?.Dispose();
        _brush?.Dispose();
        _visual = null;
        _brush = null;
        // 开始绘制后由渲染线程结束回调释放设备，避免与最后一帧竞争。
        if (loop is null) _deviceLease?.Dispose();
        _deviceLease = null;
        _device = null;
    }

    private void UpdateInput()
    {
        if (!_loaded || _loop is null || XamlRoot is null) return;
        var scale = XamlRoot.RasterizationScale;
        if (_visual is not null)
            _visual.Size = new Vector2((float)(Math.Ceiling(ActualWidth * scale) / scale),
                (float)(Math.Ceiling(ActualHeight * scale) / scale));
        _loop.SetInput(new DesktopLyricsRenderInput(_lyrics.Document,
            _lyrics.HasTrack ? "暂无歌词" : AppIdentity.DisplayName, ActualWidth, ActualHeight,
            XamlRoot.RasterizationScale, _settings.FontSize, _settings.TextColor,
            _settings.DualLine, _settings.Alignment, _engine.State == PlaybackState.Playing));
    }

    private void OnPositionChanged(object? sender, PlaybackPositionChangedEventArgs args)
    {
        var previous = _clock.Position;
        _clock.SetDuration(args.Duration);
        _clock.Sync(args.Position);
        if (_engine.State == PlaybackState.Playing || _clock.Position != previous) _loop?.NotifyPositionChanged();
    }

    private void OnStateChanged(object? sender, PlaybackStateChangedEventArgs args)
    {
        _clock.SetPlaying(args.State == PlaybackState.Playing);
        UpdateInput();
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(DesktopLyricsViewModel.FontSize) or nameof(DesktopLyricsViewModel.TextColor)
            or nameof(DesktopLyricsViewModel.DualLine) or nameof(DesktopLyricsViewModel.Alignment))
        {
            if (_broken && _loaded) RecreateSurface();
            UpdateInput();
        }
    }
    private void OnLyricsChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(LyricsViewModel.Document) or nameof(LyricsViewModel.HasTrack)) UpdateInput();
    }
    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => OnViewportChanged();

    private void OnViewportChanged()
    {
        if (!_loaded || _disposed) return;
        if (_broken) RecreateSurface();
        UpdateInput();
        if (_resizeTimer is null)
        {
            _resizeTimer = DispatcherQueue.CreateTimer();
            _resizeTimer.IsRepeating = false;
            _resizeTimer.Interval = TimeSpan.FromMilliseconds(110);
            _resizeTimer.Tick += (_, _) => { UpdateInput(); _loop?.Invalidate(); };
        }
        // 原生窗口尺寸提交后再补一帧，暂停状态也不会保留被 Resize 清空的表面。
        _resizeTimer.Stop();
        _resizeTimer.Start();
    }

    private void RecreateSurface()
    {
        ReleaseSurface();
        CreateSurface();
    }
    private void OnDeviceLost(CanvasDevice sender, object args)
        => DispatcherQueue.TryEnqueue(() =>
        {
            if (!_loaded || _disposed || !ReferenceEquals(sender, _device)) return;
            ReleaseSurface();
            CreateSurface();
        });

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Unload();
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
    }
}
