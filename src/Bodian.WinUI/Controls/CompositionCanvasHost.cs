using System.Diagnostics;
using System.Numerics;
using Bodian.WinUI.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Composition;
using Microsoft.Graphics.DirectX;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Windows.Foundation;

namespace Bodian.WinUI.Controls;

internal interface ICompositionCanvasRenderer : IDisposable
{
    bool Draw(CanvasDrawingSession session, double width, double height, TimeSpan now);
}

/// <summary>UI 只管理合成视图；尺寸变更、绘制和绘图资源由限帧的独立后台循环处理。</summary>
internal sealed class CompositionCanvasHost : UserControl
{
    private readonly Grid _host = new();
    private readonly Func<CanvasDevice, CancellationToken, ICompositionCanvasRenderer> _factory;
    private readonly ILogger _logger;
    private readonly string _name;
    private readonly double _framesPerSecond;
    private SpriteVisual? _visual;
    private CompositionSurfaceBrush? _brush;
    private CanvasDevice? _device;
    private SurfaceWorker? _worker;
    private XamlRoot? _root;
    private bool _paused;
    private bool _resourcesSuspended;
    private bool _loaded;

    public CompositionCanvasHost(string name, Func<CanvasDevice, CancellationToken, ICompositionCanvasRenderer> factory,
        double framesPerSecond = 120)
    {
        _name = name;
        _framesPerSecond = framesPerSecond;
        _factory = factory;
        _logger = (Application.Current.Resources["BodianLoggerFactory"] as ILoggerFactory
            ?? NullLoggerFactory.Instance).CreateLogger<CompositionCanvasHost>();
        IsHitTestVisible = false;
        Content = _host;
        _host.SizeChanged += (_, _) => UpdateViewport();
        Loaded += (_, _) =>
        {
            _loaded = true;
            _root = XamlRoot;
            if (_root is not null) _root.Changed += OnRootChanged;
            if (!_resourcesSuspended) CreateSurface();
        };
        Unloaded += (_, _) =>
        {
            _loaded = false;
            if (_root is not null) _root.Changed -= OnRootChanged;
            _root = null;
            ReleaseSurface();
        };
    }

    public bool IsPaused
    {
        get => _paused;
        set { if (_paused == value) return; _paused = value; _worker?.SetPaused(value); }
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
            else CreateSurface();
        }
    }

    public void Invalidate() => _worker?.Invalidate();

    private void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => UpdateViewport();
    private void UpdateViewport()
        => _worker?.SetViewport(_host.ActualWidth, _host.ActualHeight, XamlRoot?.RasterizationScale ?? 1);

    private void CreateSurface()
    {
        var deviceLease = BackgroundCanvasDeviceLease.Acquire();
        var device = deviceLease.Device;
        _device = device;
        device.DeviceLost += OnDeviceLost;
        var compositor = ElementCompositionPreview.GetElementVisual(_host).Compositor;
        var graphics = CanvasComposition.CreateCompositionGraphicsDevice(compositor, device);
        var surface = graphics.CreateDrawingSurface(new Size(1, 1), DirectXPixelFormat.B8G8R8A8UIntNormalized,
            DirectXAlphaMode.Premultiplied);
        _brush = compositor.CreateSurfaceBrush(surface);
        _brush.Stretch = CompositionStretch.Fill;
        _visual = compositor.CreateSpriteVisual();
        _visual.RelativeSizeAdjustment = Vector2.One;
        _visual.Brush = _brush;
        ElementCompositionPreview.SetElementChildVisual(_host, _visual);
        _worker = new SurfaceWorker(device, surface, _factory, _logger, _name, _framesPerSecond,
            () => DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    try { surface.Dispose(); }
                    finally { try { graphics.Dispose(); } finally { deviceLease.Dispose(); } }
                }
                catch (Exception exception) { _logger.LogWarning(exception, "释放 {Name} 合成资源失败", _name); }
            }));
        UpdateViewport();
        _worker.SetPaused(_paused);
        _worker.Start();
    }

    private void ReleaseSurface()
    {
        if (_device is not null) _device.DeviceLost -= OnDeviceLost;
        var worker = _worker;
        _worker = null;
        worker?.Stop();
        ElementCompositionPreview.SetElementChildVisual(_host, null);
        _visual?.Dispose();
        _brush?.Dispose();
        _visual = null;
        _brush = null;
        _device = null;
    }

    private void OnDeviceLost(CanvasDevice sender, object args)
        => DispatcherQueue.TryEnqueue(() =>
        {
            if (!_loaded || !ReferenceEquals(sender, _device)) return;
            ReleaseSurface();
            CreateSurface();
        });

    private sealed class SurfaceWorker(CanvasDevice device, CompositionDrawingSurface surface,
        Func<CanvasDevice, CancellationToken, ICompositionCanvasRenderer> factory,
        ILogger logger, string name, double framesPerSecond, Action release)
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly object _lifetimeGate = new();
        private readonly AutoResetEvent _wake = new(false);
        private Viewport _viewport = new(0, 0, 1, 0);
        private int _paused, _dirty = 1, _stopped;

        public void Start() => new Thread(Run) { IsBackground = true, Name = $"Bodian {name} renderer" }.Start();
        public void Stop()
        {
            lock (_lifetimeGate)
            {
                if (_stopped != 0) return;
                _stopped = 1;
                _stop.Cancel();
            }
        }
        public void SetPaused(bool value) { Volatile.Write(ref _paused, value ? 1 : 0); Invalidate(); }
        public void Invalidate()
        {
            lock (_lifetimeGate)
            {
                if (_stopped != 0) return;
                Volatile.Write(ref _dirty, 1);
                _wake.Set();
            }
        }
        public void SetViewport(double width, double height, double scale)
        {
            var old = _viewport;
            if (old.Width == width && old.Height == height && old.Scale == scale) return;
            Volatile.Write(ref _viewport, new Viewport(width, height, scale, Stopwatch.GetTimestamp()));
            Invalidate();
        }

        private void Run()
        {
            ICompositionCanvasRenderer? renderer = null;
            try
            {
                renderer = factory(device, _stop.Token);
                using var pacer = new FramePacer(framesPerSecond, _stop.Token.WaitHandle);
                var statistics = new FrameStatistics(logger, name);
                WaitHandle[] handles = [_stop.Token.WaitHandle, _wake];
                var clock = Stopwatch.StartNew();
                Viewport? current = null;
                var animated = true;
                while (!_stop.IsCancellationRequested)
                {
                    if (Volatile.Read(ref _paused) != 0 || (!animated && Volatile.Read(ref _dirty) == 0))
                    {
                        if (WaitHandle.WaitAny(handles) == 0) break;
                        statistics.Reset();
                        pacer.Reset();
                        continue;
                    }
                    if (!pacer.WaitForNextFrame()) break;
                    if (Volatile.Read(ref _paused) != 0) continue;
                    var viewport = Volatile.Read(ref _viewport);
                    if (viewport.Width <= 0 || viewport.Height <= 0)
                    { Interlocked.Exchange(ref _dirty, 0); animated = false; continue; }
                    var resized = current is null || current.Width != viewport.Width || current.Height != viewport.Height || current.Scale != viewport.Scale;
                    if (resized && current is not null && Stopwatch.GetElapsedTime(viewport.Timestamp).TotalMilliseconds < 50)
                    { viewport = current; resized = false; }
                    var started = Stopwatch.GetTimestamp();
                    if (resized)
                    {
                        CanvasComposition.Resize(surface, new Size(Math.Ceiling(viewport.Width * viewport.Scale),
                            Math.Ceiling(viewport.Height * viewport.Scale)));
                        current = viewport;
                    }
                    Interlocked.Exchange(ref _dirty, 0);
                    using (var session = CanvasComposition.CreateDrawingSession(surface,
                        new Rect(0, 0, Math.Ceiling(viewport.Width * viewport.Scale), Math.Ceiling(viewport.Height * viewport.Scale)),
                        (float)(96 * viewport.Scale)))
                    {
                        session.Clear(Windows.UI.Color.FromArgb(0, 0, 0, 0));
                        animated = renderer.Draw(session, viewport.Width, viewport.Height, clock.Elapsed);
                    }
                    statistics.Record(started);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (Exception exception) { logger.LogError(exception, "{Name} 后台绘制失败", name); }
            finally
            {
                try { renderer?.Dispose(); }
                catch (Exception exception) { logger.LogWarning(exception, "释放 {Name} 绘制资源失败", name); }
                try { release(); }
                finally
                {
                    lock (_lifetimeGate)
                    {
                        _stopped = 1;
                        _wake.Dispose();
                        _stop.Dispose();
                    }
                }
            }
        }

        private sealed record Viewport(double Width, double Height, double Scale, long Timestamp);
    }
}
