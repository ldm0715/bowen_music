using System.ComponentModel;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using Bowen.Core.Media;
using Bowen.Core.Models;
using Bowen.Core.Services;
using Bowen.WinUI.Controls;
using Bowen.WinUI.Media;
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
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
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
    private readonly List<(Border Dot, uint Color)> _solidColorDots = [];
    private Border? _customSolidColorDot;
    private readonly CoverPaletteLoader _paletteLoader;
    private Color? _autoSolidColor;
    private int _autoColorRequest;
    private readonly SolidColorBrush _solidBackgroundBrush = new(Color.FromArgb(0xFF, 0x0B, 0x12, 0x22));
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
        BuildSolidColorPalette();
        _paletteLoader = new CoverPaletteLoader(
            (Application.Current.Resources["BowenLoggerFactory"] as ILoggerFactory)?.CreateLogger<CoverPaletteLoader>());
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
        if (Lyrics.BackgroundMode == LyricsBackgroundMode.SolidColor && Lyrics.BackgroundColorAuto)
        {
            _ = RefreshSolidColorAsync();
        }

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
        var solid = Lyrics.BackgroundMode == LyricsBackgroundMode.SolidColor;
        var backdropActive = !solid && !(UsesMicaBackdrop && _immersiveEntranceCompleted);
        BackdropView.IsPaused = _canvas.IsPaused || !backdropActive;
        // 过渡仅暂停绘制；真正进入后台才释放设备和纹理，避免全屏切换反复重建。
        var releaseResources = !Lyrics.IsOpen || !_windowVisible || !_window.IsWindowVisible || _window.IsMinimized;
        _canvas.IsResourceSuspended = releaseResources;
        _spectrum.IsResourceSuspended = releaseResources;
        ReflectionView.IsResourceSuspended = releaseResources;
        BackdropView.IsResourceSuspended = releaseResources || !backdropActive;
        AmbientView.IsResourceSuspended = releaseResources || !backdropActive || UsesCustomImage;
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

    /// <summary>自定义背景图片的完整路径；未选择时为空串。</summary>
    private string BackgroundImagePath
        => string.IsNullOrEmpty(Lyrics.BackgroundImageFile)
            ? string.Empty
            : AppPaths.LyricsBackgroundFile(Lyrics.BackgroundImageFile);

    /// <summary>选了自定义图片且文件还在；文件丢失时退回封面模糊。</summary>
    private bool UsesCustomImage
        => Lyrics.BackgroundMode == LyricsBackgroundMode.CustomImage
            && Lyrics.BackgroundImageFile.Length > 0
            && File.Exists(BackgroundImagePath);

    /// <summary>纯色背景当前实际使用的颜色：自动模式下取封面派生色。</summary>
    private Color SolidDisplayColor
        => Lyrics.BackgroundColorAuto && _autoSolidColor is { } auto
            ? auto
            : ToColor(Lyrics.BackgroundColorArgb);

    /// <summary>
    /// 按当前设置摆放背景层：Mica 收起封面层；自定义图片换成用户选的图并加暗度蒙版。
    /// </summary>
    private void ApplyBackgroundMode()
    {
        // 入场期间外壳还看得见；此时切 Mica 会把外壳页面透在歌词后面，等入场结束再切。
        var mica = UsesMicaBackdrop && _immersiveEntranceCompleted;
        var custom = UsesCustomImage;
        var solid = Lyrics.BackgroundMode == LyricsBackgroundMode.SolidColor;
        BackdropView.Visibility = mica || solid ? Visibility.Collapsed : Visibility.Visible;
        // 只在真正会显示背景层时换源。切到 Mica / 纯色会挂起画布，
        // 此时清空来源只会白白触发一次解码，来回切换就会反复分配纹理。
        if (custom)
        {
            BackdropView.LocalPath = BackgroundImagePath;
        }
        else if (!solid && !UsesMicaBackdrop)
        {
            BackdropView.LocalPath = string.Empty;
        }
        BackdropView.BlurDip = custom ? Lyrics.BackgroundImageBlur : 52;
        BackdropView.ImageOpacity = custom ? 1 : 0.72;
        BackdropView.Saturation = custom ? 0.85f : 0.68f;
        BackdropView.FocusX = custom ? Lyrics.BackgroundImageFocusX : 0.5;
        BackdropView.FocusY = custom ? Lyrics.BackgroundImageFocusY : 0.5;
        BackdropView.Zoom = custom ? Lyrics.BackgroundImageZoom : 1;
        AmbientView.Visibility = mica || custom || solid ? Visibility.Collapsed : Visibility.Visible;
        BackgroundDimLayer.Opacity = custom ? Lyrics.BackgroundImageDim : 0;
        if (solid) _solidBackgroundBrush.Color = SolidDisplayColor;

        BackdropLayer.Background = mica
            ? TransparentBackdropBrush
            : solid ? _solidBackgroundBrush : CoverBackdropBrush;
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
        if (args.PropertyName is nameof(PlayerViewModel.CurrentTrackId) or nameof(PlayerViewModel.CurrentCoverUri))
        {
            _ = RefreshSolidColorAsync();
        }

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

    /// <summary>纯色背景的预设色板，与高亮色板同一套圆点样式。</summary>
    private void BuildSolidColorPalette()
    {
        foreach (var (argb, name) in LyricsSettings.BackgroundPalette)
        {
            var value = argb;
            var dot = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(ToColor(value)),
                VerticalAlignment = VerticalAlignment.Center,
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
            button.Click += (_, _) => Lyrics.SetBackgroundColor(value, auto: false);
            ToolTipService.SetToolTip(button, name);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"背景颜色：{name}");
            _solidColorDots.Add((dot, value));
            SolidColorPalette.Children.Add(button);
        }

        // 最后一个圆点固定是「自定义颜色」：圆点本身应用上次保存的自定义色，
        // 右下角的铅笔才是编辑入口；两者分开，避免点一下圆点就弹出色盘。
        _customSolidColorDot = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(ToColor(Lyrics.BackgroundCustomColorArgb)),
            VerticalAlignment = VerticalAlignment.Center,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
        };

        var applyButton = new Button
        {
            Content = _customSolidColorDot,
            Width = 28,
            Height = 28,
            MinWidth = 0,
            Padding = new Thickness(5),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
        };
        applyButton.Click += (_, _) => Lyrics.SetBackgroundColor(Lyrics.BackgroundCustomColorArgb, auto: false);
        ToolTipService.SetToolTip(applyButton, "应用自定义颜色");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(applyButton, "应用自定义背景颜色");

        var pencilButton = new Button
        {
            Width = 14,
            Height = 14,
            MinWidth = 0,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(Color.FromArgb(0xC0, 0, 0, 0)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Content = new Icon
            {
                Data = ResourceIcon("IconEdit"),
                Size = 7,
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)),
            },
        };
        pencilButton.Click += (_, _) => _ = OpenSolidColorDialogAsync();
        ToolTipService.SetToolTip(pencilButton, "编辑自定义颜色");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(pencilButton, "编辑自定义背景颜色");

        var customCell = new Grid { Width = 28, Height = 28 };
        customCell.Children.Add(applyButton);
        customCell.Children.Add(pencilButton);
        SolidColorPalette.Children.Add(customCell);
    }

    private void UpdateSolidColorSelection()
    {
        foreach (var (dot, argb) in _solidColorDots)
        {
            var selected = !Lyrics.BackgroundColorAuto && Lyrics.BackgroundColorArgb == argb;
            dot.BorderBrush = new SolidColorBrush(selected
                ? Color.FromArgb(0xFF, 0x00, 0xD9, 0xA0)
                : Color.FromArgb(0x66, 0x80, 0x80, 0x80));
            dot.BorderThickness = new Thickness(selected ? 2 : 1);
        }

        if (_customSolidColorDot is { } custom)
        {
            custom.Background = new SolidColorBrush(ToColor(Lyrics.BackgroundCustomColorArgb));
            var selected = !Lyrics.BackgroundColorAuto
                && Lyrics.BackgroundColorArgb == Lyrics.BackgroundCustomColorArgb;
            custom.BorderBrush = new SolidColorBrush(selected
                ? Color.FromArgb(0xFF, 0x00, 0xD9, 0xA0)
                : Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
            custom.BorderThickness = new Thickness(selected ? 2 : 1);
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
            LyricsBackgroundCombo.SelectedIndex = Lyrics.BackgroundMode switch
            {
                LyricsBackgroundMode.Mica when MainWindow.IsMicaSupported => 1,
                LyricsBackgroundMode.CustomImage => 2,
                LyricsBackgroundMode.SolidColor => 3,
                _ => 0,
            };
            var customBackground = Lyrics.BackgroundMode == LyricsBackgroundMode.CustomImage;
            BackgroundImagePanel.Visibility = customBackground ? Visibility.Visible : Visibility.Collapsed;
            BackgroundImageName.Text = Lyrics.BackgroundImageFile.Length == 0 ? "未选择" : Lyrics.BackgroundImageFile;
            PickBackgroundImageButton.Content = Lyrics.BackgroundImageFile.Length == 0 ? "选择图片…" : "更换图片…";
            BackgroundDimSlider.Value = Math.Round(Lyrics.BackgroundImageDim * 100);
            BackgroundDimValue.Text = $"{(int)Math.Round(Lyrics.BackgroundImageDim * 100)}%";
            BackgroundBlurSlider.Value = Lyrics.BackgroundImageBlur;
            BackgroundBlurValue.Text = ((int)Math.Round(Lyrics.BackgroundImageBlur)).ToString();
            ReCropBackgroundButton.IsEnabled = Lyrics.BackgroundImageFile.Length > 0;
            var solidBackground = Lyrics.BackgroundMode == LyricsBackgroundMode.SolidColor;
            SolidColorPanel.Visibility = solidBackground ? Visibility.Visible : Visibility.Collapsed;
            SolidColorAutoToggle.IsOn = Lyrics.BackgroundColorAuto;
            var displayColor = SolidDisplayColor;
            SolidColorHex.Text = Lyrics.BackgroundColorAuto
                ? $"跟随封面 · #{displayColor.R:X2}{displayColor.G:X2}{displayColor.B:X2}"
                : $"#{displayColor.R:X2}{displayColor.G:X2}{displayColor.B:X2}";
            UpdateSolidColorSelection();
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

    private async void OnLyricsBackgroundSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_syncingAppearance || !_appearanceOpen) return;

        var mode = LyricsBackgroundCombo.SelectedIndex switch
        {
            1 => LyricsBackgroundMode.Mica,
            2 => LyricsBackgroundMode.CustomImage,
            3 => LyricsBackgroundMode.SolidColor,
            _ => LyricsBackgroundMode.CoverBlur,
        };
        Lyrics.SetBackgroundMode(mode);

        // 第一次选「自定义图片」时直接弹出选图；取消且没有旧图就退回封面模糊。
        if (mode == LyricsBackgroundMode.CustomImage && Lyrics.BackgroundImageFile.Length == 0)
        {
            var picked = await PickBackgroundImageAsync();
            if (!picked && Lyrics.BackgroundImageFile.Length == 0)
            {
                Lyrics.SetBackgroundMode(LyricsBackgroundMode.CoverBlur);
            }
        }

        if (mode == LyricsBackgroundMode.SolidColor && Lyrics.BackgroundColorAuto)
        {
            _ = RefreshSolidColorAsync();
        }
    }

    private async void OnPickBackgroundImageClick(object sender, RoutedEventArgs args)
    {
        if (!_appearanceOpen) return;
        await PickBackgroundImageAsync();
    }

    private void OnSolidColorAutoToggled(object sender, RoutedEventArgs args)
    {
        if (_syncingAppearance || !_appearanceOpen) return;
        Lyrics.SetBackgroundColorAuto(SolidColorAutoToggle.IsOn);
    }

    private async Task OpenSolidColorDialogAsync()
    {
        if (!_appearanceOpen) return;
        // 编辑的是「保存的自定义色」，不是当前生效的预设色；
        // 否则每次打开都会被最近切换的颜色覆盖掉。
        var color = await ShowBackgroundColorDialogAsync(ToColor(Lyrics.BackgroundCustomColorArgb));
        if (color is { } argb)
        {
            Lyrics.SetBackgroundCustomColor(argb);
        }
    }

    /// <summary>
    /// 纯色背景自动取色：与氛围色同一条取色管线，再把主色压成暗色背景。
    /// </summary>
    private async Task RefreshSolidColorAsync()
    {
        if (Lyrics.BackgroundMode != LyricsBackgroundMode.SolidColor || !Lyrics.BackgroundColorAuto)
        {
            return;
        }

        var request = ++_autoColorRequest;
        var trackId = Player.CurrentTrackId ?? 0;
        var cover = Player.CurrentCoverUri;
        var palette = await _paletteLoader.LoadAsync(trackId, cover);

        if (request != _autoColorRequest
            || Lyrics.BackgroundMode != LyricsBackgroundMode.SolidColor
            || !Lyrics.BackgroundColorAuto)
        {
            return;
        }

        var seed = palette.Count > 1 ? palette[1] : palette.Count > 0 ? palette[0] : default;
        var dark = ColorPalette.DarkBackground(seed);
        _autoSolidColor = Color.FromArgb(255, dark.R, dark.G, dark.B);
        ApplyBackgroundMode();
        if (_appearanceOpen) SyncAppearanceControls();
    }

    /// <summary>纯色取色弹窗：预设色板放在面板里，这里只负责自定义输入与预览。</summary>
    private async Task<uint?> ShowBackgroundColorDialogAsync(Color current)
    {
        var (hue, saturation, value) = ToHsv(current);

        const double wheelSize = 170;
        var wheelImage = new Image
        {
            Source = CreateColorWheelBitmap(256),
            Width = wheelSize,
            Height = wheelSize,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false,
        };
        var marker = new Ellipse
        {
            Width = 14,
            Height = 14,
            Stroke = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)),
            StrokeThickness = 2,
            IsHitTestVisible = false,
        };
        var wheel = new Canvas
        {
            Width = wheelSize,
            Height = wheelSize,
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
        };
        wheel.Children.Add(wheelImage);
        wheel.Children.Add(marker);

        var lyricPreview = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 10),
            Background = new SolidColorBrush(current),
        };
        var previewLines = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        previewLines.Children.Add(new TextBlock
        {
            Text = "蓝色的身影在街角摇曳",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ToColor(Lyrics.HighlightColorArgb)),
        });
        previewLines.Children.Add(new TextBlock
        {
            Text = "青い影が揺れる街角",
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
        });
        lyricPreview.Child = previewLines;

        var swatch = new Border
        {
            Width = 56,
            Height = 34,
            CornerRadius = new CornerRadius(6),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0x80, 0x80, 0x80)),
            BorderThickness = new Thickness(1),
        };
        var input = new TextBox
        {
            PlaceholderText = "#RRGGBB、#RGB、#AARRGGBB 或 rgb(12, 34, 56)",
            VerticalAlignment = VerticalAlignment.Center,
        };
        var valueSlider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            StepFrequency = 1,
            Width = 170,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var hint = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromArgb(0xB3, 0x80, 0x80, 0x80)),
        };
        var error = new TextBlock
        {
            FontSize = 12,
            Text = "颜色格式无法识别",
            Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x6B, 0x6B)),
            Visibility = Visibility.Collapsed,
        };

        var valueRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        valueRow.Children.Add(new TextBlock { Text = "明度", VerticalAlignment = VerticalAlignment.Center });
        valueRow.Children.Add(valueSlider);

        var inputRow = new Grid { ColumnSpacing = 8 };
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inputRow.Children.Add(input);
        Grid.SetColumn(swatch, 1);
        inputRow.Children.Add(swatch);

        var pickerRow = new Grid { ColumnSpacing = 12 };
        pickerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pickerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        lyricPreview.Height = wheelSize;
        pickerRow.Children.Add(lyricPreview);
        Grid.SetColumn(wheel, 1);
        pickerRow.Children.Add(wheel);

        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(pickerRow);
        content.Children.Add(valueRow);
        content.Children.Add(inputRow);
        content.Children.Add(error);
        content.Children.Add(hint);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "背景颜色",
            Content = content,
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = _themeRoot?.ActualTheme ?? ElementTheme.Default,
        };
        if (Application.Current.Resources.TryGetValue("BowenAcrylicBrush", out var brush) && brush is Brush acrylic)
        {
            dialog.Resources["ContentDialogBackground"] = acrylic;
        }

        var updating = false;
        uint? result = null;

        void UpdateMarker()
        {
            var radius = wheelSize / 2;
            var angle = hue * Math.PI / 180;
            var x = radius + (Math.Cos(angle) * saturation * radius);
            var y = radius + (Math.Sin(angle) * saturation * radius);
            Canvas.SetLeft(marker, x - (marker.Width / 2));
            Canvas.SetTop(marker, y - (marker.Height / 2));
        }

        void ApplyColor(Color color)
        {
            lyricPreview.Background = new SolidColorBrush(color);
            swatch.Background = new SolidColorBrush(color);
            hint.Text = RelativeLuminance(color) > 0.45
                ? "颜色偏亮，白色歌词可能不够清晰"
                : "对比度良好";
            error.Visibility = Visibility.Collapsed;
            dialog.IsPrimaryButtonEnabled = true;
            result = 0xFF000000u | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
        }

        void UpdateFromHsv()
        {
            var color = FromHsv(hue, saturation, value);
            updating = true;
            input.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            valueSlider.Value = value * 100;
            updating = false;

            UpdateMarker();
            ApplyColor(color);
        }

        void PickFromWheel(Point point)
        {
            var radius = wheelSize / 2;
            var dx = point.X - radius;
            var dy = point.Y - radius;
            var distance = Math.Sqrt((dx * dx) + (dy * dy));
            hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
            saturation = Math.Clamp(distance / radius, 0, 1);
            UpdateFromHsv();
        }

        var dragging = false;
        wheel.PointerPressed += (_, e) =>
        {
            dragging = wheel.CapturePointer(e.Pointer);
            PickFromWheel(e.GetCurrentPoint(wheel).Position);
            e.Handled = true;
        };
        wheel.PointerMoved += (_, e) =>
        {
            if (!dragging) return;
            PickFromWheel(e.GetCurrentPoint(wheel).Position);
            e.Handled = true;
        };
        wheel.PointerReleased += (_, e) =>
        {
            if (!dragging) return;
            wheel.ReleasePointerCapture(e.Pointer);
            dragging = false;
        };
        wheel.PointerCanceled += (_, e) =>
        {
            if (!dragging) return;
            wheel.ReleasePointerCapture(e.Pointer);
            dragging = false;
        };

        valueSlider.ValueChanged += (_, e) =>
        {
            if (updating) return;
            value = e.NewValue / 100;
            UpdateFromHsv();
        };

        input.TextChanged += (_, _) =>
        {
            if (updating) return;
            if (!TryParseBackgroundColor(input.Text, out var color))
            {
                error.Visibility = Visibility.Visible;
                dialog.IsPrimaryButtonEnabled = false;
                result = null;
                return;
            }

            (hue, saturation, value) = ToHsv(color);
            updating = true;
            valueSlider.Value = value * 100;
            updating = false;
            UpdateMarker();
            ApplyColor(color);
        };

        UpdateFromHsv();

        var response = await dialog.ShowAsync();
        // 色盘位图与预览色都是不小的对象，关掉后立即断开引用。
        wheelImage.Source = null;
        wheel.Children.Clear();
        swatch.Background = null;
        lyricPreview.Background = null;
        lyricPreview.Child = null;
        return response == ContentDialogResult.Primary ? result : null;
    }

    /// <summary>生成 HSV 色盘：角度是色相，半径是饱和度，明度固定为 1。</summary>
    private static WriteableBitmap CreateColorWheelBitmap(int size)
    {
        var bitmap = new WriteableBitmap(size, size);
        var pixels = new byte[size * size * 4];
        var radius = size / 2.0;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = x + 0.5 - radius;
                var dy = y + 0.5 - radius;
                var distance = Math.Sqrt((dx * dx) + (dy * dy));
                var index = ((y * size) + x) * 4;

                if (distance > radius)
                {
                    pixels[index + 3] = 0;
                    continue;
                }

                var color = FromHsv(
                    (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360,
                    Math.Clamp(distance / radius, 0, 1),
                    1);
                pixels[index + 0] = color.B;
                pixels[index + 1] = color.G;
                pixels[index + 2] = color.R;
                pixels[index + 3] = 255;
            }
        }

        using (var stream = bitmap.PixelBuffer.AsStream())
        {
            stream.Write(pixels, 0, pixels.Length);
        }

        bitmap.Invalidate();
        return bitmap;
    }

    private static (double Hue, double Saturation, double Value) ToHsv(Color color)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        var hue = 0.0;

        if (delta > 0)
        {
            if (max == r) hue = 60 * (((g - b) / delta) % 6);
            else if (max == g) hue = 60 * (((b - r) / delta) + 2);
            else hue = 60 * (((r - g) / delta) + 4);
        }

        if (hue < 0) hue += 360;

        return (hue, max <= 0 ? 0 : delta / max, max);
    }

    private static Color FromHsv(double hue, double saturation, double value)
    {
        var chroma = value * saturation;
        var section = hue / 60;
        var x = chroma * (1 - Math.Abs((section % 2) - 1));
        var m = value - chroma;

        var (r, g, b) = section switch
        {
            < 1 => (chroma, x, 0.0),
            < 2 => (x, chroma, 0.0),
            < 3 => (0.0, chroma, x),
            < 4 => (0.0, x, chroma),
            < 5 => (x, 0.0, chroma),
            _ => (chroma, 0.0, x),
        };

        return Color.FromArgb(
            255,
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    /// <summary>接受 #RGB / #RRGGBB / #AARRGGBB，以及 rgb(r,g,b) 或 r,g,b。</summary>
    private static bool TryParseBackgroundColor(string? text, out Color color)
    {
        color = default;
        text = text?.Trim();
        if (string.IsNullOrEmpty(text)) return false;

        if (text[0] == '#')
        {
            var hex = text[1..];
            if (hex.Length == 3)
            {
                hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
            }

            if (hex.Length == 6
                && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
            {
                color = Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
                return true;
            }

            if (hex.Length == 8
                && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var argb))
            {
                color = Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
                return true;
            }

            return false;
        }

        var body = text;
        if (body.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase) && body.EndsWith(')'))
        {
            body = body[4..^1];
        }

        var parts = body.Split([',', ' ', ';', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3
            || !byte.TryParse(parts[0], out var r)
            || !byte.TryParse(parts[1], out var g)
            || !byte.TryParse(parts[2], out var b))
        {
            return false;
        }

        color = Color.FromArgb(255, r, g, b);
        return true;
    }

    private static double RelativeLuminance(Color color)
        => ((0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B)) / 255;

    private async Task<bool> PickBackgroundImageAsync()
    {
        try
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".bmp");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _windowHandle);

            var file = await picker.PickSingleFileAsync();
            if (file is null) return false;

            var fileName = $"lyrics-bg-{Guid.NewGuid():N}.png";
            var destination = AppPaths.LyricsBackgroundFile(fileName);
            Directory.CreateDirectory(AppPaths.BackgroundsDirectory);

            // 先按 EXIF 摆正并缩到最长边 1920，再编码成 PNG。
            // 预览用 BitmapImage（会应用 EXIF），渲染用 CanvasBitmap（不应用）；
            // 不统一的话手机会出现「预览正的、最终歪的」。
            await WriteUprightBackgroundAsync(file, destination);

            var dim = await EstimateBackgroundDimAsync(file);
            var crop = await ShowBackgroundCropDialogAsync(destination, 0.5, 0.5, 1);
            if (crop is not { } value)
            {
                // 取消裁剪就不使用这张图，别在背景目录里留下孤儿文件。
                try { File.Delete(destination); } catch (IOException) { }
                return false;
            }

            var previous = Lyrics.BackgroundImageFile;
            Lyrics.SetBackgroundImage(fileName, dim, value.FocusX, value.FocusY, value.Zoom);

            if (previous.Length > 0 && !string.Equals(previous, fileName, StringComparison.Ordinal))
            {
                var oldPath = AppPaths.LyricsBackgroundFile(previous);
                try
                {
                    if (File.Exists(oldPath)) File.Delete(oldPath);
                }
                catch (IOException)
                {
                    // 旧图删不掉只留下一个文件，不影响本次选择。
                }
            }

            return true;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "选择歌词背景图片失败");
            return false;
        }
    }

    /// <summary>
    /// 把选中的图片按 EXIF 摆正、缩到最长边 1920，再编码成 PNG。
    /// </summary>
    /// <remarks>
    /// 预览用 <c>BitmapImage</c>，它会应用 EXIF 方向；最终渲染用 <c>CanvasBitmap</c>，不会。
    /// 直接在导入时摆正并重新编码，两边读的是同一份像素，裁剪预览和最终背景才不会对不上。
    /// </remarks>
    private static async Task WriteUprightBackgroundAsync(StorageFile source, string destination)
    {
        using var input = await source.OpenReadAsync();
        var decoder = await BitmapDecoder.CreateAsync(input);

        var longest = Math.Max(decoder.PixelWidth, decoder.PixelHeight);
        var scale = longest > 1920 ? 1920.0 / longest : 1;
        var transform = new BitmapTransform
        {
            ScaledWidth = (uint)Math.Max(1, Math.Round(decoder.PixelWidth * scale)),
            ScaledHeight = (uint)Math.Max(1, Math.Round(decoder.PixelHeight * scale)),
        };

        using var bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            transform,
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.ColorManageToSRgb);

        using var output = File.Create(destination);
        using var stream = output.AsRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync();
    }

    /// <summary>
    /// 按图片平均亮度给一个初始暗度：把背景压到约 22% 亮度，亮图多压、暗图少压。
    /// </summary>
    private static async Task<double> EstimateBackgroundDimAsync(StorageFile file)
    {
        try
        {
            using var stream = await file.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var data = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Ignore,
                new BitmapTransform { ScaledWidth = 32, ScaledHeight = 32 },
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage);

            var pixels = data.DetachPixelData();
            if (pixels.Length < 4) return LyricsSettings.DefaultBackgroundDim;

            double sum = 0;
            for (var i = 0; i + 3 < pixels.Length; i += 4)
            {
                sum += (0.0722 * pixels[i]) + (0.7152 * pixels[i + 1]) + (0.2126 * pixels[i + 2]);
            }

            var luminance = sum / (pixels.Length / 4.0) / 255;
            var dim = luminance <= 0.22 ? 0.35 : 1 - (0.22 / luminance);
            return Math.Round(Math.Clamp(dim, 0.35, LyricsSettings.MaximumBackgroundDim), 2);
        }
        catch
        {
            return LyricsSettings.DefaultBackgroundDim;
        }
    }

    private void OnBackgroundDimChanged(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (_syncingAppearance || !_appearanceOpen) return;
        BackgroundDimValue.Text = $"{(int)Math.Round(args.NewValue)}%";
        Lyrics.SetBackgroundImageDim(args.NewValue / 100);
    }

    private void OnBackgroundBlurChanged(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (_syncingAppearance || !_appearanceOpen) return;
        BackgroundBlurValue.Text = ((int)Math.Round(args.NewValue)).ToString();
        Lyrics.SetBackgroundImageBlur(args.NewValue);
    }

    private void OnResetBackgroundDimClick(object sender, RoutedEventArgs args)
    {
        if (!_appearanceOpen) return;
        Lyrics.SetBackgroundImageDim(LyricsSettings.DefaultBackgroundDim);
    }

    private void OnResetBackgroundBlurClick(object sender, RoutedEventArgs args)
    {
        if (!_appearanceOpen) return;
        Lyrics.SetBackgroundImageBlur(LyricsSettings.DefaultBackgroundBlur);
    }

    private async void OnReCropBackgroundClick(object sender, RoutedEventArgs args)
    {
        if (!_appearanceOpen || Lyrics.BackgroundImageFile.Length == 0) return;
        var path = BackgroundImagePath;
        if (!File.Exists(path)) return;

        var crop = await ShowBackgroundCropDialogAsync(
            path, Lyrics.BackgroundImageFocusX, Lyrics.BackgroundImageFocusY, Lyrics.BackgroundImageZoom);
        if (crop is { } value)
        {
            Lyrics.SetBackgroundImageCrop(value.FocusX, value.FocusY, value.Zoom);
        }
    }

    /// <summary>
    /// 裁剪弹窗：拖动图片选择保留区域，缩放滑杆进一步裁切。
    /// </summary>
    /// <returns>确定时返回裁剪中心与缩放；取消或加载失败返回 null。</returns>
    private async Task<(double FocusX, double FocusY, double Zoom)?> ShowBackgroundCropDialogAsync(
        string path, double focusX, double focusY, double zoom)
    {
        double imageWidth;
        double imageHeight;
        BitmapImage bitmap;
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var decodeWidth = Math.Min(1600, decoder.PixelWidth);
            imageWidth = decodeWidth;
            imageHeight = decoder.PixelHeight * (decodeWidth / (double)decoder.PixelWidth);
            bitmap = new BitmapImage
            {
                DecodePixelWidth = (int)decodeWidth,
                UriSource = new Uri(path, UriKind.Absolute),
            };
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "背景图片裁剪预览加载失败");
            return null;
        }

        var aspect = BackdropView.ActualWidth > 1 && BackdropView.ActualHeight > 1
            ? BackdropView.ActualHeight / BackdropView.ActualWidth
            : ActualWidth > 1 && ActualHeight > 1 ? ActualHeight / ActualWidth : 9.0 / 16;
        var previewWidth = 480.0;
        var previewHeight = previewWidth * aspect;
        if (previewHeight > 360)
        {
            previewHeight = 360;
            previewWidth = previewHeight / aspect;
        }
        else if (previewHeight < 200)
        {
            previewHeight = 200;
            previewWidth = previewHeight / aspect;
        }

        var image = new Image
        {
            Source = bitmap,
            Width = imageWidth,
            Height = imageHeight,
            Stretch = Stretch.Fill,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            RenderTransformOrigin = new Point(0, 0),
        };
        var transform = new CompositeTransform();
        image.RenderTransform = transform;

        var preview = new Canvas
        {
            Width = previewWidth,
            Height = previewHeight,
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(0xFF, 0, 0, 0)),
            Clip = new RectangleGeometry { Rect = new Rect(0, 0, previewWidth, previewHeight) },
        };
        // Canvas 不约束子元素尺寸；换成 Border 会把放大的图片压回预览大小，再缩放就只剩一小块。
        preview.Children.Add(image);

        var baseScale = Math.Max(previewWidth / imageWidth, previewHeight / imageHeight) * 1.1;
        var currentZoom = Math.Clamp(zoom, 1, 3);
        var scale = baseScale * currentZoom;
        var fx = Math.Clamp(focusX, 0, 1);
        var fy = Math.Clamp(focusY, 0, 1);
        double offsetX = 0;
        double offsetY = 0;

        void ApplyOffsets()
        {
            offsetX = Math.Clamp(offsetX, previewWidth - (imageWidth * scale), 0);
            offsetY = Math.Clamp(offsetY, previewHeight - (imageHeight * scale), 0);
            transform.ScaleX = transform.ScaleY = scale;
            transform.TranslateX = offsetX;
            transform.TranslateY = offsetY;
        }

        void CenterOnFocus()
        {
            offsetX = (previewWidth / 2) - (fx * imageWidth * scale);
            offsetY = (previewHeight / 2) - (fy * imageHeight * scale);
            ApplyOffsets();
        }

        void FocusFromOffsets()
        {
            fx = Math.Clamp((previewWidth / 2 - offsetX) / (imageWidth * scale), 0, 1);
            fy = Math.Clamp((previewHeight / 2 - offsetY) / (imageHeight * scale), 0, 1);
        }

        CenterOnFocus();

        var dragging = false;
        Point dragStart = default;
        double dragOffsetX = 0;
        double dragOffsetY = 0;
        preview.PointerPressed += (_, e) =>
        {
            dragging = preview.CapturePointer(e.Pointer);
            dragStart = e.GetCurrentPoint(preview).Position;
            dragOffsetX = offsetX;
            dragOffsetY = offsetY;
        };
        preview.PointerMoved += (_, e) =>
        {
            if (!dragging) return;
            var point = e.GetCurrentPoint(preview).Position;
            offsetX = dragOffsetX + (point.X - dragStart.X);
            offsetY = dragOffsetY + (point.Y - dragStart.Y);
            ApplyOffsets();
            FocusFromOffsets();
        };
        preview.PointerReleased += (_, e) =>
        {
            if (!dragging) return;
            preview.ReleasePointerCapture(e.Pointer);
            dragging = false;
        };
        preview.PointerCanceled += (_, e) =>
        {
            if (!dragging) return;
            preview.ReleasePointerCapture(e.Pointer);
            dragging = false;
        };

        var syncingZoom = false;
        var zoomSlider = new Slider
        {
            Minimum = 100,
            Maximum = 300,
            StepFrequency = 1,
            Value = currentZoom * 100,
            Width = 320,
            VerticalAlignment = VerticalAlignment.Center,
        };
        zoomSlider.ValueChanged += (_, e) =>
        {
            if (syncingZoom) return;
            currentZoom = Math.Clamp(e.NewValue / 100, 1, 3);
            scale = baseScale * currentZoom;
            CenterOnFocus();
        };

        void ZoomAt(double target, double anchorX, double anchorY)
        {
            target = Math.Clamp(target, 1, 3);
            if (Math.Abs(target - currentZoom) < 0.001) return;

            // 以鼠标位置为锚点：缩放前后，指针下的同一个图像点保持不动。
            var imageX = (anchorX - offsetX) / scale;
            var imageY = (anchorY - offsetY) / scale;
            currentZoom = target;
            scale = baseScale * currentZoom;
            offsetX = anchorX - (imageX * scale);
            offsetY = anchorY - (imageY * scale);
            ApplyOffsets();
            FocusFromOffsets();

            // 滚轮与滑杆共用同一份缩放值；同步滑杆时不要再次触发居中。
            syncingZoom = true;
            zoomSlider.Value = currentZoom * 100;
            syncingZoom = false;
        }

        preview.PointerWheelChanged += (_, e) =>
        {
            var point = e.GetCurrentPoint(preview);
            var delta = point.Properties.MouseWheelDelta;
            if (delta == 0) return;

            ZoomAt(currentZoom * Math.Pow(1.1, delta / 120.0), point.Position.X, point.Position.Y);
            e.Handled = true;
        };

        var hint = new TextBlock
        {
            Text = "拖动图片选择保留区域，滚轮或缩放滑杆可以进一步裁切。",
            FontSize = 12,
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var zoomRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        zoomRow.Children.Add(new TextBlock { Text = "缩放", VerticalAlignment = VerticalAlignment.Center });
        zoomRow.Children.Add(zoomSlider);

        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(preview);
        content.Children.Add(hint);
        content.Children.Add(zoomRow);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "裁剪背景图片",
            Content = content,
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            // 弹窗挂在 XamlRoot 下，不在 ShellRoot 的 RequestedTheme 子树里，必须显式跟主题。
            RequestedTheme = _themeRoot?.ActualTheme ?? ElementTheme.Default,
        };
        // ContentDialog 的模板用主题资源画底，直接设 Background 会被模板覆盖，改资源键。
        if (Application.Current.Resources.TryGetValue("BowenAcrylicBrush", out var brush) && brush is Brush acrylic)
        {
            dialog.Resources["ContentDialogBackground"] = acrylic;
        }

        var result = await dialog.ShowAsync();
        // 预览用的 BitmapImage 解码位图不小；弹窗关闭后立即断开引用，不等 GC。
        image.Source = null;
        preview.Children.Clear();
        if (result != ContentDialogResult.Primary) return null;

        FocusFromOffsets();
        return (fx, fy, currentZoom);
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

        if (args.PropertyName is nameof(LyricsViewModel.BackgroundMode)
            or nameof(LyricsViewModel.BackgroundImageFile)
            or nameof(LyricsViewModel.BackgroundImageDim)
            or nameof(LyricsViewModel.BackgroundImageBlur)
            or nameof(LyricsViewModel.BackgroundImageFocusX)
            or nameof(LyricsViewModel.BackgroundImageFocusY)
            or nameof(LyricsViewModel.BackgroundImageZoom)
            or nameof(LyricsViewModel.BackgroundColorArgb)
            or nameof(LyricsViewModel.BackgroundCustomColorArgb)
            or nameof(LyricsViewModel.BackgroundColorAuto))
        {
            if (Lyrics.BackgroundMode == LyricsBackgroundMode.SolidColor && Lyrics.BackgroundColorAuto)
            {
                _ = RefreshSolidColorAsync();
            }

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
