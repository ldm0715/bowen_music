using System.ComponentModel;
using System.Diagnostics;
using System.Numerics;
using Bowen.Core.Models;
using Bowen.WinUI.Controls;
using Bowen.WinUI.Services;
using Bowen.WinUI.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;
using VirtualKey = Windows.System.VirtualKey;

namespace Bowen.WinUI.Views;

public sealed partial class LyricsPage : Page, INavigationAware
{
    private void OnQualitySelected(object? sender, EventArgs e) => QualityFlyout.Hide();
    private readonly MainWindow _window;
    private readonly INavigationService _navigation;
    private readonly Func<Track, MvPage> _mvFactory;

    /// <summary>底部「更多」菜单要用它现造菜单状态（与曲目行那颗同一个装配点）。</summary>
    private readonly TrackActionsService _trackActions;
    private readonly IReadOnlyList<FontOption> _fontOptions;
    private readonly List<(Border Dot, uint Color)> _colorDots = [];
    private bool _syncingAppearance;
    private bool _appearanceOpen;
    private FrameworkElement? _themeRoot;
    private readonly LyricsCanvasView _canvas;
    private readonly AudioSpectrumView _spectrum;
    private readonly SongCommentsPanel _commentsPanel;
    private readonly DispatcherQueueTimer _chromeTimer;
    private readonly DispatcherQueueTimer _pointerTimer;
    private readonly DispatcherQueueTimer _layoutTimer;
    private bool _layoutPending;
    private bool? _coverPlaying;
    private double _coverSize;
    private bool? _fullscreen;
    private readonly ILogger<LyricsPage> _logger;
    private readonly nint _windowHandle;
    private NativeMethods.NativePoint? _lastCursorPoint;
    private bool _keyboardInteractionActive;
    private bool _windowVisible = true;
    private bool _progressSeeking;
    private bool _keyboardSeeking;
    private bool _chromeVisible = true;
    private readonly List<DispatcherQueueTimer> _entranceTimers = [];
    private int _entranceVersion;
    private bool _entranceActive;
    /// <summary>沉浸入场动画结束前先保持封面模糊，避免透出外壳页面。</summary>
    private bool _immersiveEntranceCompleted;

    /// <summary>
    /// 收起时长与内容层的上移距离。**宁少勿多**：参考实现整块滑走一屏，实测用力过猛。
    /// </summary>
    /// <remarks>
    /// 收起不是「页面退场」，而是让回播放条这件事成为画面的主角：
    /// 内容 400 ms 淡出并轻微上移，背景只淡出、不位移（不透明层一挪就会在顶部露出硬边），
    /// 封面照常回位。
    /// </remarks>
    private static readonly TimeSpan ExitBackdropDuration = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan ExitContentDuration = TimeSpan.FromMilliseconds(400);
    private const float ExitContentRiseDip = 8;
    private static readonly SolidColorBrush CoverBackdropBrush = new(Color.FromArgb(0xFF, 0x0B, 0x12, 0x22));
    private static readonly SolidColorBrush TransparentBackdropBrush = new(Color.FromArgb(0, 0, 0, 0));
    private Brush? _coverFrameBackground;
    private float _reflectionOpacity;
    private bool _pointerOverChrome;
    private bool _volumePopupOpen;
    private bool _moreFlyoutOpen;
    private FrameworkElement? _progressThumb;
    private Rectangle? _progressTrack;
    private Rectangle? _progressFill;
    private bool _progressPointerOver;
    private double _progressPointerX;

    public LyricsPage(MainWindow window, LyricsCanvasView canvas, AudioSpectrumView spectrum, PlayerViewModel player,
        LyricsViewModel lyrics, SongCommentsViewModel comments,
        INavigationService navigation, Func<Track, MvPage> mvFactory, TrackActionsService trackActions,
        FontSettingsViewModel font)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(spectrum);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(lyrics);
        ArgumentNullException.ThrowIfNull(comments);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(mvFactory);
        ArgumentNullException.ThrowIfNull(trackActions);
        ArgumentNullException.ThrowIfNull(font);
        _window = window;
        _navigation = navigation;
        _mvFactory = mvFactory;
        _trackActions = trackActions;
        _logger = (Application.Current.Resources["BowenLoggerFactory"] as ILoggerFactory
            ?? NullLoggerFactory.Instance).CreateLogger<LyricsPage>();
        _canvas = canvas;
        _spectrum = spectrum;
        Player = player;
        Lyrics = lyrics;
        Comments = comments;
        InitializeComponent();
        _fontOptions = BuildFontOptions(font);
        LyricsFontPicker.ItemsSource = _fontOptions;
        LyricsFontPicker.StretchToAvailableWidth();
        LyricsFontPicker.RegisterPropertyChangedCallback(
            FontPicker.SelectedIndexProperty, OnLyricsFontPickerSelectionChanged);
        BuildColorPalette();
        _commentsPanel = new SongCommentsPanel(comments);
        _commentsPanel.CloseRequested += (_, _) => CloseComments();
        CommentsHost.Content = _commentsPanel;
        ElementCompositionPreview.SetIsTranslationEnabled(CommentsPane, true);
        ElementCompositionPreview.SetIsTranslationEnabled(AppearancePane, true);
        CanvasHost.Content = canvas;
        SpectrumHost.Content = spectrum;
        canvas.BrowsingChanged += (_, _) => FollowButton.Visibility = canvas.IsBrowsing ? Visibility.Visible : Visibility.Collapsed;
        _chromeTimer = DispatcherQueue.CreateTimer();
        _chromeTimer.Interval = TimeSpan.FromSeconds(3);
        _chromeTimer.IsRepeating = false;
        _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        _pointerTimer = DispatcherQueue.CreateTimer();
        _pointerTimer.Interval = TimeSpan.FromMilliseconds(200);
        _layoutTimer = DispatcherQueue.CreateTimer();
        _layoutTimer.Interval = TimeSpan.FromMilliseconds(120);
        _layoutTimer.IsRepeating = false;
        AddHandler(KeyDownEvent, new KeyEventHandler(OnChromeKeyDown), true);
        AddHandler(PointerEnteredEvent, new PointerEventHandler(OnPointerActivity), true);
        AddHandler(PointerMovedEvent, new PointerEventHandler(OnPointerActivity), true);
        AddHandler(PointerPressedEvent, new PointerEventHandler(OnPointerActivity), true);
        AddHandler(PointerExitedEvent, new PointerEventHandler(OnPointerLeftPage), true);
        GotFocus += OnChromeFocusChanged;
        LostFocus += OnChromeFocusChanged;
        PositionSlider.AddHandler(PointerPressedEvent, new PointerEventHandler(OnProgressPressed), true);
        PositionSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnProgressReleased), true);
        PositionSlider.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnProgressReleased), true);
        PositionSlider.AddHandler(PointerCanceledEvent, new PointerEventHandler(OnProgressCanceled), true);
        PositionSlider.AddHandler(KeyDownEvent, new KeyEventHandler(OnProgressKeyDown), true);
        PositionSlider.AddHandler(KeyUpEvent, new KeyEventHandler(OnProgressKeyUp), true);
        PositionSlider.AddHandler(PointerEnteredEvent, new PointerEventHandler(OnProgressPointerMoved), true);
        PositionSlider.AddHandler(PointerMovedEvent, new PointerEventHandler(OnProgressPointerMoved), true);
        PositionSlider.AddHandler(PointerExitedEvent, new PointerEventHandler(OnProgressPointerExited), true);
        PositionSlider.Loaded += OnProgressLoaded;
        PositionSlider.GotFocus += (_, _) => UpdateProgressAppearance();
        PositionSlider.LostFocus += (_, _) => UpdateProgressAppearance();
        // 音量滑条在弹层里，由控件自己管；这里只要跟着开合把控制台钉住。
        VolumeControl.PopupStateChanged += OnVolumePopupChanged;
        Loaded += (_, _) => UpdateLayoutSizing();
    }

    public PlayerViewModel Player { get; }
    public LyricsViewModel Lyrics { get; }
    public SongCommentsViewModel Comments { get; }
    public string SongHeading(string title, string artist)
        => string.IsNullOrWhiteSpace(artist) ? title : $"{title} - {artist}";

    /// <summary>
    /// 从应用资源里取一条图标**路径文本**（<c>Themes/Icons.xaml</c>）。
    /// 只有需要由代码切图标的地方（全屏两态）才用它 ——
    /// 其余都是 XAML 里 <c>Data="{StaticResource IconX}"</c>，不需要经手代码。
    /// </summary>
    /// <remarks>
    /// 返回的是字符串而不是 <c>Geometry</c>：资源字典里的 Geometry 赋不进 Geometry 属性，
    /// 见 <see cref="Controls.IconGeometry"/>。路径与主题无关、颜色一律走 Foreground 继承，
    /// 所以这里查资源不触犯 ui-refresh.md §7（那条禁的是**跟随主题的颜色**）。
    /// </remarks>
    private static string ResourceIcon(string key)
        => Application.Current is { } app && app.Resources.TryGetValue(key, out var value)
            ? value as string ?? ""
            : "";

    public void OnNavigatedTo()
    {
        // 上一次收起 / 被中途取消的入场会留下位移与透明度，先复位再入场。
        _immersiveEntranceCompleted = false;
        AppMotion.Reset(BackdropLayer);
        AppMotion.Reset(ContentLayer);
        AppMotion.Reset(CommentsOverlay);
        foreach (var element in new FrameworkElement[]
        {
            LyricsTitleBar, HeadingHost, ControlDeck, PositionSlider, SpectrumHost, CanvasHost, CoverDeck,
        })
        {
            AppMotion.Reset(element);
        }
        _chromeTimer.Tick += OnChromeTimer;
        _pointerTimer.Tick += OnPointerTimer;
        _layoutTimer.Tick += OnLayoutTimer;
        // 封面槽最大 580 DIP；按当前 DPI 取够用的解码尺寸，别一律按 1024 解。
        // 125% 下 725 px，取 768；150% 以上才需要接近 1024。
        var coverScale = XamlRoot?.RasterizationScale ?? 1;
        Player.SetCoverDecodePixels(Math.Min(1024, (int)Math.Ceiling(580 * coverScale / 64) * 64));
        _windowVisible = _window.AppWindow.IsVisible;
        _themeRoot = _window.ThemeRoot;
        _themeRoot.ActualThemeChanged += OnAppThemeChanged;
        SyncPanelThemes();
        if (Player.CurrentTrackId is > 0) _ = Comments.LoadBadgeAsync(Player.CurrentTrackId.Value);
        _window.VisibilityChanged += OnWindowVisibilityChanged;
        _window.RenderingStateChanged += OnWindowRenderingStateChanged;
        _window.AppWindow.Changed += OnAppWindowChanged;
        Player.PropertyChanged += OnPlayerChanged;
        Lyrics.PropertyChanged += OnLyricsChanged;
        RebuildTrackMenu();
        _pointerOverChrome = false;
        _keyboardInteractionActive = false;
        _lastCursorPoint = null;
        _window.EnterImmersive(LyricsTitleBar);
        Lyrics.IsOpen = true;
        ApplyBackgroundMode();
        UpdatePause();
        SyncFullscreen();
        SyncTranslation();
        _pointerTimer.Start();
        UpdatePointerLocation();
        BeginCoverEntrance();
    }

    /// <summary>
    /// 从播放条进入时按封面飞行的节奏分层展开：封面先走，顶栏、控件、歌词依次淡入。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 参考 LyciaMusic（AMLL 详情页）：页面内容不能整块先出现，否则看起来是「页面展开后封面才飞进来」。
    /// 三个时间点取封面飞行 520 ms 的 45% / 60% / 80%，每层再各用 400 ms 淡入并轻微上移。
    /// </para>
    /// <para>
    /// 封面落位前清掉空槽底色并藏起倒影，画面里只留正在放大的封面。
    /// 普通进入（没有共享封面）不执行这套，整页按原有过渡淡入。
    /// </para>
    /// </remarks>
    private void BeginCoverEntrance()
    {
        if (!AppMotion.IsEnabled || CoverTransitionAnimator.Current?.HasLyricsEntrance(this) != true) return;

        var version = ++_entranceVersion;
        _entranceActive = true;
        foreach (var element in new FrameworkElement[]
        {
            LyricsTitleBar, HeadingHost, ControlDeck, PositionSlider, SpectrumHost, CanvasHost,
        })
        {
            AppMotion.Hide(element);
        }

        _coverFrameBackground = CoverFrame.Background;
        _reflectionOpacity = (float)ReflectionView.Opacity;
        CoverFrame.Background = null;
        ElementCompositionPreview.GetElementVisual(ReflectionView).Opacity = 0;

        RevealAfter(version, 234, () =>
        {
            FadeInFrom(LyricsTitleBar, -8);
            FadeInFrom(HeadingHost, -8);
        });
        RevealAfter(version, 312, () =>
        {
            FadeInFrom(ControlDeck, 14);
            FadeInFrom(PositionSlider, 14);
            FadeInFrom(SpectrumHost, 14);
        });
        RevealAfter(version, 416, () => FadeInFrom(CanvasHost, 12));
        RevealAfter(version, 560, () =>
        {
            _entranceActive = false;
            CoverFrame.Background = _coverFrameBackground;
            _coverFrameBackground = null;
            RevealReflection();
        });
    }

    private static void FadeInFrom(FrameworkElement element, float rise)
        => _ = AppMotion.PlayAsync(element, new Vector3(0, rise, 0), Vector3.Zero, 0, 1,
            Vector3.One, Vector3.One, TimeSpan.FromMilliseconds(400));

    private void RevealReflection()
    {
        var visual = ElementCompositionPreview.GetElementVisual(ReflectionView);
        using var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(1, _reflectionOpacity);
        animation.Duration = TimeSpan.FromMilliseconds(300);
        visual.StartAnimation("Opacity", animation);
    }

    private void RevealAfter(int version, int milliseconds, Action reveal)
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(milliseconds);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _entranceTimers.Remove(timer);

            if (version == _entranceVersion && Lyrics.IsOpen) reveal();
        };
        _entranceTimers.Add(timer);
        timer.Start();
    }

    /// <summary>
    /// 收起动画：背景层整体下移淡出，内容层同时淡出；封面由共享封面动画独立回位。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 时长与分层对齐参考实现 LyciaMusic（<c>PlayerDetail.vue</c> 的背景层 600 ms 下移一屏、
    /// <c>useSharedTransition.ts</c> 的配角元素 400 ms 淡出）。此前把整个 <c>ImmersiveHost</c>
    /// 当一块淡出，除了封面之外没有任何层在「收起」，观感就是内容直接消失。
    /// </para>
    /// <para>
    /// 返回的任务在背景退场结束时完成 —— 导航要等它，只等封面飞行会把半截背景留在屏幕上。
    /// </para>
    /// </remarks>
    internal Task PlayExitAsync()
    {
        CancelCoverEntrance();

        var rise = new Vector3(0, -ExitContentRiseDip, 0);
        _ = AppMotion.PlayAsync(ContentLayer, Vector3.Zero, rise, 1, 0,
            Vector3.One, Vector3.One, ExitContentDuration);
        _ = AppMotion.PlayAsync(CommentsOverlay, Vector3.Zero, rise, 1, 0,
            Vector3.One, Vector3.One, ExitContentDuration);
        return AppMotion.PlayAsync(BackdropLayer, Vector3.Zero, Vector3.Zero, 1, 0,
            Vector3.One, Vector3.One, ExitBackdropDuration);
    }

    /// <summary>取消还没放完的入场分层，并把封面槽与倒影恢复原样。</summary>
    private void CancelCoverEntrance()
    {
        _entranceVersion++;
        foreach (var timer in _entranceTimers) timer.Stop();
        _entranceTimers.Clear();

        if (!_entranceActive)
        {
            return;
        }

        _entranceActive = false;
        CoverFrame.Background = _coverFrameBackground;
        _coverFrameBackground = null;
        ElementCompositionPreview.GetElementVisual(ReflectionView).Opacity = _reflectionOpacity;
    }

    public void OnNavigatedFrom()
    {
        // 入场分层没放完就离开：取消计时器，把封面槽与倒影恢复原样。
        CancelCoverEntrance();
        if (_themeRoot is not null) _themeRoot.ActualThemeChanged -= OnAppThemeChanged;
        _themeRoot = null;
        CloseComments(restoreFocus: false);
        Comments.CancelBadgeRequest();
        _chromeTimer.Stop();
        _pointerTimer.Stop();
        _layoutTimer.Stop();
        _chromeTimer.Tick -= OnChromeTimer;
        _pointerTimer.Tick -= OnPointerTimer;
        _layoutTimer.Tick -= OnLayoutTimer;
        ProgressTimePopup.IsOpen = false;
        _window.VisibilityChanged -= OnWindowVisibilityChanged;
        _window.RenderingStateChanged -= OnWindowRenderingStateChanged;
        _window.AppWindow.Changed -= OnAppWindowChanged;
        Player.PropertyChanged -= OnPlayerChanged;
        Lyrics.PropertyChanged -= OnLyricsChanged;
        _volumePopupOpen = false;
        _moreFlyoutOpen = false;
        CloseAppearance(restoreFocus: false);
        MoreFlyout.Hide();
        _progressSeeking = _keyboardSeeking = false;
        Player.IsSeeking = false;
        Lyrics.IsOpen = false;
        // 只降档位、不重取图：重取时缓存未命中会让播放条封面空一截（见 PlayerViewModel 的说明）。
        Player.ResetCoverDecodePixels();
        Bowen.WinUI.Media.CoverImageCache.TrimLargeImages();
        UpdatePause();
        _window.ExitImmersive();
    }

    private void OnChromeTimer(DispatcherQueueTimer sender, object args) => HideChrome();
    private void OnPointerTimer(DispatcherQueueTimer sender, object args) => UpdatePointerLocation();
    private void OnLayoutTimer(DispatcherQueueTimer sender, object args)
    {
        if (_window.IsRenderingSuspended) return;
        _layoutPending = false;
        UpdateLayoutSizing();
    }

    private void OnWindowVisibilityChanged(object? sender, WindowVisibilityChangedEventArgs args)
    {
        _windowVisible = args.Visible;
        UpdatePause();
        if (args.Visible && !_window.IsRenderingSuspended)
        {
            ShowChrome();
            _pointerTimer.Start();
            UpdatePointerLocation();
        }
        else
        {
            _pointerOverChrome = false;
            _chromeTimer.Stop();
            _pointerTimer.Stop();
        }
    }

    private void OnWindowRenderingStateChanged(object? sender, EventArgs args)
    {
        if (_window.IsRenderingSuspended)
        {
            UpdatePause();
            _chromeTimer.Stop();
            _pointerTimer.Stop();
            _layoutTimer.Stop();
        }
        else if (Lyrics.IsOpen && _windowVisible)
        {
            if (_layoutPending)
            {
                _layoutTimer.Stop();
                _layoutPending = false;
                UpdateLayoutSizing();
            }
            UpdatePause();
            _pointerTimer.Start();
            UpdatePointerLocation();
            ScheduleChromeHide();
        }
        else UpdatePause();
    }

    private void UpdatePause()
    {
        _canvas.IsPaused = !Lyrics.IsOpen || !_windowVisible || _window.IsRenderingSuspended;
        _spectrum.IsPaused = _canvas.IsPaused;
        ReflectionView.IsPaused = _canvas.IsPaused;
        var coverBackdrop = !(UsesMicaBackdrop && _immersiveEntranceCompleted);
        BackdropView.IsPaused = _canvas.IsPaused || !coverBackdrop;
        // 过渡仅暂停绘制；真正进入后台才释放设备和纹理，避免全屏切换反复重建。
        var releaseResources = !Lyrics.IsOpen || !_windowVisible || !_window.IsWindowVisible || _window.IsMinimized;
        _canvas.IsResourceSuspended = releaseResources;
        _spectrum.IsResourceSuspended = releaseResources;
        ReflectionView.IsResourceSuspended = releaseResources;
        BackdropView.IsResourceSuspended = releaseResources || !coverBackdrop;
        AmbientView.IsResourceSuspended = releaseResources || !coverBackdrop;
    }

    /// <summary>沉浸入场动画结束：此时外壳已收起，可以安全地亮出 Mica 背板。</summary>
    public void OnImmersiveEntranceCompleted()
    {
        _immersiveEntranceCompleted = true;
        ApplyBackgroundMode();
    }

    /// <summary>当前是否真的使用 Mica；系统不支持时保持封面模糊。</summary>
    private bool UsesMicaBackdrop
        => Lyrics.BackgroundMode == LyricsBackgroundMode.Mica && MainWindow.IsMicaSupported;

    /// <summary>
    /// 按当前设置摆放背景层：Mica 时收起封面模糊与氛围色，让窗口背板透出来。
    /// </summary>
    private void ApplyBackgroundMode()
    {
        // 入场期间外壳还看得见；此时切 Mica 会把外壳页面透在歌词后面，等入场结束再切。
        var mica = UsesMicaBackdrop && _immersiveEntranceCompleted;
        BackdropView.Visibility = mica ? Visibility.Collapsed : Visibility.Visible;
        AmbientView.Visibility = mica ? Visibility.Collapsed : Visibility.Visible;
        BackdropLayer.Background = mica ? TransparentBackdropBrush : CoverBackdropBrush;
        _window.SetLyricsMaterialBackdrop(mica);
        UpdatePause();
    }

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs args)
    {
        _layoutPending = true;
        _layoutTimer.Stop();
        if (!_window.IsRenderingSuspended) _layoutTimer.Start();
    }

    private void UpdateLayoutSizing()
    {
        if (_window.IsRenderingSuspended || ActualWidth <= 0 || ActualHeight <= 0) return;
        var availableHeight = Math.Max(140, MainStage.ActualHeight / 1.8);
        var size = Math.Max(140, Math.Min(Math.Min(ActualHeight * 0.45, ActualWidth * 0.4 - 84), availableHeight));
        size = Math.Min(size, 580);
        CoverFrame.Width = CoverFrame.Height = size;
        CoverDeck.Width = CoverDeck.Height = size;
        ReflectionView.Width = size;
        ReflectionView.Height = size * 0.55;
        Canvas.SetTop(ReflectionView, size + 2);
        ApplyLyricsAppearance();
        CommentsPane.Width = Math.Max(0, Math.Min(420, ActualWidth - 32));
        if (_appearanceOpen) AppearancePane.MaxHeight = Math.Max(0, ActualHeight - 178);
        UpdateChromeInsets();
        UpdateCoverScale();
    }

    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(PlayerViewModel.IsPlaying)) UpdateCoverScale();
        if (args.PropertyName == nameof(PlayerViewModel.CurrentTrackId))
        {
            ShowChrome();

            // 菜单里那一批动作要跟着曲目换。顺手把菜单收起来 ——
            // 留着它就会出现「菜单里的歌和正在播的歌对不上」。
            MoreFlyout.Hide();
            RebuildTrackMenu();

            if (Player.CurrentTrackId is > 0) _ = Comments.LoadBadgeAsync(Player.CurrentTrackId.Value);
            else Comments.CancelBadgeRequest();
            if (Comments.IsOpen)
            {
                if (Player.CurrentTrackId is > 0) _ = Comments.ShowAsync(Player.CurrentTrackId.Value, Player.Title);
                else CloseComments();
            }
        }
        if (args.PropertyName == nameof(PlayerViewModel.Title) && Comments.IsOpen) Comments.SongTitle = Player.Title;

        // 付费徽标出现/消失会占掉一截宽度，上限跟着变。
        if (args.PropertyName == nameof(PlayerViewModel.HasPayLabel)) UpdateHeadingWidth();
    }

    private void OnHeadingHostSizeChanged(object sender, SizeChangedEventArgs e) => UpdateHeadingWidth();

    /// <summary>
    /// 标题的宽度上限。**在代码里算，不用 <c>x:Bind</c> 绑 <c>ActualWidth</c>。**
    /// </summary>
    /// <remarks>
    /// <para>
    /// 与播放条那处同一个病：<c>x:Bind</c> 对 <c>ActualWidth</c> 的依赖属性回调
    /// **在首轮测量（宽度还是 0）之后没有再触发**，函数绑定就停在下限 160px（约 11 个字）上，
    /// 十来个字的标题也被截 —— 实测「想把我唱给你听——老狼&amp;王婧」（2026-10-04）。
    /// </para>
    /// <para>
    /// 首轮布局与每次尺寸变化都会走到这里，算出来的才是真的。宽度还没量到时直接返回，
    /// 免得把 0 当成真实宽度算出下限。
    /// </para>
    /// </remarks>
    private void UpdateHeadingWidth()
    {
        if (HeadingHost.ActualWidth <= 0)
        {
            return;
        }

        HeadingTitle.MaxWidth = Formats.LyricsHeadingMaxWidth(HeadingHost.ActualWidth, Player.HasPayLabel);
    }

    private void UpdateCoverScale()
    {
        var size = CoverFrame.Width;
        if (_coverPlaying == Player.IsPlaying && _coverSize == size) return;
        _coverPlaying = Player.IsPlaying;
        _coverSize = size;
        var visual = ElementCompositionPreview.GetElementVisual(CoverDeck);
        visual.CenterPoint = new Vector3((float)(CoverFrame.Width / 2), (float)(CoverFrame.Height / 2), 0);
        using var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
        var scale = Player.IsPlaying ? 1 : 0.94f;
        animation.InsertKeyFrame(1, new Vector3(scale, scale, 1));
        animation.Duration = TimeSpan.FromMilliseconds(400);
        visual.StartAnimation("Scale", animation);
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        // 最小化也会触发 DidPresenterChange；此时不能重设标题栏模板和原生按钮。
        // 异常不能穿过 AppWindow 的 COM 事件边界，否则窗口层会直接 FailFast。
        if (_window.IsChangingLyricsPresenter || _window.IsMinimized || NativeMethods.IsIconic(_windowHandle)) return;
        try
        {
            if (args.DidPresenterChange && _fullscreen != _window.IsLyricsFullscreen) SyncFullscreen();
        }
        catch (Exception exception) { _logger.LogError(exception, "同步歌词全屏状态失败"); }
    }

    private void SyncFullscreen()
    {
        var start = Stopwatch.GetTimestamp();
        _fullscreen = _window.IsLyricsFullscreen;
        FullscreenIcon.Data = ResourceIcon(_window.IsLyricsFullscreen ? "IconExitFullScreen" : "IconFullScreen");
        ToolTipService.SetToolTip(FullscreenButton, _window.IsLyricsFullscreen ? "退出全屏 (F11)" : "进入全屏 (F11)");
        UpdateChromeInsets();
        SyncTranslationButtonVisibility();
        if (Environment.GetEnvironmentVariable("BODIAN_LYRICS_DIAGNOSTICS") == "1")
            _logger.LogInformation("全屏阶段：同步标题栏 {Elapsed:F2} ms", Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        LyricsTitleBar.RecomputeDragRegions();
        if (Environment.GetEnvironmentVariable("BODIAN_LYRICS_DIAGNOSTICS") == "1")
            _logger.LogInformation("全屏阶段：重算区域累计 {Elapsed:F2} ms", Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        ShowChrome();
    }

    private void UpdateChromeInsets()
    {
        if (_window.IsMinimized || NativeMethods.IsIconic(_windowHandle)) return;
        var scale = XamlRoot?.RasterizationScale ?? 1;
        var captionWidth = _window.IsLyricsFullscreen ? 0 : Math.Max(0, _window.AppWindow.TitleBar.RightInset / scale);
        LyricsTitleBar.ApplyTemplate();
        if (FindTemplatePart<Grid>(LyricsTitleBar, "PART_LayoutRoot") is { ColumnDefinitions.Count: > 11 } layout)
            layout.ColumnDefinitions[11].Width = new GridLength(captionWidth);
        LeftChromeHost.Width = 48 + captionWidth;
        HeadingHost.Margin = new Thickness(Math.Max(64, captionWidth + 56), 0,
            Math.Max(64, captionWidth + 56), 0);
    }

    private void OnPointerActivity(object sender, PointerRoutedEventArgs args)
    {
        _keyboardInteractionActive = false;
        UpdatePointerLocation();
    }

    private void OnPointerLeftPage(object sender, PointerRoutedEventArgs args) => UpdatePointerLocation();

    private void UpdatePointerLocation()
    {
        if (!Lyrics.IsOpen || !_windowVisible || _window.IsRenderingSuspended || XamlRoot is null
            || !NativeMethods.GetCursorPos(out var screenPoint)) return;
        if (_lastCursorPoint is { } previous && (previous.X != screenPoint.X || previous.Y != screenPoint.Y))
            _keyboardInteractionActive = false;
        _lastCursorPoint = screenPoint;
        var clientPoint = screenPoint;
        var inside = NativeMethods.GetAncestor(NativeMethods.WindowFromPoint(screenPoint), 2) == _windowHandle
            && NativeMethods.ScreenToClient(_windowHandle, ref clientPoint);
        var scale = XamlRoot.RasterizationScale;
        var pointerX = clientPoint.X / scale;
        var pointerY = clientPoint.Y / scale;
        inside = inside && pointerX >= 0 && pointerX <= LyricsRoot.ActualWidth
            && pointerY >= 0 && pointerY <= LyricsRoot.ActualHeight;
        var wasOverChrome = _pointerOverChrome;
        _pointerOverChrome = inside && (pointerY <= 72 || pointerY >= LyricsRoot.ActualHeight - 94);
        if (_pointerOverChrome) ShowChrome();
        else if (wasOverChrome || (_chromeVisible && !_chromeTimer.IsRunning)) ScheduleChromeHide();
    }

    private void OnChromeKeyDown(object sender, KeyRoutedEventArgs args)
    {
        _keyboardInteractionActive = true;
        if (HasChromeKeyboardFocus()) ShowChrome();
    }

    private bool HasChromeKeyboardFocus()
    {
        if (!_keyboardInteractionActive || NativeMethods.GetForegroundWindow() != _windowHandle || XamlRoot is null
            || FocusManager.GetFocusedElement(XamlRoot) is not Control { FocusState: FocusState.Keyboard } focused)
            return false;
        DependencyObject? element = focused;
        while (element is not null)
        {
            if (element == LyricsTitleBar || element == ControlDeck || element == PositionSlider) return true;
            element = VisualTreeHelper.GetParent(element);
        }
        return false;
    }

    private void OnChromeFocusChanged(object sender, RoutedEventArgs args)
    {
        if (HasChromeKeyboardFocus()) ShowChrome();
        else ScheduleChromeHide();
    }

    private void ScheduleChromeHide()
    {
        _chromeTimer.Stop();
        if (Comments.IsOpen || !Lyrics.IsOpen || !_windowVisible || _window.IsRenderingSuspended || !_chromeVisible || _pointerOverChrome
            || _progressSeeking || _keyboardSeeking || _volumePopupOpen || _moreFlyoutOpen
            || _appearanceOpen || HasChromeKeyboardFocus()) return;
        _chromeTimer.Start();
    }

    private void ShowChrome()
    {
        if (_window.IsMinimized) return;
        SetChromeVisibility(true);
        ScheduleChromeHide();
    }

    private void HideChrome()
    {
        if (Comments.IsOpen || !Lyrics.IsOpen || !_windowVisible || _window.IsRenderingSuspended || _pointerOverChrome
            || _progressSeeking || _keyboardSeeking || _volumePopupOpen || _moreFlyoutOpen
            || _appearanceOpen || HasChromeKeyboardFocus())
            return;
        SetChromeVisibility(false);
    }

    private void SetChromeVisibility(bool visible)
    {
        if (_chromeVisible == visible) return;
        _chromeVisible = visible;
        if (!visible) ProgressTimePopup.IsOpen = false;
        _window.SetLyricsChromeVisible(visible);
        if (visible) UpdateChromeInsets();
        foreach (var element in new FrameworkElement[] { LyricsTitleBar, HeadingHost, ControlDeck })
        {
            if (element != HeadingHost) element.IsHitTestVisible = visible;
            var visual = ElementCompositionPreview.GetElementVisual(element);
            using var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
            animation.InsertKeyFrame(1, visible ? 1 : 0);
            animation.Duration = TimeSpan.FromMilliseconds(280);
            visual.StartAnimation("Opacity", animation);
        }
        foreach (var element in new FrameworkElement[] { PositionSlider, SpectrumHost })
        {
            var visual = ElementCompositionPreview.GetElementVisual(element);
            using var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
            animation.InsertKeyFrame(1, visible ? 1 : 0.25f);
            animation.Duration = TimeSpan.FromMilliseconds(280);
            visual.StartAnimation("Opacity", animation);
        }
        UpdateProgressAppearance();
        SyncTranslationButtonVisibility();
    }

    /// <summary>音量弹层开着时控制台不能自己收走，否则展开的那颗按钮会跟着消失。</summary>
    private void OnVolumePopupChanged(object? sender, EventArgs args)
    {
        _volumePopupOpen = VolumeControl.IsPopupOpen;
        if (_volumePopupOpen) ShowChrome();
        else ScheduleChromeHide();
    }

    private void OnAppThemeChanged(FrameworkElement sender, object args) => SyncPanelThemes();

    private void SyncPanelThemes()
    {
        // 歌词舞台固定深色，两块浮层跟随主窗口的实际主题，包含“跟随系统”。
        // 外观面板的亚克力刷也按这个主题取浅色 / 深色那一份。
        var theme = _window.ThemeRoot.ActualTheme;
        CommentsPane.RequestedTheme = theme;
        _commentsPanel.RequestedTheme = theme;
        CommentsImageViewer.RequestedTheme = theme;
        AppearancePane.RequestedTheme = theme;
    }

    private async void OnCommentsClick(object sender, RoutedEventArgs args)
    {
        if (Comments.IsOpen)
        {
            CloseComments();
            return;
        }
        if (_appearanceOpen) CloseAppearance(restoreFocus: false);
        if (Player.CurrentTrackId is not > 0) return;
        var loading = Comments.ShowAsync(Player.CurrentTrackId.Value, Player.Title);
        ProgressTimePopup.IsOpen = false;
        ShowChrome();
        _chromeTimer.Stop();
        // Translation 独立于 XAML 布局，只给抽屉一个轻微的滑入动画。
        var visual = ElementCompositionPreview.GetElementVisual(CommentsPane);
        using var slide = visual.Compositor.CreateScalarKeyFrameAnimation();
        slide.InsertKeyFrame(0, 28);
        slide.InsertKeyFrame(1, 0);
        slide.Duration = TimeSpan.FromMilliseconds(220);
        visual.StartAnimation("Translation.X", slide);
        // 在点击这一刻取焦点来源：等下一拍时焦点可能已经被面板内部接管。
        var focusState = CurrentFocusVisualState;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (Comments.IsOpen) _commentsPanel.FocusCloseButton(focusState);
        });
        await loading;
    }

    private void CloseComments(bool restoreFocus = true)
    {
        var wasOpen = Comments.IsOpen;
        Comments.Close();
        // ★ 用 Programmatic 而不是 Keyboard：Keyboard 会画出焦点框，收起评论后那颗按钮上
        //   留一圈光圈，看着像选中态没清掉。Programmatic 同样把焦点移回去（键盘继续往下走不受影响），
        //   只是不画那圈框。
        if (wasOpen && restoreFocus) CommentsButton.FocusButton(FocusState.Programmatic);
        ScheduleChromeHide();
    }

    private void OnCommentsDismissTapped(object sender, TappedRoutedEventArgs args)
    {
        CloseComments();
        args.Handled = true;
    }

    private void OnImageCloseRequested(object? sender, EventArgs args)
    {
        Comments.CloseImage();
        _commentsPanel.FocusCloseButton(CurrentFocusVisualState);
    }

    /// <summary>
    /// 移焦点的同时要不要画焦点框：当前交互来自键盘才画。
    /// </summary>
    /// <remarks>
    /// 鼠标点击后用 <see cref="FocusState.Keyboard"/> 移焦点，按钮上会留下一圈白边
    /// （关闭评论区那颗最明显）。Programmatic 同样转移焦点，只是不画框。
    /// </remarks>
    private FocusState CurrentFocusVisualState =>
        FocusManager.GetFocusedElement(XamlRoot) is Control { FocusState: FocusState.Keyboard }
            ? FocusState.Keyboard
            : FocusState.Programmatic;

    private void OnBackClick(object sender, RoutedEventArgs args) => _window.GoBack();

    /// <remarks>
    /// 目标页要带曲目构造，而这里只拿得到 <see cref="PlayerViewModel"/> 上的当前曲目。
    /// 曲目为空（理论上按钮那时也不可见）时什么都不做。
    /// </remarks>
    private void OnMvClick(object sender, RoutedEventArgs args)
    {
        if (Player.CurrentTrack is { } track)
        {
            _navigation.Navigate(_mvFactory(track));
        }
    }

    /// <summary>传输组里的队列按钮。抽屉本身归主窗口 —— 它要盖住整个内容区，这一层放不下。</summary>
    private void OnQueueClick(object sender, RoutedEventArgs args) => _window.ToggleQueue();

    /// <summary>
    /// 底部「更多」的菜单状态按当前曲目重建。
    /// </summary>
    /// <remarks>
    /// 每次切歌现造一个：<c>TrackActionsViewModel</c> 的状态跟着曲目走（喜欢态、歌单选择面），
    /// 复用它只会带来「菜单还挂着上一首」这类问题。装配点与曲目行那颗是同一个。
    /// </remarks>
    private void RebuildTrackMenu()
        => MoreMenu.ViewModel = Player.CurrentTrack is { } track ? _trackActions.Create(track) : null;

    /// <remarks>
    /// ★ <b>Flyout 是 popup，不受 <c>SetChromeVisibility</c> 管</b> —— 不把它算进
    /// <see cref="ScheduleChromeHide"/> 与 <see cref="HideChrome"/> 的守卫，
    /// chrome 一淡出就会剩一个悬空的菜单挂在那儿。
    /// </remarks>
    private void OnMoreFlyoutOpening(object? sender, object e)
    {
        _moreFlyoutOpen = true;
        MoreMenu.Initialize();

        // ShowChrome 内部会调 ScheduleChromeHide，而后者见 _moreFlyoutOpen 为真就停表返回。
        ShowChrome();

        // popup 还没建出来时只能先设内容那层；presenter 的底色等 Opened 再补。
        ApplyFlyoutTheme(MoreMenu);
    }

    private void OnMoreFlyoutOpened(object? sender, object e) => ApplyFlyoutTheme(MoreMenu);

    private void OnQualityFlyoutOpening(object? sender, object e) => ApplyFlyoutTheme(QualityPicker);

    private void OnQualityFlyoutOpened(object? sender, object e) => ApplyFlyoutTheme(QualityPicker);

    /// <summary>
    /// 弹层跟随主窗口主题，而不是继承歌词页固定的深色（评论面板与外观面板同理，见 <see cref="SyncPanelThemes"/>）。
    /// </summary>
    private void ApplyFlyoutTheme(FrameworkElement content)
        => PopupTheme.Apply(content, _window.ThemeRoot.ActualTheme);

    private void OnMoreFlyoutClosed(object? sender, object e)
    {
        _moreFlyoutOpen = false;
        MoreMenu.Reset();
        ScheduleChromeHide();
    }

    /// <summary>菜单里某个动作要求关窗。它是内容控件，拿不到这个 Flyout，只能往上抛。</summary>
    private void OnMoreMenuCloseRequested(object? sender, EventArgs e) => MoreFlyout.Hide();
    private void OnFollowClick(object sender, RoutedEventArgs args) => _canvas.ResumeFollowing();
    private async Task ToggleFullscreenAsync()
    {
        if (_window.IsChangingLyricsPresenter) return;
        FullscreenButton.IsEnabled = false;
        try
        {
            await _window.ToggleImmersiveFullscreenAsync();
            if (Lyrics.IsOpen) SyncFullscreen();
        }
        catch (Exception exception) { _logger.LogError(exception, "切换歌词全屏失败"); }
        finally { FullscreenButton.IsEnabled = true; }
    }
    private void OnTranslationClick(object sender, RoutedEventArgs args)
    {
        Lyrics.ShowTranslation = !Lyrics.ShowTranslation;
        SyncTranslation();
    }

    /// <summary>
    /// 把译文按钮的外观对齐到 <see cref="LyricsViewModel.HasTranslation"/> 与
    /// <see cref="LyricsViewModel.ShowTranslation"/>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>开关只差不透明度，不换图标。</b> 开与关是同一个动作的两态，换字形就要维护两张图；
    /// 而「变淡」本来就是这套界面里既有的「没生效」表达（禁用态也是靠前景色变淡）。
    /// 提示文案跟着翻面，鼠标停在上面能看出下一步会发生什么。
    /// </para>
    /// <para>
    /// <b>这首歌没有译文时整颗不显示</b>，由 <see cref="SyncTranslationButtonVisibility"/> 负责 ——
    /// 早先是留着禁用并提示「没有译文」，但一颗点不动的按钮挂在歌词上，用户只会去戳它。
    /// 这里碰到没译文就把不透明度交回 1：藏起来的是按钮自己，留着上次的 0.45 反而会在下次
    /// 显示出来时先淡一下。（<c>IsEnabled</c> 那条绑定留着不删 —— 万一显隐逻辑将来有疏漏，
    /// 它还能兜住「点了没反应」。）
    /// </para>
    /// </remarks>
    private void SyncTranslation()
    {
        SyncTranslationButtonVisibility();

        if (!Lyrics.HasTranslation)
        {
            TranslationIcon.Opacity = 1;
            return;
        }

        TranslationIcon.Opacity = Lyrics.ShowTranslation ? 1 : 0.45;
        ToolTipService.SetToolTip(TranslationButton, Lyrics.ShowTranslation ? "关闭译文" : "显示译文");
    }

    /// <summary>
    /// 译文按钮的显隐：没译文、chrome 收起（沉浸）、全屏 —— 三条任一成立就不显示。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>为什么要单独算一份</b>：它已经从标题栏搬到歌词区域里，不在
    /// <see cref="SetChromeVisibility"/> 遍历的那几块里，那套淡入淡出管不到它。
    /// </para>
    /// <para>
    /// <b>没译文就不显示</b>：留一颗点不动的按钮挂在歌词上，用户只会去戳它。
    /// </para>
    /// <para>
    /// <b>全屏时连 chrome 可见也藏</b>：全屏是「专心看词」的模式，歌词上不该压着一颗按钮。
    /// </para>
    /// <para>
    /// <b>三个入口都要调它</b> —— <see cref="SetChromeVisibility"/>（鼠标进出）、
    /// <see cref="SyncFullscreen"/>（F11 切换）、<see cref="SyncTranslation"/>
    /// （<c>HasTranslation</c> 会随换歌变），少一处就会出现「状态变了按钮没跟上」。
    /// </para>
    /// </remarks>
    private void SyncTranslationButtonVisibility()
        => TranslationButton.Visibility = _chromeVisible && !_window.IsLyricsFullscreen && Lyrics.HasTranslation
            ? Visibility.Visible
            : Visibility.Collapsed;

    // ── 歌词外观面板 ────────────────────────────────────────────────────────

    /// <summary>字体列表与设置页一致：首项「默认字体」，其后是系统已装字体。</summary>
    private static IReadOnlyList<FontOption> BuildFontOptions(FontSettingsViewModel font)
    {
        var options = new List<FontOption> { FontOption.Default };
        options.AddRange(font.Fonts);
        return options;
    }

    private int FontIndexOf(string fontFamily)
    {
        for (var index = 0; index < _fontOptions.Count; index++)
        {
            if (string.Equals(_fontOptions[index].FamilyName, fontFamily, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return 0;
    }

    private string FontNameAt(int index)
        => index >= 0 && index < _fontOptions.Count ? _fontOptions[index].FamilyName : string.Empty;

    /// <summary>
    /// 圆点色板。与桌面歌词一样在代码里生成，颜色定义只留 `LyricsSettings.HighlightPalette` 一处。
    /// </summary>
    private void BuildColorPalette()
    {
        foreach (var (argb, name) in LyricsSettings.HighlightPalette)
        {
            var color = ToColor(argb);
            var dot = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(color),
                VerticalAlignment = VerticalAlignment.Center,

                // 白色与浅色底上的点都需要一圈中性描边，否则边界看不出来。
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0x80, 0x80, 0x80)),
                BorderThickness = new Thickness(1),
            };

            var button = new Button
            {
                Content = dot,
                Width = 28,
                Height = 28,
                MinWidth = 0,
                Padding = new Thickness(5),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
            };
            button.Click += (_, _) => Lyrics.SetHighlightColor(argb);
            ToolTipService.SetToolTip(button, name);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"高亮颜色：{name}");
            _colorDots.Add((dot, argb));
            LyricsColorPalette.Children.Add(button);
        }
    }

    private void UpdateColorPaletteSelection()
    {
        foreach (var (dot, argb) in _colorDots)
        {
            var selected = Lyrics.HighlightColorArgb == argb;

            // 选中用品牌强调色描边：白色描边在浅色底上看不出来。
            dot.BorderBrush = new SolidColorBrush(selected
                ? Color.FromArgb(0xFF, 0x00, 0xD9, 0xA0)
                : Color.FromArgb(0x66, 0x80, 0x80, 0x80));
            dot.BorderThickness = new Thickness(selected ? 2 : 1);
        }
    }

    /// <summary>把 ViewModel 的当前值回填到面板控件；回填期间不反向写设置。</summary>
    private void SyncAppearanceControls()
    {
        _syncingAppearance = true;
        try
        {
            LyricsFontPicker.SelectedIndex = FontIndexOf(Lyrics.FontFamily);
            LyricsBackgroundMicaItem.IsEnabled = MainWindow.IsMicaSupported;
            LyricsBackgroundMicaItem.Content = MainWindow.IsMicaSupported ? "Mica" : "Mica（需要 Windows 11）";
            LyricsBackgroundCombo.SelectedIndex = UsesMicaBackdrop ? 1 : 0;
            LyricsFontSizeSlider.Value = Lyrics.FontSize;
            LyricsFontSizeValue.Text = ((int)Math.Round(Lyrics.FontSize)).ToString();
            LyricsLineGapSlider.Value = Lyrics.LineGap;
            LyricsLineGapValue.Text = ((int)Math.Round(Lyrics.LineGap)).ToString();
            LyricsTranslationSyncToggle.IsOn = Lyrics.SyncTranslationHighlight;
            LyricsTranslationFontSizeSlider.Value = Math.Round(Lyrics.TranslationFontScale * 100);
            LyricsTranslationFontSizeValue.Text = $"{(int)Math.Round(Lyrics.TranslationFontScale * 100)}%";
            LyricsTranslationLineGapSlider.Value = Lyrics.TranslationLineGap;
            LyricsTranslationLineGapValue.Text = ((int)Math.Round(Lyrics.TranslationLineGap)).ToString();
            LyricsEdgeBlurSlider.Value = Math.Round(Lyrics.EdgeBlurRatio * 100);
            LyricsEdgeBlurValue.Text = $"{(int)Math.Round(Lyrics.EdgeBlurRatio * 100)}%";
            SyncAlignmentButtons(Lyrics.Alignment);
            LyricsHorizontalOffsetSlider.Value = Lyrics.HorizontalOffsetPercent;
            LyricsHorizontalOffsetValue.Text = FormatOffsetPercent(Lyrics.HorizontalOffsetPercent);
            LyricsVerticalOffsetSlider.Value = Lyrics.VerticalOffsetPercent;
            LyricsVerticalOffsetValue.Text = FormatOffsetPercent(Lyrics.VerticalOffsetPercent);
            UpdateColorPaletteSelection();
        }
        finally
        {
            _syncingAppearance = false;
        }
    }

    private void OnLyricsFontPickerSelectionChanged(DependencyObject sender, DependencyProperty dp)
    {
        if (_syncingAppearance || !_appearanceOpen) return;
        Lyrics.SetFontFamily(FontNameAt(LyricsFontPicker.SelectedIndex));
    }

    private void OnLyricsBackgroundSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_syncingAppearance || !_appearanceOpen) return;
        Lyrics.SetBackgroundMode(LyricsBackgroundCombo.SelectedIndex == 1
            ? LyricsBackgroundMode.Mica
            : LyricsBackgroundMode.CoverBlur);
    }

    private void OnLyricsFontSizeChanged(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (_syncingAppearance || !_appearanceOpen) return;
        LyricsFontSizeValue.Text = ((int)Math.Round(args.NewValue)).ToString();
        Lyrics.SetFontSize(args.NewValue);
    }

    private void OnLyricsLineGapChanged(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (_syncingAppearance || !_appearanceOpen) return;
        LyricsLineGapValue.Text = ((int)Math.Round(args.NewValue)).ToString();
        Lyrics.SetLineGap(args.NewValue);
    }

    private void OnTranslationSyncToggled(object sender, RoutedEventArgs args)
    {
        if (_syncingAppearance || !_appearanceOpen) return;
        Lyrics.SetSyncTranslationHighlight(LyricsTranslationSyncToggle.IsOn);
    }

    private void OnLyricsTranslationFontSizeChanged(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (_syncingAppearance || !_appearanceOpen) return;
        LyricsTranslationFontSizeValue.Text = $"{(int)Math.Round(args.NewValue)}%";
        Lyrics.SetTranslationFontScale(args.NewValue / 100);
    }

    private void OnLyricsTranslationLineGapChanged(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (_syncingAppearance || !_appearanceOpen) return;
        LyricsTranslationLineGapValue.Text = ((int)Math.Round(args.NewValue)).ToString();
        Lyrics.SetTranslationLineGap(args.NewValue);
    }

    private void OnLyricsEdgeBlurChanged(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (_syncingAppearance || !_appearanceOpen) return;
        LyricsEdgeBlurValue.Text = $"{(int)Math.Round(args.NewValue)}%";
        Lyrics.SetEdgeBlurRatio(args.NewValue / 100);
    }

    private void OnAlignLeftClick(object sender, RoutedEventArgs args) => SetAlignment(LyricsAlignment.Left);
    private void OnAlignCenterClick(object sender, RoutedEventArgs args) => SetAlignment(LyricsAlignment.Center);
    private void OnAlignRightClick(object sender, RoutedEventArgs args) => SetAlignment(LyricsAlignment.Right);

    private void SetAlignment(LyricsAlignment alignment)
    {
        if (!_appearanceOpen) return;
        SyncAlignmentButtons(alignment);
        Lyrics.SetAlignment(alignment);
    }

    private void SyncAlignmentButtons(LyricsAlignment alignment)
    {
        AlignLeftButton.IsChecked = alignment == LyricsAlignment.Left;
        AlignCenterButton.IsChecked = alignment == LyricsAlignment.Center;
        AlignRightButton.IsChecked = alignment == LyricsAlignment.Right;
    }

    private void OnResetFontSizeClick(object sender, RoutedEventArgs args)
    {
        if (!_appearanceOpen) return;
        Lyrics.SetFontSize(LyricsSettings.DefaultFontSize);
    }

    private void OnResetLineGapClick(object sender, RoutedEventArgs args)
    {
        if (!_appearanceOpen) return;
        Lyrics.SetLineGap(LyricsSettings.DefaultLineGap);
    }

    private void OnResetTranslationFontScaleClick(object sender, RoutedEventArgs args)
    {
        if (!_appearanceOpen) return;
        Lyrics.SetTranslationFontScale(LyricsSettings.DefaultTranslationFontScale);
    }

    private void OnResetTranslationLineGapClick(object sender, RoutedEventArgs args)
    {
        if (!_appearanceOpen) return;
        Lyrics.SetTranslationLineGap(LyricsSettings.DefaultTranslationLineGap);
    }

    private void OnResetEdgeBlurClick(object sender, RoutedEventArgs args)
    {
        if (!_appearanceOpen) return;
        Lyrics.SetEdgeBlurRatio(LyricsSettings.DefaultEdgeBlurRatio);
    }

    private void OnLyricsHorizontalOffsetChanged(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (_syncingAppearance || !_appearanceOpen) return;
        LyricsHorizontalOffsetValue.Text = FormatOffsetPercent(args.NewValue);
        Lyrics.SetHorizontalOffsetPercent(args.NewValue);
    }

    private void OnLyricsVerticalOffsetChanged(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (_syncingAppearance || !_appearanceOpen) return;
        LyricsVerticalOffsetValue.Text = FormatOffsetPercent(args.NewValue);
        Lyrics.SetVerticalOffsetPercent(args.NewValue);
    }

    private void OnResetHorizontalOffsetClick(object sender, RoutedEventArgs args)
    {
        if (!_appearanceOpen) return;
        Lyrics.SetHorizontalOffsetPercent(LyricsSettings.DefaultOffsetPercent);
    }

    private void OnResetVerticalOffsetClick(object sender, RoutedEventArgs args)
    {
        if (!_appearanceOpen) return;
        Lyrics.SetVerticalOffsetPercent(LyricsSettings.DefaultOffsetPercent);
    }

    private static string FormatOffsetPercent(double percent)
    {
        var rounded = (int)Math.Round(percent);
        return rounded > 0 ? $"+{rounded}%" : $"{rounded}%";
    }

    /// <summary>外观按钮：开、关共用同一颗；外观面板与评论面板互斥。</summary>
    private void OnAppearanceClick(object sender, RoutedEventArgs args)
    {
        if (_appearanceOpen)
        {
            CloseAppearance();
            return;
        }

        if (Comments.IsOpen) CloseComments(restoreFocus: false);
        OpenAppearance();
    }

    private void OpenAppearance()
    {
        if (_appearanceOpen || !Lyrics.IsOpen) return;
        _appearanceOpen = true;
        _chromeTimer.Stop();
        SyncAppearanceControls();
        SyncPanelThemes();
        AppearanceOverlay.Visibility = Visibility.Visible;
        AppearancePane.MaxHeight = Math.Max(0, ActualHeight - 190);

        // 从左侧滑入，与评论面板从右侧滑入对称。
        var visual = ElementCompositionPreview.GetElementVisual(AppearancePane);
        using var slide = visual.Compositor.CreateScalarKeyFrameAnimation();
        slide.InsertKeyFrame(0, -28);
        slide.InsertKeyFrame(1, 0);
        slide.Duration = TimeSpan.FromMilliseconds(220);
        visual.StartAnimation("Translation.X", slide);

        ShowChrome();
        var focusState = CurrentFocusVisualState;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_appearanceOpen) AppearanceCloseButton.Focus(focusState);
        });
    }

    private void CloseAppearance(bool restoreFocus = true)
    {
        if (!_appearanceOpen) return;
        _appearanceOpen = false;
        AppearanceOverlay.Visibility = Visibility.Collapsed;
        if (restoreFocus) AppearanceButton.Focus(FocusState.Programmatic);
        ScheduleChromeHide();
    }

    private void OnAppearanceCloseClick(object sender, RoutedEventArgs args) => CloseAppearance();

    private void OnAppearanceDismissTapped(object sender, TappedRoutedEventArgs args)
    {
        CloseAppearance();
        args.Handled = true;
    }

    private static Color ToColor(uint argb) => Color.FromArgb(
        (byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    /// <summary>把当前外观偏好下发给 Win2D 画布。</summary>
    private void ApplyLyricsAppearance()
    {
        _canvas.SetFontSize(Lyrics.FontSize);
        _canvas.SetFontFamily(Lyrics.FontFamily);
        _canvas.SetLineGap(Lyrics.LineGap);
        _canvas.SetSyncTranslationHighlight(Lyrics.SyncTranslationHighlight);
        _canvas.SetTranslationFontScale(Lyrics.TranslationFontScale);
        _canvas.SetTranslationLineGap(Lyrics.TranslationLineGap);
        _canvas.SetEdgeBlurRatio(Lyrics.EdgeBlurRatio);
        _canvas.SetAlignment(Lyrics.Alignment);
        _canvas.SetHorizontalOffsetPercent(Lyrics.HorizontalOffsetPercent);
        _canvas.SetVerticalOffsetPercent(Lyrics.VerticalOffsetPercent);
        _canvas.SetHighlightColor(ToColor(Lyrics.HighlightColorArgb));
    }

    /// <summary>译文按钮与歌词外观都由 ViewModel 驱动，任一变化都要重画。</summary>
    private void OnLyricsChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(LyricsViewModel.HasTranslation) or nameof(LyricsViewModel.ShowTranslation))
        {
            SyncTranslation();
            return;
        }

        if (args.PropertyName is nameof(LyricsViewModel.BackgroundMode))
        {
            ApplyBackgroundMode();
            if (_appearanceOpen) SyncAppearanceControls();
            return;
        }

        if (args.PropertyName is nameof(LyricsViewModel.FontFamily)
            or nameof(LyricsViewModel.FontSize)
            or nameof(LyricsViewModel.Alignment)
            or nameof(LyricsViewModel.HighlightColorArgb)
            or nameof(LyricsViewModel.LineGap)
            or nameof(LyricsViewModel.SyncTranslationHighlight)
            or nameof(LyricsViewModel.TranslationFontScale)
            or nameof(LyricsViewModel.TranslationLineGap)
            or nameof(LyricsViewModel.EdgeBlurRatio)
            or nameof(LyricsViewModel.HorizontalOffsetPercent)
            or nameof(LyricsViewModel.VerticalOffsetPercent))
        {
            ApplyLyricsAppearance();
            if (_appearanceOpen) SyncAppearanceControls();
        }
    }

    private async void OnFullscreenClick(object sender, RoutedEventArgs args) => await ToggleFullscreenAsync();
    private async void OnFullscreenInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    { args.Handled = true; await ToggleFullscreenAsync(); }
    private async void OnEscapeInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (_appearanceOpen) { CloseAppearance(); return; }
        if (Comments.IsImageOpen) { Comments.CloseImage(); _commentsPanel.FocusCloseButton(); return; }
        if (Comments.IsThreadOpen) { Comments.CloseThread(); _commentsPanel.FocusCloseButton(); return; }
        if (Comments.IsOpen) { CloseComments(); return; }
        if (_window.IsLyricsFullscreen) await ToggleFullscreenAsync();
        else _window.GoBack();
    }
    private void OnPlayPauseInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (Comments.IsOpen || PositionSlider.FocusState == FocusState.Keyboard || VolumeControl.IsPopupOpen) return;
        Player.TogglePlayPauseCommand.Execute(null);
        ShowChrome();
        args.Handled = true;
    }

    private void OnProgressPressed(object sender, PointerRoutedEventArgs args)
    {
        if (!args.GetCurrentPoint(PositionSlider).Properties.IsLeftButtonPressed) return;
        _progressSeeking = true;
        Player.IsSeeking = true;
        OnProgressPointerMoved(sender, args);
        ShowChrome();
    }
    private void OnProgressReleased(object sender, PointerRoutedEventArgs args)
    {
        if (!_progressSeeking) return;
        _progressSeeking = false;
        _ = Player.SeekToAsync(PositionSlider.Value);
        UpdateProgressAppearance();
        UpdateProgressTime();
        ShowChrome();
    }
    private void OnProgressCanceled(object sender, PointerRoutedEventArgs args)
    {
        _progressSeeking = false;
        Player.IsSeeking = false;
        UpdateProgressAppearance();
        ProgressTimePopup.IsOpen = false;
        ScheduleChromeHide();
    }
    private void OnProgressKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down
            or VirtualKey.Home or VirtualKey.End or VirtualKey.PageUp or VirtualKey.PageDown)
        {
            _keyboardSeeking = true;
            Player.IsSeeking = true;
            UpdateProgressTime();
            ShowChrome();
        }
    }
    private void OnProgressKeyUp(object sender, KeyRoutedEventArgs args)
    {
        if (!_keyboardSeeking) return;
        _keyboardSeeking = false;
        _ = Player.SeekToAsync(PositionSlider.Value);
        UpdateProgressAppearance();
        ProgressTimePopup.IsOpen = false;
        ShowChrome();
    }

    private void OnProgressLoaded(object sender, RoutedEventArgs args)
    {
        PositionSlider.ApplyTemplate();
        _progressThumb = FindTemplatePart<FrameworkElement>(PositionSlider, "HorizontalThumb");
        _progressTrack = FindTemplatePart<Rectangle>(PositionSlider, "HorizontalTrackRect");
        _progressFill = FindTemplatePart<Rectangle>(PositionSlider, "HorizontalDecreaseRect");
        UpdateProgressAppearance();
    }

    private static T? FindTemplatePart<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        if (root is T element && element.Name == name) return element;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var match = FindTemplatePart<T>(VisualTreeHelper.GetChild(root, index), name);
            if (match is not null) return match;
        }
        return null;
    }

    private void UpdateProgressAppearance()
    {
        var active = _chromeVisible && (_progressPointerOver || _progressSeeking || _keyboardSeeking || PositionSlider.FocusState == FocusState.Keyboard);
        if (_progressThumb is not null) _progressThumb.Opacity = active ? 1 : 0;
        if (_progressTrack is not null) _progressTrack.Height = active ? 5 : 2;
        if (_progressFill is not null) _progressFill.Height = active ? 5 : 2;
    }

    private void OnProgressPointerMoved(object sender, PointerRoutedEventArgs args)
    {
        var point = args.GetCurrentPoint(PositionSlider).Position;
        _progressPointerX = point.X;
        _progressPointerOver = point.X >= 0 && point.X <= PositionSlider.ActualWidth
            && point.Y >= 0 && point.Y <= PositionSlider.ActualHeight;
        UpdateProgressAppearance();
        UpdateProgressTime();
    }

    private void OnProgressPointerExited(object sender, PointerRoutedEventArgs args)
    {
        _progressPointerOver = false;
        UpdateProgressAppearance();
        if (!_progressSeeking && !_keyboardSeeking) ProgressTimePopup.IsOpen = false;
    }

    private void OnProgressValueChanged(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (_chromeTimer is null) return;
        if (_progressSeeking || _keyboardSeeking) UpdateProgressTime();
    }

    private void UpdateProgressTime()
    {
        if (XamlRoot is null || PositionSlider.ActualWidth <= 14) return;
        if (!_progressPointerOver && !_progressSeeking && !_keyboardSeeking)
        {
            ProgressTimePopup.IsOpen = false;
            return;
        }
        var thumbWidth = _progressThumb?.ActualWidth ?? 14;
        var fraction = Math.Clamp((_progressPointerX - thumbWidth / 2)
            / Math.Max(1, PositionSlider.ActualWidth - thumbWidth), 0, 1);
        var seconds = _progressSeeking || _keyboardSeeking ? PositionSlider.Value : fraction * Player.DurationSeconds;
        ProgressTimeText.Text = $"{Formats.Seconds(seconds)} / {Formats.Seconds(Player.DurationSeconds)}";
        ProgressTimeBubble.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var origin = PositionSlider.TransformToVisual(null).TransformPoint(new Point());
        var hostOrigin = LyricsRoot.TransformToVisual(null).TransformPoint(new Point());
        var size = ProgressTimeBubble.DesiredSize;
        var pointerX = _keyboardSeeking
            ? thumbWidth / 2 + PositionSlider.Value / Player.ProgressMaximum * (PositionSlider.ActualWidth - thumbWidth)
            : _progressPointerX;
        ProgressTimePopup.XamlRoot = XamlRoot;
        ProgressTimePopup.HorizontalOffset = Math.Clamp(origin.X + pointerX - size.Width / 2,
            8, Math.Max(8, XamlRoot.Size.Width - size.Width - 8)) - hostOrigin.X;
        ProgressTimePopup.VerticalOffset = Math.Max(0, origin.Y - size.Height - 8) - hostOrigin.Y;
        ProgressTimePopup.IsOpen = true;
    }
}
