using System.ComponentModel;
using Bodian.Core.Playback;
using Bodian.WinUI.LyricRenderer;
using Bodian.WinUI.Playback;
using Bodian.WinUI.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Bodian.WinUI.Controls;

/// <summary>
/// Win2D 歌词宿主。只做四件事：转发生命周期、转发指针、把 UI 线程的变动投到渲染线程、喂时钟。
/// </summary>
/// <remarks>
/// <para>
/// <b>位置源只有 5Hz</b>（引擎按 200ms 节流上报），逐字高亮要按帧推进，
/// 所以中间隔一层 <see cref="LyricsPlaybackClock"/> 做插值。
/// </para>
/// <para>
/// <b>渲染线程零写入。</b> 时钟用不可变锚点交换引用，渲染器只读；
/// UI 线程要改渲染器状态（换歌、视口变化）一律走 <c>RunOnGameLoopThreadAsync</c> 投递，
/// 不是直接赋值。
/// </para>
/// <para>
/// <b>三条渲染线程的禁忌</b>（都是踩过的）：
/// </para>
/// <list type="number">
/// <item><b>不读任何 XAML 属性</b>。<c>ActualWidth</c> / <c>ActualHeight</c> 只能在 UI 线程读，
/// 在 <c>CreateResources</c> 里读会以 <c>E_INVALIDARG</c> 崩掉进程。所以视口尺寸缓存在字段里，
/// 由 UI 线程的 <c>Loaded</c> / <c>SizeChanged</c> 写。</item>
/// <item><b>不用控件当资源创建者</b>，用它的 <c>Device</c>。排版是懒建的（在 <c>Update</c> 里），
/// 而用控件建资源只在 <c>CreateResources</c> 期间被允许。</item>
/// <item><b>不在这里读播放器的属性</b>，只读时钟 —— 引擎属性会碰 libmpv。</item>
/// </list>
/// <para>
/// <b>渲染回调里 catch 住异常。</b> 渲染线程上未处理的异常会把整个进程带走，而且堆栈不会进日志
/// （WinUI 只给一句 <c>STATUS_STOWED_EXCEPTION</c>）。这里记下并停掉循环：画面停在最后一帧，
/// 但进程活着、日志里有完整堆栈。
/// </para>
/// </remarks>
public sealed partial class LyricsCanvasView : UserControl
{
    private readonly LyricsViewModel _viewModel;
    private readonly IPlaybackService _engine;
    private readonly ILogger<LyricsCanvasView> _logger;
    private readonly LyricsPlaybackClock _clock = new(TimeProvider.System);
    private readonly LyricsRenderer _renderer;

    /// <summary>视口尺寸。由 UI 线程写，渲染线程读（见类注释的禁忌 1）。</summary>
    private double _viewportWidth;

    private double _viewportHeight;

    private bool _subscribed;
    private bool _paused;
    private bool _broken;

    public LyricsCanvasView(
        LyricsViewModel viewModel,
        IPlaybackService engine,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(engine);

        // 注入 ILoggerFactory 而不是 ILogger<T>：渲染器要自己那一类的 logger，
        // 从 ILogger<LyricsCanvasView> 转不过去（泛型类型不协变）。
        var factory = loggerFactory ?? NullLoggerFactory.Instance;

        _viewModel = viewModel;
        _engine = engine;
        _logger = factory.CreateLogger<LyricsCanvasView>();
        _renderer = new LyricsRenderer(LyricsRenderSettings.Default, factory.CreateLogger<LyricsRenderer>());

        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ActualThemeChanged += (_, _) => SyncThemeColors();
    }

    /// <summary>渲染循环的开关。歌词页不活跃或窗口不可见时为 <c>true</c>。</summary>
    /// <remarks>
    /// <b>显式控制，不依赖 Win2D 的隐式省电行为</b> —— 那个行为没有官方承诺。
    /// </remarks>
    public bool IsPaused
    {
        get => _paused;
        set
        {
            if (_paused == value)
            {
                return;
            }

            _paused = value;
            Canvas.Paused = value;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SyncThemeColors();

        // 视口尺寸只在 UI 线程量，量完投给渲染线程。
        MeasureViewport();
        Post(() => _renderer.SetViewport(_viewportWidth, _viewportHeight));

        if (_subscribed)
        {
            return;
        }

        _subscribed = true;

        _engine.PositionChanged += OnPositionChanged;
        _engine.StateChanged += OnStateChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>
    /// 退场：退订 + 放掉设备资源。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>必须退订。</b> 引擎与 ViewModel 都是单例，订阅挂在它们身上；不退订的话
    /// 每进一次歌词页就漏一份处理器，而且退场之后还会继续被调用。
    /// </para>
    /// <para>
    /// <b>可以在这里释放设备资源</b>：控件离开可视树之后 Win2D 就停了游戏循环，
    /// 不会再有在途的 <c>Draw</c>。这也是 Win2D 对「<c>CreateResources</c> 里建的东西」的
    /// 官方释放时机 —— 没有对应的 <c>ReleaseResources</c> 回调。
    /// </para>
    /// </remarks>
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_subscribed)
        {
            _subscribed = false;

            _engine.PositionChanged -= OnPositionChanged;
            _engine.StateChanged -= OnStateChanged;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _renderer.Dispose();
    }

    private void OnCreateResources(CanvasAnimatedControl sender, CanvasCreateResourcesEventArgs args)
    {
        Guarded(nameof(OnCreateResources), () =>
        {
            // 设备相关资源只能在这里建。设备丢失时本方法会以 NewDevice 重入，整体换代。
            // ★ 传 Device 而不是 sender，见类注释的禁忌 2。
            _renderer.RebuildDeviceResources(sender.Device);

            // 换设备之后排版也要重建，所以这里重新同步一次当前文档与视口。
            _renderer.SetDocument(_viewModel.Document);
            _renderer.SetViewport(_viewportWidth, _viewportHeight);
        });
    }

    private void OnUpdate(ICanvasAnimatedControl sender, CanvasAnimatedUpdateEventArgs args)
        => Guarded(nameof(OnUpdate), () => _renderer.Update(_clock.Position, args.Timing.TotalTime, _clock.JumpCount));

    private void OnDraw(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args)
        => Guarded(nameof(OnDraw), () => _renderer.Draw(args.DrawingSession));

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _viewportWidth = e.NewSize.Width;
        _viewportHeight = e.NewSize.Height;

        var width = _viewportWidth;
        var height = _viewportHeight;
        Post(() => _renderer.SetViewport(width, height));
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Canvas);
        var index = _renderer.LineIndexAt(point.Position.Y);

        if (index >= 0)
        {
            _viewModel.SeekToLineCommand.Execute(index);
        }
    }

    private void OnPositionChanged(object? sender, PlaybackPositionChangedEventArgs e)
    {
        _clock.SetDuration(e.Duration);
        _clock.Sync(e.Position);
    }

    private void OnStateChanged(object? sender, PlaybackStateChangedEventArgs e)
        => _clock.SetPlaying(e.State == PlaybackState.Playing);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LyricsViewModel.Document))
        {
            return;
        }

        var document = _viewModel.Document;
        Post(() => _renderer.SetDocument(document));
    }

    /// <summary>在 UI 线程量视口尺寸。</summary>
    private void MeasureViewport()
    {
        _viewportWidth = Canvas.ActualWidth;
        _viewportHeight = Canvas.ActualHeight;
    }

    /// <summary>把一次状态改动投到渲染线程上。</summary>
    private void Post(DispatcherQueueHandler action)
    {
        if (_broken)
        {
            return;
        }

        try
        {
            _ = Canvas.RunOnGameLoopThreadAsync(action);
        }
        catch (Exception ex)
        {
            // 控件已经不在树里（页面切走了）。丢掉这次更新不影响正确性 ——
            // 下次进场时 CreateResources 会把当前文档重新同步一遍。
            _logger.LogDebug(ex, "投递到渲染线程失败（控件可能已经退场）");
        }
    }

    /// <summary>
    /// 包住渲染线程上的回调，别让异常带走进程。
    /// </summary>
    /// <remarks>
    /// 出错后<b>停掉循环</b>（而不是继续每帧抛一次）：画面停在最后一帧，日志里有完整堆栈。
    /// 修好之前那一帧也画不出来，但至少能看见原因。
    /// </remarks>
    private void Guarded(string callback, Action action)
    {
        if (_broken)
        {
            return;
        }

        try
        {
            action();
        }
        catch (Exception ex)
        {
            _broken = true;

            _logger.LogCritical(ex, "Win2D 歌词渲染在 {Callback} 中失败，已停掉渲染循环", callback);

            try
            {
                Canvas.Paused = true;
            }
            catch (Exception pauseEx)
            {
                // 暂停都失败就只能这样了，日志里已经有一条 Critical。
                _logger.LogDebug(pauseEx, "渲染循环暂停失败");
            }
        }
    }

    /// <summary>
    /// 定歌词的配色：底色与页面一致，文字颜色按<b>实际主题</b>直接定。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>文字颜色刻意不读主题画刷。</b> 读画刷拿到的是解析当时的主题值，一旦与底色不配对，
    /// 就是白字配白底 —— 字都在，一个也看不见。这里按
    /// <see cref="FrameworkElement.ActualTheme"/> 直接定：浅底深字、深底浅字。
    /// </para>
    /// <para>
    /// 底色仍然优先用页面的主题画刷（这样和搜索页、播放条一致），取不到才退回保底色。
    /// </para>
    /// </remarks>
    private void SyncThemeColors()
    {
        var dark = ActualTheme == ElementTheme.Dark;

        var background = (Host.Background as SolidColorBrush)?.Color
                         ?? (dark
                             ? Windows.UI.Color.FromArgb(255, 32, 32, 32)
                             : Windows.UI.Color.FromArgb(255, 243, 243, 243));

        var played = dark
            ? Windows.UI.Color.FromArgb(255, 255, 255, 255)
            : Windows.UI.Color.FromArgb(255, 24, 24, 24);

        var unplayed = dark
            ? Windows.UI.Color.FromArgb(255, 148, 148, 154)
            : Windows.UI.Color.FromArgb(255, 132, 132, 138);

        Canvas.ClearColor = background;

        _logger.LogInformation(
            "歌词配色：主题 {Theme}，底色 {Background}，已唱 {Played}，未唱 {Unplayed}",
            ActualTheme,
            background,
            played,
            unplayed);

        Post(() => _renderer.SetColors(played, unplayed));
    }
}
