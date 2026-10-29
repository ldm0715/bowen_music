using System.ComponentModel;
using System.Diagnostics;
using System.Numerics;
using Bowen.Core.Models;
using Bowen.Core.Playback;
using Bowen.WinUI.LyricRenderer;
using Bowen.WinUI.Playback;
using Bowen.WinUI.Services;
using Bowen.WinUI.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graphics.DirectX;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Composition;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Hosting;
using Windows.Foundation;
using Windows.UI;

namespace Bowen.WinUI.Controls;

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
    private double _fontSize = LyricsSettings.DefaultFontSize;
    private string _fontFamily = "";
    private double _lineGap = LyricsSettings.DefaultLineGap;
    private LyricsAlignment _alignment = LyricsAlignment.Left;
    private double _horizontalOffsetPercent;
    private double _verticalOffsetPercent;
    private Color _highlightColor = Color.FromArgb(255, 255, 255, 255);
    private bool _loaded;
    private bool _paused;
    private bool _resourcesSuspended;
    private bool _broken;

    // 引擎状态进入 Playing 只代表文件已加载；音频可能还在缓冲。
    // 位置真正变化之前不让歌词时钟起步，否则会先滚一段再跳回来。
    private PlaybackState _lastEngineState = PlaybackState.Idle;
    private TimeSpan? _lastEnginePosition;
    private bool _waitingForPlaybackToAdvance;

    /// <summary>这次等待是跳转造成的（而不是新歌加载）。跳转要等位置越过目标。</summary>
    private bool _waitingForSeekAdvance;

    /// <summary>跳转目标。mpv 在 seek 完成前就会把位置报成这个值。</summary>
    private TimeSpan _seekTarget;

    /// <summary>超过目标这么多才算声音真的接上。</summary>
    private static readonly TimeSpan SeekSettledThreshold = TimeSpan.FromMilliseconds(50);

    /// <summary>播放期间定时让常驻设备归还缓存。<b>不这么做的话，逐行渲染的显存会一直占着。</b></summary>
    private DispatcherQueueTimer? _cacheTrimTimer;
    private static readonly TimeSpan CacheTrimInterval = TimeSpan.FromSeconds(30);
    private DispatcherQueueTimer? _playbackProbeTimer;
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

    /// <summary>切换歌词字体。设置页改动后调用，渲染线程收到后整份文档重排。</summary>
    public void SetFontFamily(string fontFamily)
    {
        _fontFamily = fontFamily;
        _renderLoop?.Send(renderer => renderer.SetFontFamily(fontFamily));
    }

    /// <summary>切换行距。渲染线程收到后整份文档重排。</summary>
    public void SetLineGap(double lineGap)
    {
        if (Math.Abs(_lineGap - lineGap) < 0.01) return;
        _lineGap = lineGap;
        _renderLoop?.Send(renderer => renderer.SetLineGap(lineGap));
    }

    /// <summary>切换横向对齐。渲染线程收到后整份文档重排。</summary>
    public void SetAlignment(LyricsAlignment alignment)
    {
        if (_alignment == alignment) return;
        _alignment = alignment;
        _renderLoop?.Send(renderer => renderer.SetAlignment(alignment));
    }

    /// <summary>切换水平偏移，占歌词区域宽度的百分比。</summary>
    public void SetHorizontalOffsetPercent(double percent)
    {
        if (Math.Abs(_horizontalOffsetPercent - percent) < 0.01) return;
        _horizontalOffsetPercent = percent;
        _renderLoop?.Send(renderer => renderer.SetHorizontalOffsetPercent(percent));
    }

    /// <summary>切换垂直偏移，占歌词区域高度的百分比。</summary>
    public void SetVerticalOffsetPercent(double percent)
    {
        if (Math.Abs(_verticalOffsetPercent - percent) < 0.01) return;
        _verticalOffsetPercent = percent;
        _renderLoop?.Send(renderer => renderer.SetVerticalOffsetPercent(percent));
    }

    /// <summary>切换已唱部分的高亮色。只换颜色纹理，不改排版。</summary>
    public void SetHighlightColor(Color color)
    {
        if (_highlightColor == color) return;
        _highlightColor = color;
        _renderLoop?.Send(renderer => renderer.SetPlayedColor(color));
    }

    public void ResumeFollowing() => _renderLoop?.Send(renderer => renderer.ResumeFollowing());

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_loaded) return;
        _loaded = true;
        _broken = false;
        _clock.SetDuration(_engine.Duration);
        // 恢复态（重启后还没点播放）引擎里没有文件、Position 是 0；起点取自视图模型，
        // 它会在引擎闲置时退回上次的续播位置，进页就直接停在对应的那一句。
        _clock.Sync(_viewModel.TimelinePosition, force: true);
        _lastEngineState = _engine.State;
        _lastEnginePosition = _engine.Position;
        _waitingForPlaybackToAdvance = _engine.State == PlaybackState.Playing;
        _clock.SetPlaying(false);
        if (_waitingForPlaybackToAdvance) StartPlaybackProbe();
        _engine.PositionChanged += OnPositionChanged;
        _engine.StateChanged += OnStateChanged;
        _engine.SeekPending += OnSeekPending;

        _cacheTrimTimer = DispatcherQueue.CreateTimer();
        _cacheTrimTimer.Interval = CacheTrimInterval;
        _cacheTrimTimer.IsRepeating = true;
        _cacheTrimTimer.Tick += OnCacheTrimTick;
        _cacheTrimTimer.Start();
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
        StopPlaybackProbe();
        _engine.PositionChanged -= OnPositionChanged;
        _engine.StateChanged -= OnStateChanged;
        _engine.SeekPending -= OnSeekPending;

        if (_cacheTrimTimer is not null)
        {
            _cacheTrimTimer.Stop();
            _cacheTrimTimer.Tick -= OnCacheTrimTick;
            _cacheTrimTimer = null;
        }
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ResetPointerGesture();
        if (_subscribedRoot is not null) _subscribedRoot.Changed -= OnXamlRootChanged;
        _subscribedRoot = null;
        ReleaseSurface();

        // 页面离开后把常驻设备这一轮用过的缓存还回去。
        BackgroundCanvasDeviceLease.TrimShared();
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
        loop.Send(renderer => renderer.SetFontFamily(_fontFamily));
        loop.Send(renderer => renderer.SetLineGap(_lineGap));
        loop.Send(renderer => renderer.SetAlignment(_alignment));
        loop.Send(renderer => renderer.SetHorizontalOffsetPercent(_horizontalOffsetPercent));
        loop.Send(renderer => renderer.SetVerticalOffsetPercent(_verticalOffsetPercent));
        loop.Send(renderer => renderer.SetPlayedColor(_highlightColor));
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
        // 点译文行时回到它上方的原文行：译文的时间戳属于下一句。
        var position = _viewModel.StartForLine(line);
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
        // 新歌加载期间，上一首迟到的进度事件不能把歌词带到旧位置。
        if (_lastEngineState == PlaybackState.Loading)
        {
            return;
        }

        _clock.SetDuration(args.Duration);
        if (!TryStartPlaybackClock(args.Position))
        {
            _clock.Sync(args.Position);
        }

        _lastEnginePosition = args.Position;
        _renderLoop?.Invalidate();
    }
    /// <summary>
    /// 跳转已发出、声音还没接上。
    /// </summary>
    /// <remarks>
    /// mpv 在 seek 完成前就把位置报成目标值，若照常外推，歌词会先滚到后面、声音才到。
    /// 这里把时钟停住并清掉「上次位置」，等进度真的前进后再由
    /// <see cref="TryStartPlaybackClock"/> 接上；拖动后的第一帧仍会 Sync 到目标句，
    /// 所以歌词是「跳到目标句、停住、有声音后继续」，不是从头继续外推。
    /// </remarks>
    private void OnSeekPending(object? sender, SeekPendingEventArgs args)
    {
        if (_lastEngineState is not (PlaybackState.Playing or PlaybackState.Paused)) return;

        _waitingForSeekAdvance = true;
        _seekTarget = args.Target;
        _lastEnginePosition = null;
        _waitingForPlaybackToAdvance = true;
        _clock.SetPlaying(false);
        StartPlaybackProbe();
        _renderLoop?.Invalidate();
    }

    /// <summary>
    /// 播放期间定时归还常驻设备的缓存。
    /// </summary>
    /// <remarks>
    /// 逐字高亮与长音发光每帧都会碰设备缓存；驱动把已经释放的块留在自己的堆里，
    /// 私有提交与显存会停在「唱过的整首歌」的峰值。跳转、换歌时已经会 Trim，
    /// 这里兜住「一直顺着播」的情况；暂停时不 Trim，等恢复后再说。
    /// </remarks>
    private void OnCacheTrimTick(DispatcherQueueTimer sender, object args)
    {
        if (_engine.State == PlaybackState.Playing) BackgroundCanvasDeviceLease.TrimShared();
    }

    private void OnStateChanged(object? sender, PlaybackStateChangedEventArgs args)
    {
        var previous = _lastEngineState;
        _lastEngineState = args.State;

        if (args.State == PlaybackState.Loading)
        {
            // 新歌开始加载：上一首的位置不能再拿来判断“是否已经前进”。
            _lastEnginePosition = null;
            _waitingForPlaybackToAdvance = false;
            _waitingForSeekAdvance = false;
            _clock.SetPlaying(false);
        }
        else if (args.State == PlaybackState.Playing)
        {
            // 暂停后恢复：音频已经就绪，直接续上；新加载则等位置前进。
            // 跳转还没等到声音的，也要继续等位置越过目标，不能当成普通恢复。
            var resume = previous == PlaybackState.Paused && !_waitingForSeekAdvance
                && _lastEnginePosition is { } position && position > TimeSpan.Zero;
            _waitingForPlaybackToAdvance = !resume;
            _clock.SetPlaying(resume);
        }
        else
        {
            _waitingForPlaybackToAdvance = false;
            _clock.SetPlaying(false);
            if (args.State is PlaybackState.Stopped or PlaybackState.Idle)
            {
                _lastEnginePosition = null;
            }
        }

        if (_waitingForPlaybackToAdvance) StartPlaybackProbe();
        else StopPlaybackProbe();
        _renderLoop?.Invalidate();
    }

    /// <summary>
    /// 位置真正变了才说明音频开始出了。同一位置的重复上报（缓冲中）不启动时钟。
    /// </summary>
    private bool TryStartPlaybackClock(TimeSpan position)
    {
        if (!_waitingForPlaybackToAdvance) return false;

        if (_waitingForSeekAdvance)
        {
            // mpv 在 seek 完成前会把位置报成目标值，之后才继续前进。
            // 只认「越过目标」，否则刚拖完就会把时钟重新启动，出现歌词先滚。
            if (position < _seekTarget + SeekSettledThreshold)
            {
                _lastEnginePosition = position;
                return false;
            }

            _waitingForSeekAdvance = false;

            // 跳转落定：这次跨行新建又丢弃的整行位图交给设备缓存，
            // 常驻设备不会自己还，主动归还一次，避免拖几次就停在峰值。
            BackgroundCanvasDeviceLease.TrimShared();
        }
        else
        {
            if (_lastEnginePosition is not { } last)
            {
                _lastEnginePosition = position;
                return false;
            }
            if (position == last) return false;
        }

        _waitingForPlaybackToAdvance = false;
        StopPlaybackProbe();
        _clock.Sync(position);
        _clock.SetPlaying(true);
        _lastEnginePosition = position;
        _renderLoop?.Invalidate();
        return true;
    }

    /// <summary>
    /// 启动探测。PositionChanged 走低优先级队列，页面忙时会被压后；
    /// 50 ms 直接读引擎位置，让歌词在音频开始后尽快起步。
    /// </summary>
    private void StartPlaybackProbe()
    {
        if (_playbackProbeTimer is null)
        {
            _playbackProbeTimer = DispatcherQueue.CreateTimer();
            _playbackProbeTimer.Interval = TimeSpan.FromMilliseconds(50);
            _playbackProbeTimer.IsRepeating = true;
            _playbackProbeTimer.Tick += OnPlaybackProbeTick;
        }

        _playbackProbeTimer.Start();
    }

    private void StopPlaybackProbe() => _playbackProbeTimer?.Stop();

    private void OnPlaybackProbeTick(DispatcherQueueTimer sender, object args)
    {
        if (!_waitingForPlaybackToAdvance || _engine.State != PlaybackState.Playing)
        {
            sender.Stop();
            return;
        }

        TryStartPlaybackClock(_engine.Position);
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
