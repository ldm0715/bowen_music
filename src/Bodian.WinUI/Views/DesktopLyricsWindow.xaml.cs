using Bodian.Core.Models;
using Bodian.Core.Services.Abstractions;
using Bodian.WinUI.Controls;
using Bodian.WinUI.Playback;
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
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;

namespace Bodian.WinUI.Views;

/// <summary>透明置顶桌面歌词窗：悬停显示深色背景与图标操作，后台合成歌词。</summary>
/// <remarks>窗口透明、置顶与穿透的原生实现见 docs/desktop-lyrics.md。</remarks>
public sealed partial class DesktopLyricsWindow : Window
{
    /// <summary>默认宽度（DIP）。高度由字号与行数算，不用记。</summary>
    private const int DefaultWidthDip = 760;

    /// <summary>可拉伸的宽度范围（DIP）。</summary>
    private const int MinimumWidthDip = 420;
    private const int MaximumWidthDip = 1000;
    private const double ResizeBandDip = 16;
    private const double SnapDistanceDip = 16;

    /// <summary>设置排占的高度（DIP）。一直留着，免得工具栏浮现时歌词跳一下。</summary>
    private const double ToolbarBandDip = 84;

    /// <summary>歌词区之外的余量（DIP）。</summary>
    private const double VerticalPaddingDip = 20;

    /// <summary>鼠标刚离开窗口时不要立刻收，给一段宽限 —— 手抖一下工具栏就闪一次很难受。</summary>
    private const int ToolbarHideDelayMilliseconds = 180;

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(30);
    private static readonly TimeSpan TopmostInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan PlacementSaveDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 可选文字颜色。**只给基础色，不做取色板** —— 桌面歌词条上摆一个色轮太重了，
    /// 而这几个色在深浅壁纸上都能读。
    /// </summary>
    private static readonly (uint Color, string Name)[] PaletteColors =
    [
        (0xFF00E5BF, "青绿"), (0xFF000000, "黑色"), (0xFFFF4D4F, "红色"),
        (0xFFFF922B, "橙色"), (0xFFFADB14, "黄色"), (0xFF37D67A, "绿色"),
        (0xFF4D9CFF, "蓝色"), (0xFFFF6FB5, "粉色"),
    ];

    private static readonly Color ToolbarIdleColor = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
    private static readonly Color ToolbarActiveColor = Color.FromArgb(0xFF, 0x00, 0xE5, 0xBF);
    private static readonly Color ToolbarActiveBackground = Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF);

    private readonly DesktopLyricsViewModel _settings;
    private readonly DesktopLyricsCanvasView _canvas;
    private readonly DesktopLyricsResizeCursor _resizeCursor;
    private readonly IDesktopLyricsPlacementStore _placement;
    private readonly ILogger _logger;

    private readonly List<(Border Dot, uint Color)> _paletteDots = [];
    private readonly nint _handle;
    private double _scale;

    private DispatcherQueueTimer? _pollTimer;
    private DispatcherQueueTimer? _topmostTimer;
    private DispatcherQueueTimer? _placementTimer;
    private DispatcherQueueTimer? _toolbarHideTimer;

    private bool _initializing = true;
    private bool _applyingSettings;
    private bool _everShown;
    private bool _windowVisible;
    private bool _passThrough;
    private bool _toolbarVisible;
    private bool _settingsOpen;
    private bool _adjustingFontSize;
    private bool _dragging;
    private bool _resizing;
    private NativeMethods.NativePoint _gestureCursorOrigin;
    private PointInt32 _gestureWindowOrigin;
    private SizeInt32 _gestureWindowSize;

    public PlayerViewModel Player { get; }

    public DesktopLyricsWindow(
        DesktopLyricsViewModel settings,
        LyricsViewModel lyrics,
        IPlaybackService engine,
        IDesktopLyricsPlacementStore placement,
        PlayerViewModel player,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(lyrics);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(player);

        _settings = settings;
        Player = player;
        _placement = placement;
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<DesktopLyricsWindow>();

        InitializeComponent();
        _canvas = new DesktopLyricsCanvasView(settings, lyrics, engine, loggerFactory ?? NullLoggerFactory.Instance);
        LyricStack.Children.Add(_canvas);
        Player.PropertyChanged += OnPlayerPropertyChanged;
        Player.Statistics.PropertyChanged += OnStatisticsPropertyChanged;
        UpdatePlaybackAppearance();

        _handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _scale = NativeMethods.GetDpiForWindow(_handle) / 96.0;

        ConfigureWindowForm();
        _resizeCursor = new DesktopLyricsResizeCursor(_handle, BeginNativeResize, ContinueGesture, EndGesture);
        AppWindow.Changed += (_, _) => UpdateResizeEdge();
        ApplyPlacement();
        BuildColorPalette();
        FontSizeSlider.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnFontSizePointerPressed), true);
        FontSizeSlider.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnFontSizePointerReleased), true);
        FontSizeSlider.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(OnFontSizePointerReleased), true);
        FontSizePanel.PointerPressed += (_, e) => e.Handled = true;
        ColorPanel.PointerPressed += (_, e) => e.Handled = true;

        // ★ 拖动挂在整块根面板上，不是只挂歌词那一块：工具栏上的按钮与滑块自己会吃掉
        //   指针事件（标记为已处理，不会冒泡到这里），所以挂外层不会误触；
        //   而挂外层换来的是「关掉穿透后，随便按哪儿都能拖」。
        Root.PointerPressed += OnLyricPointerPressed;
        Root.PointerMoved += OnGesturePointerMoved;
        Root.PointerReleased += OnGesturePointerReleased;
        Root.PointerCaptureLost += (_, _) => EndGesture();
        _settings.PropertyChanged += OnSettingsPropertyChanged;
        Closed += (_, _) => TearDown();

        ApplySettings();
        _initializing = false;
        StartTimers();
        _canvas.IsPaused = false;
    }

    /// <summary>把窗口藏起来。宿主在关闭桌面歌词时调它。</summary>
    public void HideWindow()
    {
        _windowVisible = false;
        _resizeCursor.Update(default, false);
        _canvas.IsPaused = true;
        CloseSettings();
        SetToolbarVisible(false);
        SetPassThrough(true);
        StopTimers();
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
        UpdateResizeEdge();

        StartTimers();
        _canvas.IsPaused = false;
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

        var exStyle = (long)NativeMethods.GetWindowLongPtr(_handle, NativeMethods.GwlExStyle);
        NativeMethods.SetWindowLongPtr(
            _handle,
            NativeMethods.GwlExStyle,
            (nint)(exStyle | NativeMethods.WsExLayered | NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate));

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

        // Win11 去圆角（顺带去掉那圈细边框）。Win10 上返回 E_INVALIDARG 且什么也不做，
        // 白边靠上面清 WS_CAPTION | WS_THICKFRAME 解决 —— 失败是预期内的，不是异常。
        var corner = NativeMethods.DwmcpDoNotRound;
        _ = NativeMethods.DwmSetWindowAttribute(
            _handle,
            NativeMethods.DwmwaWindowCornerPreference,
            ref corner,
            sizeof(int));
    }

    /// <remarks>
    /// 高度不记盘：它由字号与行数算出来，记下来只会和内容打架。
    /// 位置与宽度则要记住，那是用户摆的。
    /// </remarks>
    private void ApplyPlacement()
    {
        var saved = _placement.Load();
        var work = saved is not null
            ? DisplayArea.GetFromRect(new RectInt32(saved.X, saved.Y, 1, 1), DisplayAreaFallback.Nearest).WorkArea
            : DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;

        var widthDip = ClampWidthDip(saved is { Width: > 0 } ? saved.Width : DefaultWidthDip, work);
        var width = (int)(widthDip * _scale);
        var height = (int)(ContentHeightDip() * _scale);

        // 没记录就落在主屏底部居中、离底边约 120 DIP —— 那是最不挡事的位置。
        var x = saved?.X ?? work.X + ((work.Width - width) / 2);
        var y = saved?.Y ?? work.Y + work.Height - height - (int)(120 * _scale);

        ApplyBounds(new RectInt32(x, y, width, height), work);
    }

    private int ClampWidthDip(int width, RectInt32? workArea = null)
    {
        var work = workArea ?? DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var maximum = Math.Max(1, Math.Min(MaximumWidthDip, (int)Math.Floor(work.Width / _scale)));
        return Math.Clamp(width, Math.Min(MinimumWidthDip, maximum), maximum);
    }

    private double ContentHeightDip()
    {
        var line = _settings.FontSize * 1.4;
        var lines = _settings.DualLine ? (line * 2) + 4 : line;

        return Math.Ceiling(ToolbarBandDip + lines + VerticalPaddingDip);
    }

    /// <summary>字号或行数变了要重算高度，否则字会被裁掉或多出一截空白。</summary>
    private void ApplyContentHeight()
    {
        var height = (int)(ContentHeightDip() * _scale);

        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        ApplyBounds(new RectInt32(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, height), work);
    }

    private void ApplyBounds(RectInt32 desired, RectInt32 work, int snapDistance = 0)
    {
        var bounded = DesktopLyricsWindowGeometry.Constrain(
            new WindowPlacement(desired.X, desired.Y, desired.Width, desired.Height),
            new WindowPlacement(work.X, work.Y, work.Width, work.Height), snapDistance);
        var position = AppWindow.Position;
        var size = AppWindow.Size;
        if (position.X == bounded.X && position.Y == bounded.Y && size.Width == bounded.Width && size.Height == bounded.Height) return;
        AppWindow.MoveAndResize(new RectInt32(bounded.X, bounded.Y, bounded.Width, bounded.Height));
    }

    // ── 设置排 ──────────────────────────────────────────────────────────────

    /// <remarks>
    /// 圆点在代码里生成，不写进 XAML：调色板只有一处定义，加一个颜色不用同时改两个地方。
    /// </remarks>
    private void BuildColorPalette()
    {
        foreach (var (argb, name) in PaletteColors)
        {
            var color = ToColor(argb);
            var dot = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(color),
                VerticalAlignment = VerticalAlignment.Center,

                // 白色的点画在浅色壁纸上会看不见，统一加一圈淡描边。
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
            };

            var button = new Button
            {
                Content = dot, Width = 28, Height = 28, MinWidth = 0,
                Padding = new Thickness(4), BorderThickness = new Thickness(0),
                Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
            };
            button.Click += (_, _) => _settings.TextColor = color;
            ToolTipService.SetToolTip(button, name);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"高亮颜色：{name}");
            _paletteDots.Add((dot, argb));
            ColorPalette.Children.Add(button);
        }
    }

    private void OnFontSizeChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_initializing || _applyingSettings) return;
        _settings.FontSize = e.NewValue;
    }

    private void OnDualLineClick(object sender, RoutedEventArgs e)
        => _settings.DualLine = !_settings.DualLine;

    private void OnAlignmentClick(object sender, RoutedEventArgs e)
        => _settings.Alignment = _settings.Alignment == DesktopLyricsAlignment.Staggered
            ? DesktopLyricsAlignment.Center
            : DesktopLyricsAlignment.Staggered;

    private void OnPassThroughClick(object sender, RoutedEventArgs e)
        => _settings.PassThrough = !_settings.PassThrough;

    private void OnLockClick(object sender, RoutedEventArgs e)
        => _settings.Locked = !_settings.Locked;

    private void OnCloseClick(object sender, RoutedEventArgs e) => _settings.DismissCommand.Execute(null);

    // ── 设置 → 表现 ─────────────────────────────────────────────────────────

    private void OnFontSizeClick(object sender, RoutedEventArgs e)
        => ShowSettings(FontSizePanel);

    private void OnColorClick(object sender, RoutedEventArgs e)
        => ShowSettings(ColorPanel);

    private void ShowSettings(FrameworkElement panel)
    {
        var opening = panel.Visibility != Visibility.Visible;
        CloseSettings();
        if (!opening) return;
        panel.Visibility = Visibility.Visible;
        _settingsOpen = true;
        UpdateResizeEdge();
        _toolbarHideTimer?.Stop();
        SetToolbarVisible(true);
        SetPassThrough(false);
    }

    private void CloseSettings()
    {
        FontSizePanel.Visibility = Visibility.Collapsed;
        ColorPanel.Visibility = Visibility.Collapsed;
        _settingsOpen = false;
        UpdateResizeEdge();
    }

    private void OnFontSizePointerPressed(object sender, PointerRoutedEventArgs e)
        => _adjustingFontSize = true;

    private void OnFontSizePointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_adjustingFontSize) return;
        _adjustingFontSize = false;
        ApplyContentHeight();
        QueuePlacementSave();
    }

    private void ApplySettings()
    {
        _applyingSettings = true;
        try
        {
            ColorIcon.Foreground = new SolidColorBrush(_settings.TextColor);
            ApplyControlStates();
            if (!_adjustingFontSize) ApplyContentHeight();
        }
        finally { _applyingSettings = false; }
    }

    /// <remarks>
    /// 这几颗用的是普通 <c>Button</c> 而不是 <c>ToggleButton</c>：<c>ToggleButton</c> 的选中态
    /// 走系统主题画刷，在这块浮在桌面上的透明区域里观感不对（而且这里本来就不跟随应用主题）。
    /// 两态各自什么颜色由这里说了算。
    /// </remarks>
    private void ApplyControlStates()
    {
        SetToggleAppearance(DualLineButton, _settings.DualLine);
        SetToggleAppearance(AlignmentButton, _settings.Alignment == DesktopLyricsAlignment.Staggered);
        PassThroughButton.Foreground = new SolidColorBrush(_settings.PassThrough ? ToolbarActiveColor : ToolbarIdleColor);
        PassThroughButton.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        PassThroughIcon.Data = IconGeometry.Paths(_settings.PassThrough ? "IconCursorFilled" : "IconCursor");
        var passThroughLabel = _settings.PassThrough
            ? "鼠标穿透已开启：歌词区域可点击下方窗口"
            : "鼠标穿透已关闭：可拖动歌词";
        ToolTipService.SetToolTip(PassThroughButton, passThroughLabel);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(PassThroughButton, passThroughLabel);
        SetToggleAppearance(LockButton, _settings.Locked);

        // 锁定后只保留解锁入口，所有其他按钮和拉伸边缘都隐藏。
        if (_settings.Locked) CloseSettings();
        DualLineButton.Visibility = _settings.Locked ? Visibility.Collapsed : Visibility.Visible;
        AlignmentButton.Visibility = _settings.Locked ? Visibility.Collapsed : Visibility.Visible;
        PassThroughButton.Visibility = _settings.Locked ? Visibility.Collapsed : Visibility.Visible;
        FontSizeButton.Visibility = _settings.Locked ? Visibility.Collapsed : Visibility.Visible;
        ColorButton.Visibility = _settings.Locked ? Visibility.Collapsed : Visibility.Visible;
        CloseButton.Visibility = _settings.Locked ? Visibility.Collapsed : Visibility.Visible;
        PreviousButton.Visibility = _settings.Locked ? Visibility.Collapsed : Visibility.Visible;
        PlayPauseButton.Visibility = _settings.Locked ? Visibility.Collapsed : Visibility.Visible;
        NextButton.Visibility = _settings.Locked ? Visibility.Collapsed : Visibility.Visible;
        FavoriteButton.Visibility = _settings.Locked ? Visibility.Collapsed : Visibility.Visible;

        // 字号滑块在弹层里，不常显 —— 但值要跟上，展开时不能停在上次的位置。
        FontSizeSlider.Value = _settings.FontSize;
        FontSizeValue.Text = ((int)_settings.FontSize).ToString();

        UpdateResizeEdge();
        AlignmentButton.IsEnabled = _settings.DualLine;
        LockIcon.Data = IconGeometry.Paths(_settings.Locked ? "IconLock" : "IconLockOpen");

        foreach (var (dot, argb) in _paletteDots)
        {
            var current = ToArgb(_settings.TextColor) == argb;

            // 选中的那个加粗描边 —— 桌面歌词条上只有这一种「当前色」提示。
            dot.BorderBrush = new SolidColorBrush(current
                ? Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)
                : Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF));
            dot.BorderThickness = new Thickness(current ? 2 : 1);
        }

        ToolTipService.SetToolTip(LockButton, _settings.Locked ? "解锁位置" : "锁定位置");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(LockButton, _settings.Locked ? "解锁位置" : "锁定位置");
    }

    private static void SetToggleAppearance(Button button, bool on)
    {
        button.Foreground = new SolidColorBrush(on ? ToolbarActiveColor : ToolbarIdleColor);
        button.Background = new SolidColorBrush(on ? ToolbarActiveBackground : Color.FromArgb(0, 0, 0, 0));
    }

    // ── 定时器 ──────────────────────────────────────────────────────────────

    /// <remarks>
    /// 定时器只创建一次，窗口藏起来时全部停掉。
    /// </remarks>
    private void StartTimers()
    {
        if (_pollTimer is null)
        {
            _pollTimer = DispatcherQueue.CreateTimer();
            _pollTimer.Interval = PollInterval;
            _pollTimer.IsRepeating = true;
            _pollTimer.Tick += (_, _) => UpdatePointerState();
        }

        if (_topmostTimer is null)
        {
            _topmostTimer = DispatcherQueue.CreateTimer();
            _topmostTimer.Interval = TopmostInterval;
            _topmostTimer.IsRepeating = true;
            _topmostTimer.Tick += (_, _) =>
            {
                if (!_settingsOpen && !_dragging && !_resizing)
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

        if (_toolbarHideTimer is null)
        {
            _toolbarHideTimer = DispatcherQueue.CreateTimer();
            _toolbarHideTimer.Interval = TimeSpan.FromMilliseconds(ToolbarHideDelayMilliseconds);
            _toolbarHideTimer.IsRepeating = false;
            _toolbarHideTimer.Tick += (_, _) => SetToolbarVisible(false);
        }

        _pollTimer.Start();
        _topmostTimer.Start();
    }

    private void StopTimers()
    {
        _pollTimer?.Stop();
        _topmostTimer?.Stop();
        _placementTimer?.Stop();
        _toolbarHideTimer?.Stop();
    }

    private void QueuePlacementSave()
    {
        _placementTimer?.Stop();
        _placementTimer?.Start();
    }

    private void SavePlacement()
    {
        var position = AppWindow.Position;

        _placement.Save(new WindowPlacement(
            position.X,
            position.Y,
            (int)Math.Round(AppWindow.Size.Width / _scale),
            (int)Math.Round(AppWindow.Size.Height / _scale)));
    }

    private void KeepOnTop()
    {
        // 已经置顶时不反复重排 HWND，避免干扰透明表面、光标和弹层。
        var style = (long)NativeMethods.GetWindowLongPtr(_handle, NativeMethods.GwlExStyle);
        if ((style & NativeMethods.WsExTopmost) != 0) return;
        NativeMethods.SetWindowPos(_handle, NativeMethods.HwndTopmost, 0, 0, 0, 0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
    }

    // ── 穿透与设置排的显隐 ──────────────────────────────────────────────────

    /// <remarks>
    /// 一个定时器同时管两件事：<b>设置排要不要显示</b>，以及<b>这一下点击要不要穿透</b>。
    /// <c>WS_EX_TRANSPARENT</c> 一开窗口就收不到鼠标消息，所以显隐不能靠 <c>PointerEntered</c> ——
    /// 只能这么轮询。
    /// </remarks>
    private void UpdatePointerState()
    {
        _resizeCursor.PollResize();
        if (_adjustingFontSize || _dragging || _resizing)
        {
            _toolbarHideTimer?.Stop();
            SetToolbarVisible(true);
            SetPassThrough(false);
            return;
        }

        if (!NativeMethods.GetCursorPos(out var cursor))
        {
            return;
        }
        _scale = NativeMethods.GetDpiForWindow(_handle) / 96.0;
        UpdateResizeEdge();

        var origin = AppWindow.Position;
        var size = AppWindow.Size;
        var inWindow = cursor.X >= origin.X && cursor.X < origin.X + size.Width
            && cursor.Y >= origin.Y && cursor.Y < origin.Y + size.Height;

        if (inWindow)
        {
            _toolbarHideTimer?.Stop();
            SetToolbarVisible(true);
        }
        else if (_toolbarVisible && _toolbarHideTimer is { IsRunning: false })
        {
            // 只启动一次。每次轮询都 Start 会重置倒计时，使背景永远无法隐藏。
            _toolbarHideTimer.Start();
        }

        // 悬停背景只负责显示边界。开启穿透后，只有实际按钮和拉伸把手保留交互。
        var overInteractive = !_settings.PassThrough
            || (_settingsOpen && (IsOver(FontSizePanel, cursor) || IsOver(ColorPanel, cursor)))
            || (_toolbarVisible && IsOverToolbarButton(cursor))
            || IsInResizeBand(cursor);
        SetPassThrough(!overInteractive);
    }

    private void UpdateResizeEdge()
    {
        var width = (int)Math.Ceiling(ResizeBandDip * _scale);
        var position = AppWindow.Position;
        var size = AppWindow.Size;
        _resizeCursor?.Update(new RectInt32(position.X + size.Width - width, position.Y, width, size.Height),
            _windowVisible && _settings.IsEnabled && !_settings.Locked && !_settingsOpen);
    }

    private bool IsInResizeBand(NativeMethods.NativePoint cursor)
    {
        if (_settings.Locked || _settingsOpen) return false;
        var position = AppWindow.Position;
        var size = AppWindow.Size;
        var right = position.X + size.Width;
        return cursor.X >= right - (int)Math.Ceiling(ResizeBandDip * _scale) && cursor.X < right
            && cursor.Y >= position.Y && cursor.Y < position.Y + size.Height;
    }

    private bool IsOverToolbarButton(NativeMethods.NativePoint cursor)
        => IsOver(FontSizeButton, cursor) || IsOver(ColorButton, cursor)
            || IsOver(DualLineButton, cursor) || IsOver(AlignmentButton, cursor)
            || IsOver(PassThroughButton, cursor) || IsOver(LockButton, cursor)
            || IsOver(CloseButton, cursor) || IsOver(PreviousButton, cursor)
            || IsOver(PlayPauseButton, cursor) || IsOver(NextButton, cursor)
            || IsOver(FavoriteButton, cursor);

    private void SetToolbarVisible(bool visible)
    {
        if (_toolbarVisible == visible)
        {
            return;
        }

        if (!visible) CloseSettings();
        _toolbarVisible = visible;
        Toolbar.Opacity = visible ? 1 : 0;
        Toolbar.IsHitTestVisible = visible;
        HoverBackground.Opacity = visible ? 1 : 0;
    }

    private void SetPassThrough(bool passThrough)
    {
        if (_passThrough == passThrough)
        {
            return;
        }

        _passThrough = passThrough;

        var exStyle = (long)NativeMethods.GetWindowLongPtr(_handle, NativeMethods.GwlExStyle);
        var updated = passThrough
            ? exStyle | NativeMethods.WsExTransparent
            : exStyle & ~NativeMethods.WsExTransparent;

        NativeMethods.SetWindowLongPtr(_handle, NativeMethods.GwlExStyle, (nint)updated);
    }

    private bool IsOver(FrameworkElement element, NativeMethods.NativePoint cursor)
    {
        if (element.Visibility != Visibility.Visible || element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return false;
        }

        var origin = AppWindow.Position;
        var topLeft = element.TransformToVisual(null).TransformPoint(new Point(0, 0));

        var left = origin.X + (int)Math.Round(topLeft.X * _scale);
        var top = origin.Y + (int)Math.Round(topLeft.Y * _scale);
        var right = left + (int)Math.Round(element.ActualWidth * _scale);
        var bottom = top + (int)Math.Round(element.ActualHeight * _scale);

        return cursor.X >= left && cursor.X < right && cursor.Y >= top && cursor.Y < bottom;
    }

    // ── 拖动与拉伸 ──────────────────────────────────────────────────────────

    /// <remarks>
    /// 位移一律用光标的屏幕坐标算，不用元素内坐标：窗口一边拖一边动，元素内坐标会跟着变，
    /// 累计两次就飘了。
    /// </remarks>
    private void OnLyricPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_settings.Locked || IsInteractiveSource(e.OriginalSource)
            || !e.GetCurrentPoint(Root).Properties.IsLeftButtonPressed
            || !NativeMethods.GetCursorPos(out _gestureCursorOrigin)) return;
        if (_settingsOpen)
        {
            CloseSettings();
            e.Handled = true;
            return;
        }
        if (_settings.PassThrough) return;

        _gestureWindowOrigin = AppWindow.Position;
        _gestureWindowSize = AppWindow.Size;
        _dragging = Root.CapturePointer(e.Pointer);
        e.Handled = _dragging;
    }

    private static bool IsInteractiveSource(object source)
    {
        for (var element = source as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (element is ButtonBase or Slider or Thumb or TextBox or ToggleSwitch) return true;
        }
        return false;
    }

    private void OnGesturePointerMoved(object sender, PointerRoutedEventArgs e) => ContinueGesture();

    private void ContinueGesture()
    {
        if (!(_dragging || _resizing) || !NativeMethods.GetCursorPos(out var cursor))
        {
            return;
        }

        if (_dragging)
        {
            var work = DisplayArea.GetFromRect(new RectInt32(cursor.X, cursor.Y, 1, 1), DisplayAreaFallback.Nearest).WorkArea;
            ApplyBounds(new RectInt32(
                _gestureWindowOrigin.X + (cursor.X - _gestureCursorOrigin.X),
                _gestureWindowOrigin.Y + (cursor.Y - _gestureCursorOrigin.Y),
                AppWindow.Size.Width, AppWindow.Size.Height), work, (int)Math.Round(SnapDistanceDip * _scale));
        }
        else
        {
            var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            var resized = DesktopLyricsWindowGeometry.ResizeFromRight(
                new WindowPlacement(_gestureWindowOrigin.X, _gestureWindowOrigin.Y, _gestureWindowSize.Width, _gestureWindowSize.Height),
                cursor.X - _gestureCursorOrigin.X,
                (int)Math.Ceiling(MinimumWidthDip * _scale), (int)Math.Floor(MaximumWidthDip * _scale),
                new WindowPlacement(work.X, work.Y, work.Width, work.Height), (int)Math.Round(SnapDistanceDip * _scale));
            ApplyBounds(new RectInt32(resized.X, resized.Y, resized.Width, resized.Height), work);
        }
    }

    private void OnGesturePointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging)
        {
            Root.ReleasePointerCapture(e.Pointer);
        }

        EndGesture();
    }

    private bool BeginNativeResize()
    {
        if (!_windowVisible || _settings.Locked || _settingsOpen
            || !NativeMethods.GetCursorPos(out _gestureCursorOrigin)) return false;
        _scale = NativeMethods.GetDpiForWindow(_handle) / 96.0;
        _gestureWindowOrigin = AppWindow.Position;
        _gestureWindowSize = AppWindow.Size;
        _resizing = true;
        _toolbarHideTimer?.Stop();
        SetToolbarVisible(true);
        SetPassThrough(false);
        return true;
    }

    private void EndGesture()
    {
        if (!_dragging && !_resizing)
        {
            return;
        }

        _dragging = false;
        _resizing = false;
        UpdatePointerState();
        QueuePlacementSave();
    }

    private void OnSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        => ApplySettings();

    private void OnPlayerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlayerViewModel.IsPlaying) or nameof(PlayerViewModel.CurrentTrackId))
            UpdatePlaybackAppearance();
    }

    private void OnStatisticsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TrackStatisticsViewModel.IsFavorite)) UpdatePlaybackAppearance();
    }

    private void UpdatePlaybackAppearance()
    {
        PlayPauseButton.Foreground = new SolidColorBrush(Player.IsPlaying ? ToolbarActiveColor : ToolbarIdleColor);
        FavoriteButton.Foreground = new SolidColorBrush(Player.Statistics.IsFavorite ? ToolbarActiveColor : ToolbarIdleColor);
        var hasTrack = Player.CurrentTrackId.HasValue;
        PlayPauseButton.IsEnabled = hasTrack;
        ToolTipService.SetToolTip(PlayPauseButton, Player.IsPlaying ? "暂停" : "播放");
        ToolTipService.SetToolTip(FavoriteButton, Player.Statistics.IsFavorite ? "取消收藏" : "收藏");
    }

    // ── 收尾 ────────────────────────────────────────────────────────────────

    private void TearDown()
    {
        _windowVisible = false;
        _resizeCursor.Dispose();
        SavePlacement();
        _canvas.IsPaused = true;
        StopTimers();

        // ViewModel 比窗口活得久（它们常驻），不退订就是让窗口被这些事件引用着不放。
        _settings.PropertyChanged -= OnSettingsPropertyChanged;
        Player.PropertyChanged -= OnPlayerPropertyChanged;
        Player.Statistics.PropertyChanged -= OnStatisticsPropertyChanged;
        _canvas.Dispose();
    }

    private static Color ToColor(uint argb) => Color.FromArgb(
        (byte)(argb >> 24),
        (byte)(argb >> 16),
        (byte)(argb >> 8),
        (byte)argb);

    private static uint ToArgb(Color color)
        => ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
}
