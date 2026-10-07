using Bodian.Core.Models;
using Bodian.Core.Models.Lyrics;
using Bodian.Core.Playback;
using Bodian.Core.Services.Abstractions;
using Bodian.WinUI.Controls;
using Bodian.WinUI.Playback;
using Bodian.WinUI.Media;
using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;

namespace Bodian.WinUI.Views;

/// <summary>透明置顶小窗：一条横向播放条，可拖动、可贴左右边收起、队列面板按空间择向展开。</summary>
/// <remarks>窗口透明、置顶、圆角与贴边收起的原生实现见 docs/mini-player.md。</remarks>
public sealed partial class MiniPlayerWindow : Window
{
    /// <summary>内容条尺寸（DIP）。</summary>
    /// <remarks>
    /// 高度必须与 XAML 里封面容器的 <c>Width</c>（64）一致，否则封面就不是正方形。
    /// 内容行的 56 + 进度行的 8 也正好凑成这个数。
    /// </remarks>
    private const double BarWidthDip = 360;
    private const double BarHeightDip = 64;

    /// <summary>四个角的半径（DIP）。窗口逐像素透明，所以这是真抗锯齿圆角，不靠 DWM。</summary>
    private const double CornerRadiusDip = 12;

    /// <summary>贴边收起后留下的窄条宽度（DIP）。够看见就行，唤起靠光标轮询不靠点中它。</summary>
    private const double CollapsedWidthDip = 8;

    /// <summary>唤起区：贴边侧往内多宽、纵向各自放宽多少（DIP）。</summary>
    private const double WakeBandDip = 8;
    private const double WakeSlackDip = 8;

    /// <summary>拖动时的屏幕四边吸附距离（DIP）。与桌面歌词同一个值。</summary>
    private const double SnapDistanceDip = 16;

    /// <summary>队列面板的理想高度、最小高度，以及它与内容条之间的透明间隙（DIP）。</summary>
    private const double QueuePanelHeightDip = 320;
    private const double QueueMinimumHeightDip = 120;

    /// <summary>悬停抽屉的高度（DIP）。一行歌名 + 歌手，不用更高。</summary>
    private const double DrawerHeightDip = 36;
    private const double DrawerMinimumHeightDip = 28;

    /// <summary>
    /// 内容条与面板之间的间距（DIP）。
    /// </summary>
    /// <remarks>
    /// <b>0 是有意的：两者要连成一块</b>，不是两张分开的卡片。所以外轮廓是「圆角在两头、
    /// 中间接缝处是直角」，由 <see cref="ApplyChrome"/> 按方向统一分配；面板自带标题行，
    /// 不需要额外的分隔线。
    /// </remarks>
    private const double PanelGapDip = 0;

    /// <summary>首次出现时距工作区右下角各留多少（DIP）。</summary>
    private const double DefaultMarginDip = 24;

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(30);
    private static readonly TimeSpan IdlePollInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan TopmostInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan CollapseDelay = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan PlacementSaveDelay = TimeSpan.FromSeconds(1);

    private readonly MiniPlayerViewModel _settings;
    private readonly IMiniPlayerPlacementStore _placement;
    private readonly IWindowHandleProvider _mainWindow;
    private readonly ILogger _logger;
    private readonly IPlaybackService _engine;
    private readonly LyricsPlaybackClock _lyricClock = new(TimeProvider.System);
    private LyricLine? _miniLyricLine;
    private double _miniLyricWidth, _miniLyricHeight;
    private DispatcherQueueTimer? _lyricTimer;
    private bool _subscriptionsActive;
    private bool _bindingsSuspended;
    private PlayQueuePanel? _queueView;
    private readonly nint _handle;

    private DispatcherQueueTimer? _pollTimer;
    private DispatcherQueueTimer? _topmostTimer;
    private DispatcherQueueTimer? _placementTimer;
    private DispatcherQueueTimer? _collapseTimer;

    /// <summary>内容条自身的矩形（物理像素，**永远是展开态**）。收起、面板、抽屉都从它派生。</summary>
    private WindowPlacement _bar = new(0, 0, 0, 0);

    /// <summary>当前窗口的完整矩形，光标命中判定用。</summary>
    private WindowPlacement _windowRect = new(0, 0, 0, 0);

    private MiniPlayerDockEdge _edge = MiniPlayerDockEdge.None;
    private bool _collapsed;
    private bool _armed = true;
    private bool _hovering;
    private bool _drawerOpen;
    private bool _panelOpen;
    private bool _flyoutOpen;
    private bool _dragging;
    private bool _windowVisible;
    private bool _everShown;
    private double _scale;
    private NativeMethods.NativePoint _gestureCursorOrigin;
    private WindowPlacement _gestureBarOrigin = new(0, 0, 0, 0);

    public PlayerViewModel Player { get; }

    public LyricsViewModel Lyrics { get; }

    public PlayQueueViewModel Queue { get; }

    public ThemeViewModel Theme { get; }

    public MiniPlayerWindow(
        MiniPlayerViewModel settings,
        PlayerViewModel player,
        LyricsViewModel lyrics,
        IPlaybackService engine,
        PlayQueueViewModel queue,
        ThemeViewModel theme,
        IMiniPlayerPlacementStore placement,
        IWindowHandleProvider mainWindow,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(lyrics);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(mainWindow);

        _settings = settings;
        Player = player;
        Lyrics = lyrics;
        _engine = engine;
        Queue = queue;
        Theme = theme;
        _placement = placement;
        _mainWindow = mainWindow;
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<MiniPlayerWindow>();

        InitializeComponent();

        _handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _scale = NativeMethods.GetDpiForWindow(_handle) / 96.0;

        ConfigureWindowForm();
        ApplyPlacement();
        ApplyChrome(null);
        RefreshInfoSlot();

        // 拖动挂在整块根面板上：按钮自己会吃掉指针事件（标记为已处理），所以不会误触；
        // 挂外层换来的是「随便按哪儿都能拖」。
        Root.PointerPressed += OnRootPointerPressed;
        Root.PointerMoved += OnRootPointerMoved;
        Root.PointerReleased += OnRootPointerReleased;
        Root.PointerCaptureLost += (_, _) => EndDrag();

        // ★ handledEventsToo: true 不能省。Slider 内部会把指针事件标记为已处理，
        //   用 XAML 的 PointerPressed="..." 或 AddHandler(..., false) 都收不到 ——
        //   症状是「拖得动，但进度不受影响」。播放条那颗进度条用的是同一条路。
        ProgressSlider.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnProgressPressed), true);
        ProgressSlider.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnProgressReleased), true);
        ProgressSlider.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(OnProgressCaptureLost), true);
        ProgressSlider.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(OnProgressCaptureLost), true);

        Bindings.StopTracking();
        _bindingsSuspended = true;

        Closed += (_, _) => TearDown();
    }

    /// <summary>把窗口藏起来。宿主在关闭小窗时调它。</summary>
    public void HideWindow()
    {
        _windowVisible = false;
        _hovering = false;
        _drawerOpen = false;
        _panelOpen = false;
        _collapsed = false;
        _flyoutOpen = false;
        VolumeFlyout.Hide();
        _edge = MiniPlayerDockEdge.None;
        Queue.IsMiniPlayerOpen = false;
        ReleaseQueueView();
        SuspendVisualUpdates();

        StopTimers();
        SavePlacement();

        // 收起态的窄条是穿透的，藏起来之前必须把穿透关掉 —— 否则下次显示时
        // 窗口会先以「点不着」的状态出现，直到轮询跑到下一帧。
        SetPassThrough(false);
        AppWindow.Hide();
    }

    /// <summary>显示窗口。</summary>
    /// <remarks>
    /// <b>第一次用 <c>Activate()</c>，之后用 <c>AppWindow.Show(false)</c>。</b>
    /// 刚创建、从没激活过的窗口不会自己开始渲染，必须激活一次；
    /// 而再次显示时绝不能激活 —— 那会把焦点从用户正在做的事上抢走。
    /// </remarks>
    public void ShowWindow()
    {
        _windowVisible = true;
        ResumeVisualUpdates();
        RefreshInfoSlot();

        if (_everShown)
        {
            AppWindow.Show(activateWindow: false);
        }
        else
        {
            _everShown = true;
            Activate();
        }

        KeepOnTop();
        StartTimers();
        ApplyLayout();
    }

    // ── 窗口形态 ────────────────────────────────────────────────────────────

    private void ConfigureWindowForm()
    {
        SystemBackdrop = new TransparentBackdrop(_handle, _logger);

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        // ★ 不加 WS_EX_NOACTIVATE：小窗里有音量 Popup 和一堆要点的按钮，
        //   无焦点的悬浮窗上原生弹层会抢占/丢失指针捕获（桌面歌词踩过，它的解法是把面板留在窗口内）。
        //   加 WS_EX_TOOLWINDOW：不进 Alt+Tab、不占任务栏 —— 主窗口同时开着，任务栏多一条很乱。
        var exStyle = (long)NativeMethods.GetWindowLongPtr(_handle, NativeMethods.GwlExStyle);
        NativeMethods.SetWindowLongPtr(
            _handle,
            NativeMethods.GwlExStyle,
            (nint)(exStyle | NativeMethods.WsExLayered | NativeMethods.WsExToolWindow));

        var style = (long)NativeMethods.GetWindowLongPtr(_handle, NativeMethods.GwlStyle);
        NativeMethods.SetWindowLongPtr(
            _handle,
            NativeMethods.GwlStyle,
            (nint)(style & ~(NativeMethods.WsCaption | NativeMethods.WsThickFrame)));

        NativeMethods.SetWindowPos(
            _handle,
            0,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder
                | NativeMethods.SwpNoActivate | NativeMethods.SwpFrameChanged);

        // 只为「别让 Win11 再加一层系统圆角/细边框」。我们的圆角是 XAML 画的，
        // 这一调在 Win10 上返回 E_INVALIDARG 且什么也不做 —— 失败是预期内的，不是异常。
        var corner = NativeMethods.DwmcpDoNotRound;
        _ = NativeMethods.DwmSetWindowAttribute(
            _handle,
            NativeMethods.DwmwaWindowCornerPreference,
            ref corner,
            sizeof(int));
    }

    /// <remarks>
    /// 只恢复位置，不恢复「贴边收起」—— 收起是临时让路的状态，重启后小窗应该看得见。
    /// 尺寸也不记：它是固定的，记下来只会和内容打架。
    /// </remarks>
    private void ApplyPlacement()
    {
        var saved = _placement.Load();
        var work = WorkAreaFor(saved);
        var width = Scaled(BarWidthDip);
        var height = Scaled(BarHeightDip);

        _bar = saved is not null
            ? DesktopLyricsWindowGeometry.Constrain(new WindowPlacement(saved.X, saved.Y, width, height), work)
            : DesktopLyricsWindowGeometry.Constrain(
                new WindowPlacement(
                    work.X + work.Width - width - Scaled(DefaultMarginDip),
                    work.Y + work.Height - height - Scaled(DefaultMarginDip),
                    width,
                    height),
                work);
    }

    /// <summary>把三个圆角卡片的角半径按当前展开方向对齐，衔接处才不会露出直角。</summary>
    private void ApplyChrome(MiniPlayerPanelSide? side)
    {
        var all = new CornerRadius(CornerRadiusDip);
        var top = new CornerRadius(CornerRadiusDip, CornerRadiusDip, 0, 0);
        var bottom = new CornerRadius(0, 0, CornerRadiusDip, CornerRadiusDip);

        var coverAll = new CornerRadius(CornerRadiusDip, 0, 0, CornerRadiusDip);
        var coverTop = new CornerRadius(CornerRadiusDip, 0, 0, 0);
        var coverBottom = new CornerRadius(0, 0, 0, CornerRadiusDip);

        switch (side)
        {
            case MiniPlayerPanelSide.Down:
                Bar.CornerRadius = top;
                QueueCard.CornerRadius = bottom;
                TrackDrawer.CornerRadius = bottom;
                Cover.CornerRadius = coverTop;
                CoverFallback.CornerRadius = coverTop;
                break;

            case MiniPlayerPanelSide.Up:
                Bar.CornerRadius = bottom;
                QueueCard.CornerRadius = top;
                TrackDrawer.CornerRadius = top;
                Cover.CornerRadius = coverBottom;
                CoverFallback.CornerRadius = coverBottom;
                break;

            default:
                Bar.CornerRadius = all;
                QueueCard.CornerRadius = all;
                TrackDrawer.CornerRadius = all;
                Cover.CornerRadius = coverAll;
                CoverFallback.CornerRadius = coverAll;
                break;
        }

        CollapsedStrip.CornerRadius = all;
    }

    /// <summary>
    /// 由 <see cref="_bar"/> 与当前状态（收起 / 队列面板 / 悬停抽屉）算出窗口矩形并应用。
    /// </summary>
    /// <remarks>
    /// <b>一切的源头是 <see cref="_bar"/>。</b> 队列面板与抽屉只是让窗口在某个方向上变高，
    /// 条本身的屏幕位置一动不动；贴边收起则只是把宽度缩到窄条、贴边那一侧不动。
    /// 这样「条的位置」与「窗口多大」是两件事，不会互相污染，也不会把窄条尺寸写进磁盘。
    /// </remarks>
    private void ApplyLayout()
    {
        var work = WorkArea();

        WindowPlacement rect;
        MiniPlayerPanelPlan? plan = null;
        var queue = false;

        if (_collapsed && _edge != MiniPlayerDockEdge.None)
        {
            rect = MiniPlayerGeometry.Collapse(_bar, _edge, Scaled(CollapsedWidthDip));
        }
        else if (_panelOpen)
        {
            plan = MiniPlayerGeometry.PlanPanel(
                _bar, work, Scaled(QueuePanelHeightDip), Scaled(PanelGapDip), Scaled(QueueMinimumHeightDip));

            if (plan is null)
            {
                // 上下都塞不下面板就不开 —— 与其挤成一条缝，不如什么都不发生。
                _panelOpen = false;
                Queue.IsMiniPlayerOpen = false;
            }

            queue = true;
            rect = plan?.Window ?? _bar;
        }
        else if (_drawerOpen)
        {
            plan = MiniPlayerGeometry.PlanDrawer(
                _bar, work, Scaled(DrawerHeightDip), Scaled(PanelGapDip), Scaled(DrawerMinimumHeightDip));

            if (plan is null)
            {
                _drawerOpen = false;
            }

            rect = plan?.Window ?? _bar;
        }
        else
        {
            rect = _bar;
        }

        ShowPanel(plan, queue);
        ApplyChrome(plan?.Side);
        ApplyWindowRect(rect);
    }

    private void ShowPanel(MiniPlayerPanelPlan? plan, bool queue)
    {
        if (plan is null)
        {
            PanelHost.Visibility = Visibility.Collapsed;
            ReleaseQueueView();
            return;
        }
        if (queue) EnsureQueueView();
        else ReleaseQueueView();

        PanelHost.Visibility = Visibility.Visible;
        PanelHost.Height = plan.Value.PanelHeight;
        Grid.SetRow(PanelHost, plan.Value.Side == MiniPlayerPanelSide.Up ? 0 : 2);
        PanelHost.Margin = plan.Value.Side == MiniPlayerPanelSide.Up
            ? new Thickness(0, 0, 0, Scaled(PanelGapDip))
            : new Thickness(0, Scaled(PanelGapDip), 0, 0);

        // 抽屉里一次只装一样：队列面板优先，其次是歌名那一行。
        QueueCard.Visibility = queue ? Visibility.Visible : Visibility.Collapsed;
        TrackDrawer.Visibility = queue ? Visibility.Collapsed : Visibility.Visible;
    }

    private void EnsureQueueView()
    {
        if (_queueView is not null) return;
        _queueView = new PlayQueuePanel { ViewModel = Queue };
        _queueView.ClearRequested += OnClearQueueRequested;
        _queueView.CloseRequested += OnCloseQueueRequested;
        QueueCard.Child = _queueView;
    }

    private void ReleaseQueueView()
    {
        if (_queueView is null) return;
        _queueView.ClearRequested -= OnClearQueueRequested;
        _queueView.CloseRequested -= OnCloseQueueRequested;
        QueueCard.Child = null;
        _queueView.ClearValue(PlayQueuePanel.ViewModelProperty);
        _queueView = null;
    }

    private void ApplyWindowRect(WindowPlacement rect)
    {
        _windowRect = rect;

        var position = AppWindow.Position;
        var size = AppWindow.Size;
        if (position.X == rect.X && position.Y == rect.Y && size.Width == rect.Width && size.Height == rect.Height)
        {
            return;
        }

        AppWindow.MoveAndResize(new RectInt32(rect.X, rect.Y, rect.Width, rect.Height));
    }

    // ── 定时器 ──────────────────────────────────────────────────────────────

    /// <remarks>定时器只创建一次，窗口藏起来时全部停掉。</remarks>
    private void StartTimers()
    {
        if (_pollTimer is null)
        {
            _pollTimer = DispatcherQueue.CreateTimer();
            _pollTimer.Interval = IdlePollInterval;
            _pollTimer.IsRepeating = true;
            _pollTimer.Tick += (_, _) => PollCursor();
        }

        if (_topmostTimer is null)
        {
            _topmostTimer = DispatcherQueue.CreateTimer();
            _topmostTimer.Interval = TopmostInterval;
            _topmostTimer.IsRepeating = true;
            _topmostTimer.Tick += (_, _) =>
            {
                if (!_dragging && !_panelOpen)
                {
                    KeepOnTop();
                }
            };
        }

        if (_placementTimer is null)
        {
            _placementTimer = DispatcherQueue.CreateTimer();
            _placementTimer.Interval = PlacementSaveDelay;
            _placementTimer.IsRepeating = false;
            _placementTimer.Tick += (_, _) => SavePlacement();
        }

        if (_collapseTimer is null)
        {
            _collapseTimer = DispatcherQueue.CreateTimer();
            _collapseTimer.Interval = CollapseDelay;
            _collapseTimer.IsRepeating = false;
            _collapseTimer.Tick += (_, _) => CollapseToDock();
        }

        _pollTimer.Start();
        _topmostTimer.Start();
    }

    private void StopTimers()
    {
        StopLyricTimer();
        _pollTimer?.Stop();
        _topmostTimer?.Stop();
        _placementTimer?.Stop();
        _collapseTimer?.Stop();
    }

    private void QueuePlacementSave()
    {
        _placementTimer?.Stop();
        _placementTimer?.Start();
    }

    private void SavePlacement() => _placement.Save(_bar);

    private void KeepOnTop()
    {
        // 已经置顶时不反复重排 HWND，避免干扰透明表面、光标和弹层。
        var style = (long)NativeMethods.GetWindowLongPtr(_handle, NativeMethods.GwlExStyle);
        if ((style & NativeMethods.WsExTopmost) != 0)
        {
            return;
        }

        NativeMethods.SetWindowPos(_handle, NativeMethods.HwndTopmost, 0, 0, 0, 0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
    }

    // ── 贴边收起 ────────────────────────────────────────────────────────────

    /// <remarks>
    /// <b>一个定时器同时管两件事：收起态要不要展开，以及展开态要不要收回去。</b>
    /// 之所以不靠 PointerEntered/Exited，是因为收起态的窗口整条开着 WS_EX_TRANSPARENT，
    /// 那种状态下它收不到任何鼠标消息。
    /// </remarks>
    private void PollCursor()
    {
        if (!NativeMethods.GetCursorPos(out var cursor)) return;
        var nearWindow = cursor.X >= _windowRect.X - Scaled(WakeSlackDip)
            && cursor.X < _windowRect.X + _windowRect.Width + Scaled(WakeSlackDip)
            && cursor.Y >= _windowRect.Y - Scaled(WakeSlackDip)
            && cursor.Y < _windowRect.Y + _windowRect.Height + Scaled(WakeSlackDip);
        var interval = nearWindow || _dragging ? PollInterval : IdlePollInterval;
        if (_pollTimer is not null && _pollTimer.Interval != interval) _pollTimer.Interval = interval;
        if (nearWindow || _dragging) _scale = NativeMethods.GetDpiForWindow(_handle) / 96.0;

        if (_collapsed && _edge != MiniPlayerDockEdge.None)
        {
            var zone = MiniPlayerGeometry.WakeZone(
                _bar, WorkArea(), _edge, Scaled(WakeBandDip), Scaled(WakeSlackDip));
            var (wake, armed) = MiniPlayerGeometry.WakeTick(
                _armed, MiniPlayerGeometry.Contains(zone, cursor.X, cursor.Y));

            _armed = armed;

            if (wake)
            {
                ExpandFromDock();
            }

            return;
        }

        var inside = MiniPlayerGeometry.Contains(_windowRect, cursor.X, cursor.Y);
        SetHover(inside);

        if (_edge == MiniPlayerDockEdge.None || _dragging || _panelOpen || _flyoutOpen)
        {
            return;
        }

        if (inside)
        {
            _collapseTimer?.Stop();
        }
        else if (_collapseTimer is { IsRunning: false })
        {
            // 只启动一次。每次轮询都 Start 会重置倒计时，永远收不回去。
            _collapseTimer.Start();
        }
    }

    private void ExpandFromDock()
    {
        if (!_windowVisible)
        {
            return;
        }

        _bar = MiniPlayerGeometry.Expand(_bar, _edge, Scaled(BarWidthDip), WorkArea());
        _collapsed = false;
        ResumeVisualUpdates();

        // 光标此刻就在条上（这正是展开的原因），所以悬停态直接置位 ——
        // 窗口刚从穿透切回可命中，PointerEntered 不一定会补发。
        _hovering = true;
        _drawerOpen = !_panelOpen;

        SetPassThrough(false);
        RefreshInfoSlot();
        ApplyLayout();
    }

    private void CollapseToDock()
    {
        // 窗口已经藏起来时不动作：200 ms 的收起计时器可能正好和 HideWindow 撞上。
        if (!_windowVisible || _edge == MiniPlayerDockEdge.None || _collapsed || _dragging || _panelOpen
            || _flyoutOpen)
        {
            return;
        }

        _collapsed = true;
        _armed = false;
        _hovering = false;
        _drawerOpen = false;

        SetPassThrough(true);
        SuspendVisualUpdates();
        RefreshInfoSlot();
        ApplyLayout();
        QueuePlacementSave();
    }

    /// <remarks>
    /// <b>收起态的窄条必须穿透。</b> 它贴在屏幕边上、整条高度，不穿透就会吃掉
    /// 下面那个窗口在这一条上的点击。
    /// </remarks>
    private void SetPassThrough(bool passThrough)
    {
        var exStyle = (long)NativeMethods.GetWindowLongPtr(_handle, NativeMethods.GwlExStyle);
        var updated = passThrough
            ? exStyle | NativeMethods.WsExTransparent
            : exStyle & ~NativeMethods.WsExTransparent;

        if (updated != exStyle) NativeMethods.SetWindowLongPtr(_handle, NativeMethods.GwlExStyle, (nint)updated);
    }

    // ── 悬停：信息区换传输按钮 + 弹歌名抽屉 ─────────────────────────────────

    /// <summary>
    /// 设置悬停态。鼠标离开时抽屉<b>立即</b>收起，不留宽限 —— 它是悬浮时的附加信息，不是常驻面板。
    /// </summary>
    /// <remarks>
    /// <b>由光标轮询驱动，不用 PointerEntered / PointerExited。</b> 窗口每次为抽屉或面板
    /// 改尺寸，那两个事件都可能假触发一次：抽屉展开 → 假 PointerExited → 抽屉收起 →
    /// 假 PointerEntered → 又展开。按窗口矩形判定没有这个问题，而且收起态的窗口整条
    /// 开着 WS_EX_TRANSPARENT、本来也收不到鼠标消息。
    /// </remarks>
    private void SetHover(bool hovering)
    {
        if (_hovering == hovering)
        {
            return;
        }

        _hovering = hovering;

        // 音量弹层开着时窗口被钉住。弹层落在窗口外面，鼠标要移出去才够得着 ——
        // 一改布局（抽屉收起、窗口缩回 64 DIP）Flyout 就会被重新定位甚至直接关掉，
        // 表现是「弹出来了但点不到，一下就没了」。
        if (_flyoutOpen)
        {
            return;
        }

        _drawerOpen = hovering && !_dragging && !_panelOpen;

        RefreshInfoSlot();
        ApplyLayout();
    }

    private void OnVolumeFlyoutOpened(object? sender, object e) => _flyoutOpen = true;

    /// <summary>
    /// 滑条左边那颗静音开关。
    /// </summary>
    /// <remarks>
    /// 走的是与播放条那颗、以及 <c>Ctrl+M</c> 同一个 <see cref="PlayerViewModel.ToggleMute"/>：
    /// 只压静音标记、音量字段保持原值，所以滑块停在原地，再点一下就恢复。
    /// 弹层不关 —— 用户要看着图标变。
    /// </remarks>
    private void OnVolumeMuteClick(object sender, RoutedEventArgs e) => Player.ToggleMute();

    /// <remarks>弹层期间窗口是钉住的，关掉之后按当前状态重新算一次，别把那笔账留着。</remarks>
    private void OnVolumeFlyoutClosed(object? sender, object e)
    {
        _flyoutOpen = false;
        _drawerOpen = _hovering && !_dragging && !_panelOpen;

        RefreshInfoSlot();
        ApplyLayout();
    }

    /// <summary>
    /// 信息区三种状态：悬浮显示传输按钮，播放中显示一行歌词，其余显示歌名与歌手。
    /// </summary>
    private void RefreshInfoSlot()
    {
        var transport = _hovering && !_dragging;
        var lyric = !transport && Player.IsPlaying && Lyrics.CurrentLineText.Length > 0;

        TransportPanel.Visibility = transport ? Visibility.Visible : Visibility.Collapsed;
        LyricViewport.Visibility = lyric ? Visibility.Visible : Visibility.Collapsed;
        InfoPanel.Visibility = transport || lyric ? Visibility.Collapsed : Visibility.Visible;
        UpdateMiniLyric();
    }

    private void OnLyricViewportSizeChanged(object sender, SizeChangedEventArgs args)
    {
        LyricViewportClip.Rect = new Rect(0, 0, args.NewSize.Width, args.NewSize.Height);
        UpdateMiniLyric();
    }

    private void OnLyricPositionChanged(object? sender, PlaybackPositionChangedEventArgs args)
    {
        _lyricClock.SetDuration(args.Duration);
        _lyricClock.Sync(args.Position);
        UpdateMiniLyric();
    }

    private void StopLyricTimer() => _lyricTimer?.Stop();

    private void ScheduleLyricUpdate(LyricDocument document, TimeSpan position, bool overflowing)
    {
        var delay = LyricRefreshSchedule.NextUpdateDelay(document, position, Player.IsPlaying,
            animateHighlight: false, overflowing);
        if (!delay.HasValue) { _lyricTimer?.Stop(); return; }
        if (_lyricTimer is null)
        {
            _lyricTimer = DispatcherQueue.CreateTimer();
            _lyricTimer.IsRepeating = false;
            _lyricTimer.Tick += (_, _) => UpdateMiniLyric();
        }
        _lyricTimer.Stop();
        _lyricTimer.Interval = delay.Value;
        _lyricTimer.Start();
    }

    private void ResumeVisualUpdates()
    {
        PlayingBars.SetWindowRendering(Root.XamlRoot, true);
        if (_bindingsSuspended) { Bindings.Update(); _bindingsSuspended = false; }
        if (!_subscriptionsActive)
        {
            _subscriptionsActive = true;
            Player.PropertyChanged += OnPlayerPropertyChanged;
            Lyrics.PropertyChanged += OnLyricsPropertyChanged;
            _engine.PositionChanged += OnLyricPositionChanged;
        }
        _lyricClock.SetDuration(_engine.Duration);
        _lyricClock.Sync(_engine.Position, force: true);
        _lyricClock.SetPlaying(_engine.State == PlaybackState.Playing);
        UpdateCoverImage();
        UpdateVolumeMuteState();
    }

    private void SuspendVisualUpdates()
    {
        PlayingBars.SetWindowRendering(Root.XamlRoot, false);
        StopLyricTimer();
        if (!_bindingsSuspended) { Bindings.StopTracking(); _bindingsSuspended = true; }
        if (_subscriptionsActive)
        {
            _subscriptionsActive = false;
            Player.PropertyChanged -= OnPlayerPropertyChanged;
            Lyrics.PropertyChanged -= OnLyricsPropertyChanged;
            _engine.PositionChanged -= OnLyricPositionChanged;
        }
        MiniCoverBrush.ImageSource = null;
        _miniLyricLine = null;
        LyricLineText.Text = string.Empty;
    }

    private void UpdateCoverImage()
        => MiniCoverBrush.ImageSource = CoverImageCache.Get(Player.CurrentCoverUri,
            Math.Clamp((int)Math.Ceiling(64 * _scale), 64, 256));

    private void UpdateMiniLyric()
    {
        if (!_windowVisible || _collapsed || LyricViewport.Visibility != Visibility.Visible)
        {
            StopLyricTimer();
            return;
        }
        var position = _lyricClock.Position;
        var document = Lyrics.Document;
        var index = document.IndexOfLineAt(position);
        var line = index >= 0 ? document.Lines[index] : null;
        if (!ReferenceEquals(_miniLyricLine, line))
        {
            _miniLyricLine = line;
            LyricLineText.Text = line?.Text ?? string.Empty;
            // Canvas 给文字完整的自然宽度，外层视口单独裁剪，不截断或缩小字形。
            LyricLineText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            _miniLyricWidth = LyricLineText.DesiredSize.Width;
            _miniLyricHeight = LyricLineText.DesiredSize.Height;
        }
        var width = LyricViewport.ActualWidth;
        var overflowing = width > 0 && _miniLyricWidth > width;
        var progress = line is null ? 0 : LyricHorizontalScroll.ProgressAt(line, document.Kind, position);
        var x = overflowing ? -LyricHorizontalScroll.Offset(_miniLyricWidth, width, progress)
            : (width - _miniLyricWidth) / 2;
        var scale = Root.XamlRoot?.RasterizationScale ?? _scale;
        var shiftX = Math.Round(x * scale) / scale;
        var shiftY = Math.Round((LyricViewport.ActualHeight - _miniLyricHeight) / 2 * scale) / scale;
        if (LyricShift.X != shiftX) LyricShift.X = shiftX;
        if (LyricShift.Y != shiftY) LyricShift.Y = shiftY;
        // 只在长句实际推进时最多 60 Hz；短句、音节间隙与句尾等到下一时间点。
        ScheduleLyricUpdate(document, position, overflowing);
    }

    private void OnPlayerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlayerViewModel.CurrentCoverUri)) UpdateCoverImage();
        if (e.PropertyName == nameof(PlayerViewModel.IsMuted)) UpdateVolumeMuteState();
        if (e.PropertyName == nameof(PlayerViewModel.IsPlaying))
        {
            _lyricClock.SetPlaying(Player.IsPlaying);
            RefreshInfoSlot();
        }
    }

    /// <summary>
    /// 静音时整颗音量按钮切到强调色。
    /// </summary>
    /// <remarks>
    /// 点击语义在主窗口与小窗之间不同（见 MiniPlayerWindow.xaml 里那段注释），但
    /// <b>静音是个状态、两处必须长得一样</b> —— 按 Ctrl+M 之后小窗上也得看得出来。
    /// </remarks>
    private void UpdateVolumeMuteState()
        => VisualStateManager.GoToState(
            VolumeButton,
            Player.IsMuted ? "Muted" : "Audible",
            useTransitions: false);

    private void OnLyricsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LyricsViewModel.CurrentLineText) or nameof(LyricsViewModel.Document))
        {
            RefreshInfoSlot();
        }
    }

    // ── 拖动 ────────────────────────────────────────────────────────────────

    /// <remarks>
    /// 位移一律用光标的屏幕坐标算，不用元素内坐标：窗口一边拖一边动，元素内坐标会跟着变，
    /// 累计两次就飘了。
    /// </remarks>
    private void OnRootPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(Root).Properties.IsLeftButtonPressed)
        {
            return;
        }

        // ★ 传输区那一块整块都不参与拖窗。**不能只靠 IsInteractiveSource** ——
        //   按钮被禁用后就不再可命中，点在它身上其实落在下面的容器上，
        //   走到 IsInteractiveSource 那里查不到 ButtonBase，那一下就会变成拖窗。
        if (_hovering && !_dragging && IsOverTransport(e.GetCurrentPoint(Root).Position))
        {
            e.Handled = true;
            return;
        }

        if (IsInteractiveSource(e.OriginalSource)
            || !NativeMethods.GetCursorPos(out _gestureCursorOrigin))
        {
            return;
        }

        // 队列面板开着时先关掉：面板的方向由条的位置决定，边拖边算会互相打架。
        if (_panelOpen)
        {
            _panelOpen = false;
            Queue.IsMiniPlayerOpen = false;
        }

        // 按下即解贴边，鼠标一移窗口就跟手走，不需要额外的位移阈值。
        _edge = MiniPlayerDockEdge.None;
        _collapsed = false;
        _drawerOpen = false;
        _gestureBarOrigin = _bar;

        _dragging = Root.CapturePointer(e.Pointer);
        e.Handled = _dragging;

        if (_dragging)
        {
            SetPassThrough(false);
            RefreshInfoSlot();
            ApplyLayout();
        }
    }

    /// <summary>点是不是落在悬浮时那块传输按钮上。</summary>
    private bool IsOverTransport(Point position)
    {
        if (TransportPanel.Visibility != Visibility.Visible
            || TransportPanel.ActualWidth <= 0
            || TransportPanel.ActualHeight <= 0)
        {
            return false;
        }

        var origin = TransportPanel.TransformToVisual(Root).TransformPoint(new Point());

        return position.X >= origin.X && position.X <= origin.X + TransportPanel.ActualWidth
            && position.Y >= origin.Y && position.Y <= origin.Y + TransportPanel.ActualHeight;
    }

    private static bool IsInteractiveSource(object source)
    {
        for (var element = source as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (element is ButtonBase or Slider or Thumb or TextBox or ToggleSwitch)
            {
                return true;
            }
        }

        return false;
    }

    private void OnRootPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging || !NativeMethods.GetCursorPos(out var cursor))
        {
            return;
        }

        var work = WorkAreaAt(cursor.X, cursor.Y);

        _bar = DesktopLyricsWindowGeometry.Constrain(
            new WindowPlacement(
                _gestureBarOrigin.X + (cursor.X - _gestureCursorOrigin.X),
                _gestureBarOrigin.Y + (cursor.Y - _gestureCursorOrigin.Y),
                _gestureBarOrigin.Width,
                _gestureBarOrigin.Height),
            work,
            Scaled(SnapDistanceDip));

        ApplyLayout();
    }

    private void OnRootPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging)
        {
            Root.ReleasePointerCapture(e.Pointer);
        }

        EndDrag();
    }

    private void EndDrag()
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;

        // 松手时外边缘与工作区左/右边重合就收起。Constrain 已经在拖动过程中把窗口吸到边缘，
        // 所以「重合」等价于「距边缘不超过吸附距离」—— 不必再立一个阈值。
        _edge = MiniPlayerGeometry.EdgeAt(_bar, WorkArea());

        if (_edge != MiniPlayerDockEdge.None)
        {
            _collapsed = true;

            // 光标此刻还在条上，不锁一下会立刻又被唤起，来回抖。
            _armed = false;
            _hovering = false;
            _drawerOpen = false;
            SetPassThrough(true);
        }
        else
        {
            // 拖动期间抽屉是关着的（边拖边改窗口尺寸会互相打架）；
            // 停在原地松手时鼠标还在条上，把悬停该有的样子补回来。
            _drawerOpen = _hovering && !_panelOpen;
        }

        RefreshInfoSlot();
        ApplyLayout();
        QueuePlacementSave();
    }

    // ── 队列面板与抽屉 ──────────────────────────────────────────────────────

    private void OnQueueClick(object sender, RoutedEventArgs e)
    {
        _panelOpen = !_panelOpen;
        Queue.IsMiniPlayerOpen = _panelOpen;

        if (_panelOpen)
        {
            _drawerOpen = false;
        }

        ApplyLayout();
    }

    private void OnCloseQueueRequested(object? sender, EventArgs args)
    {
        _panelOpen = false;
        Queue.IsMiniPlayerOpen = false;
        ApplyLayout();
    }

    /// <remarks>
    /// 确认框放在窗口而不是 ViewModel：那是一个界面决策（要不要弹、按钮怎么摆），
    /// 而 <c>XamlRoot</c> 也拿不到 ViewModel 里去。与主窗口的 <c>OnClearQueueRequested</c> 同一做法。
    /// </remarks>
    private async void OnClearQueueRequested(object? sender, EventArgs args)
    {
        var dialog = AppDialogs.Create("清空播放列表？", Root.XamlRoot, Root.ActualTheme);
        dialog.Content = "只会清掉这一份播放队列，正在播的这首会继续放完。";
        dialog.PrimaryButtonText = "清空";
        dialog.CloseButtonText = "取消";
        dialog.DefaultButton = ContentDialogButton.Close;

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        Queue.Clear();
    }

    // ── 传输区 / 进度 / 收尾 ────────────────────────────────────────────────

    private void OnPreviousClick(object sender, RoutedEventArgs e) => Player.PreviousCommand.Execute(null);

    private void OnNextClick(object sender, RoutedEventArgs e) => Player.NextCommand.Execute(null);

    private void OnPlayPauseClick(object sender, RoutedEventArgs e) => Player.TogglePlayPauseCommand.Execute(null);

    private void OnProgressPressed(object sender, PointerRoutedEventArgs e) => Player.IsSeeking = true;

    private void OnProgressReleased(object sender, PointerRoutedEventArgs e)
        => _ = Player.SeekToAsync(ProgressSlider.Value);

    private void OnProgressCaptureLost(object sender, PointerRoutedEventArgs e) => Player.IsSeeking = false;

    private void OnCloseClick(object sender, RoutedEventArgs e) => _settings.DismissCommand.Execute(null);

    /// <remarks>
    /// <b>先抢前台再藏自己。</b> <c>SetForegroundWindow</c> 要求调用进程是前台进程 ——
    /// 用户刚点了 ▢，前台就是本进程的小窗。反过来先隐藏，前台可能落到别的应用上，
    /// 调用被拒，症状是「点了 ▢ 主窗口没起来」。
    /// </remarks>
    private void OnReturnToMainClick(object sender, RoutedEventArgs e)
    {
        var main = _mainWindow.WindowHandle;

        if (NativeMethods.IsIconic(main))
        {
            NativeMethods.ShowWindow(main, NativeMethods.SwRestore);
        }

        NativeMethods.SetForegroundWindow(main);
        _settings.DismissCommand.Execute(null);
    }

    private void TearDown()
    {
        _windowVisible = false;
        StopTimers();
        SuspendVisualUpdates();
        ReleaseQueueView();
        SavePlacement();

        // ViewModel 比窗口活得久（它们常驻），不退订就是让窗口被这些事件引用着不放。
        Player.PropertyChanged -= OnPlayerPropertyChanged;
        Lyrics.PropertyChanged -= OnLyricsPropertyChanged;
        _engine.PositionChanged -= OnLyricPositionChanged;
    }

    // ── 坐标换算 ────────────────────────────────────────────────────────────

    private int Scaled(double dip) => (int)Math.Round(dip * _scale);

    /// <summary>窗口当前所在显示器的可用区域。</summary>
    private WindowPlacement WorkArea()
    {
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;

        return new WindowPlacement(area.X, area.Y, area.Width, area.Height);
    }

    /// <summary>某个屏幕坐标所在显示器的可用区域。</summary>
    private WindowPlacement WorkAreaAt(int x, int y)
    {
        var area = DisplayArea.GetFromRect(new RectInt32(x, y, 1, 1), DisplayAreaFallback.Nearest).WorkArea;

        return new WindowPlacement(area.X, area.Y, area.Width, area.Height);
    }

    /// <summary>有保存记录时按记录所在显示器算，没有就按主显示器。</summary>
    private WindowPlacement WorkAreaFor(WindowPlacement? saved)
    {
        if (saved is not { } placement)
        {
            return WorkArea();
        }

        var area = DisplayArea.GetFromRect(
            new RectInt32(placement.X, placement.Y, 1, 1), DisplayAreaFallback.Nearest).WorkArea;

        return new WindowPlacement(area.X, area.Y, area.Width, area.Height);
    }
}
