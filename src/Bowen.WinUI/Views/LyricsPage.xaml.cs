using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using Bowen.Core.Media;
using Bowen.Core.Models;
using Bowen.Core.Models.Lyrics;
using Bowen.Core.Services;
using Bowen.Core.Services.Abstractions;
using Bowen.WinUI.Controls;
using Bowen.WinUI.Media;
using Bowen.WinUI.Services;
using Bowen.WinUI.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI;
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
using Windows.System.Power;
using Windows.UI;
using VirtualKey = Windows.System.VirtualKey;

namespace Bowen.WinUI.Views;

public sealed partial class LyricsPage : Page, INavigationAware
{
    private void OnQualitySelected(object? sender, EventArgs e) => QualityFlyout.Hide();

    private void UpdateEffectsButton()
        => VisualStateManager.GoToState(this,
            Effects.IsEnabled ? "AudioEffectsActive" : "AudioEffectsInactive", useTransitions: false);

    private void OnEffectsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AudioEffectsViewModel.IsEnabled))
        {
            UpdateEffectsButton();
        }
    }
    private readonly MainWindow _window;
    private readonly INavigationService _navigation;
    private readonly Func<Track, MvPage> _mvFactory;

    /// <summary>底部「更多」菜单要用它现造菜单状态（与曲目行那颗同一个装配点）。</summary>
    private readonly TrackActionsService _trackActions;
    private readonly IReadOnlyList<FontOption> _fontOptions;
    private readonly List<(Border Dot, uint Color)> _colorDots = [];
    private readonly List<(Border Dot, uint Color)> _solidColorDots = [];
    private Border? _customSolidColorDot;
    private readonly List<(Border Dot, uint Color)> _mp3ShellDots = [];
    private Border? _customMp3ShellDot;
    private Color? _autoMp3ShellColor;
    private int _autoMp3ShellRequest;
    private readonly CoverPaletteLoader _paletteLoader;
    private readonly CoverPaletteLoader _mp3PaletteLoader;
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
    private bool? _coverAnchored;
    private double _coverSize;
    private Brush? _coverFrameBaseBackground;
    private readonly SolidColorBrush _mp3SectorBrush = new(Color.FromArgb(0xFF, 0x1C, 0x21, 0x26));
    private readonly SolidColorBrush _mp3SectorHoverBrush = new(Color.FromArgb(0xFF, 0x2A, 0x31, 0x38));

    /// <summary>换碟动画：上一次的封面图、当前图，以及连切时作废旧一轮的版本号。</summary>
    private ImageSource? _lastCoverImage;
    private ImageSource? _previousCoverImage;
    private int _coverSwapVersion;

    /// <summary>黑胶唱片当前的旋转角度，暂停时保留，恢复播放时从这里接着转。</summary>
    private double _vinylAngle;
    private bool _vinylSpinning;
    private long _vinylSpinStarted;
    private readonly DispatcherQueueTimer _vinylTimer;
    private readonly DispatcherQueueTimer _mp3ClockTimer;

    /// <summary>转速：20 秒一圈（18°/s）。真实黑胶 33⅓ 转/分在屏幕上看太快，这里取观感上的慢转。</summary>
    private const double VinylDegreesPerSecond = 18;

    /// <summary>唱臂播放 / 待机角度；枢轴在右上，0° 是落盘姿态，正角把臂抬到唱片上方。</summary>
    private const float TonearmPlayingAngle = 0;
    private const float TonearmRestingAngle = 60;

    /// <summary>折线唱臂：上段竖直长度、下段长度与折角（度）。上段要够长，否则像挂在枢轴下的短钩。</summary>
    private const double TonearmUpperLength = 0.20;
    private const double TonearmLowerLength = 0.13;
    private const double TonearmBendDegrees = 80;

    /// <summary>唱片外圈的细纹路数量与笔刷；集中在最外圈，中心标签占大半盘面。</summary>
    private const int VinylGrooveCount = 28;
    private readonly List<Ellipse> _vinylGrooves = [];
    private readonly SolidColorBrush _vinylGrooveBrush = new(Color.FromArgb(0x0E, 0xFF, 0xFF, 0xFF));
    private bool? _fullscreen;
    private readonly ILogger<LyricsPage> _logger;
    private readonly BackgroundImageStore _backgroundImages;
    private readonly IClipboardService _clipboard;
    private readonly ILyricPosterSettingsStore _posterSettings;
    private readonly CoverPaletteLoader _posterPaletteLoader;
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
    private bool _shareFlyoutOpen;
    private FrameworkElement? _progressThumb;
    private Rectangle? _progressTrack;
    private Rectangle? _progressFill;
    private bool _progressPointerOver;
    private double _progressPointerX;

    public LyricsPage(MainWindow window, LyricsCanvasView canvas, AudioSpectrumView spectrum, PlayerViewModel player,
        LyricsViewModel lyrics, SongCommentsViewModel comments, AudioEffectsViewModel effects,
        INavigationService navigation, Func<Track, MvPage> mvFactory, TrackActionsService trackActions,
        FontSettingsViewModel font, BackgroundImageStore backgroundImages, IClipboardService clipboard,
        ILyricPosterSettingsStore posterSettings)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(spectrum);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(lyrics);
        ArgumentNullException.ThrowIfNull(effects);
        ArgumentNullException.ThrowIfNull(comments);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(mvFactory);
        ArgumentNullException.ThrowIfNull(trackActions);
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(backgroundImages);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(posterSettings);
        _window = window;
        _navigation = navigation;
        _mvFactory = mvFactory;
        _trackActions = trackActions;
        _logger = (Application.Current.Resources["BowenLoggerFactory"] as ILoggerFactory
            ?? NullLoggerFactory.Instance).CreateLogger<LyricsPage>();
        _backgroundImages = backgroundImages;
        _clipboard = clipboard;
        _posterSettings = posterSettings;
        _canvas = canvas;
        _spectrum = spectrum;
        Player = player;
        Lyrics = lyrics;
        Comments = comments;
        Effects = effects;
        InitializeComponent();
        // 随身听模式下要清掉封面槽底色，切回其它样式时得把它还原。
        _coverFrameBaseBackground = CoverFrame.Background;
        // 纹路批量生成：28 道细圈挤在最外圈，手写 XAML 既冗长又改不动间距。
        for (var index = 0; index < VinylGrooveCount; index++)
        {
            var groove = new Ellipse
            {
                Stroke = _vinylGrooveBrush,
                StrokeThickness = 1,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
            };
            VinylDisc.Children.Add(groove);
            _vinylGrooves.Add(groove);
        }
        Effects.PropertyChanged += OnEffectsPropertyChanged;
        UpdateEffectsButton();
        _fontOptions = BuildFontOptions(font);
        LyricsFontPicker.ItemsSource = _fontOptions;
        LyricsFontPicker.StretchToAvailableWidth();
        LyricsFontPicker.RegisterPropertyChangedCallback(
            FontPicker.SelectedIndexProperty, OnLyricsFontPickerSelectionChanged);
        BuildColorPalette();
        BuildSolidColorPalette();
        BuildMp3ShellPalette();
        _paletteLoader = new CoverPaletteLoader(
            (Application.Current.Resources["BowenLoggerFactory"] as ILoggerFactory)?.CreateLogger<CoverPaletteLoader>());
        // 外壳自动取色单独一份：与纯色背景同时开启时，两边的请求不会互相取消。
        _mp3PaletteLoader = new CoverPaletteLoader(
            (Application.Current.Resources["BowenLoggerFactory"] as ILoggerFactory)?.CreateLogger<CoverPaletteLoader>());
        // 海报单独一份取色器：对话框里的请求不能取消歌词页正在进行的自动取色。
        _posterPaletteLoader = new CoverPaletteLoader(
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
        // 黑胶旋转用 60 Hz 定时器直接推角度：合成器循环动画在部分机器上会一圈后停住，
        // 定时器每帧写一个浮点，既匀速又可随时暂停、从原角度续转。
        _vinylTimer = DispatcherQueue.CreateTimer();
        _vinylTimer.Interval = TimeSpan.FromMilliseconds(16);
        _vinylTimer.Tick += OnVinylTimer;
        // 播放器屏幕的状态栏：时钟每 30 秒对齐一次，电量跟着系统走。
        _mp3ClockTimer = DispatcherQueue.CreateTimer();
        _mp3ClockTimer.Interval = TimeSpan.FromSeconds(30);
        _mp3ClockTimer.IsRepeating = true;
        _mp3ClockTimer.Tick += OnMp3ClockTick;
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
    public AudioEffectsViewModel Effects { get; }
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
        _lastCoverImage = Player.CoverImage;
        ApplyBackgroundMode();
        ApplyCoverStyle();
        if (Lyrics.BackgroundMode == LyricsBackgroundMode.SolidColor && Lyrics.BackgroundColorAuto)
        {
            _ = RefreshSolidColorAsync();
        }

        UpdatePause();
        SyncFullscreen();
        SyncTranslation();
        _pointerTimer.Start();
        UpdatePointerLocation();
        _mp3ClockTimer.Start();
        UpdateMp3StatusBar();
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
            // 封面已经落位：从 0° 开始转，入场飞行期间不会把落点带歪。
            UpdateVinylSpin();
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
        _mp3ClockTimer.Stop();
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
        _shareFlyoutOpen = false;
        CloseAppearance(restoreFocus: false);
        MoreFlyout.Hide();
        ShareFlyout.Hide();
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
        var mica = UsesMicaBackdrop && _immersiveEntranceCompleted;
        var micaTint = mica && Lyrics.MicaTintEnabled;
        var coverActive = !solid && !mica;
        var ambientActive = !solid && (!mica || micaTint);
        BackdropView.IsPaused = _canvas.IsPaused || !coverActive;
        // 过渡仅暂停绘制；真正进入后台才释放设备和纹理，避免全屏切换反复重建。
        var releaseResources = !Lyrics.IsOpen || !_windowVisible || !_window.IsWindowVisible || _window.IsMinimized;
        _canvas.IsResourceSuspended = releaseResources;
        _spectrum.IsResourceSuspended = releaseResources;
        ReflectionView.IsResourceSuspended = releaseResources;
        BackdropView.IsResourceSuspended = releaseResources || !coverActive;
        AmbientView.IsResourceSuspended = releaseResources || !ambientActive || UsesCustomImage;
        UpdateVinylSpin();
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
            : AppPaths.BackgroundFile(Lyrics.BackgroundImageFile);

    /// <summary>当前背景层的宽高比，用于裁剪预览。</summary>
    private double BackgroundAspect
        => BackdropView.ActualWidth > 1 && BackdropView.ActualHeight > 1
            ? BackdropView.ActualWidth / BackdropView.ActualHeight
            : ActualWidth > 1 && ActualHeight > 1
                ? ActualWidth / ActualHeight
                : 16.0 / 9;

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
        BackdropView.BlurDip = custom ? Lyrics.BackgroundImageBlur : Lyrics.CoverBlurAmount;
        BackdropView.ImageOpacity = custom ? 1 : 0.72;
        BackdropView.Saturation = custom ? 0.85f : 0.68f;
        BackdropView.FocusX = custom ? Lyrics.BackgroundImageFocusX : 0.5;
        BackdropView.FocusY = custom ? Lyrics.BackgroundImageFocusY : 0.5;
        BackdropView.Zoom = custom ? Lyrics.BackgroundImageZoom : 1;
        AmbientView.Visibility = (!mica && !custom && !solid) || (mica && Lyrics.MicaTintEnabled)
            ? Visibility.Visible
            : Visibility.Collapsed;
        AmbientView.Opacity = mica && Lyrics.MicaTintEnabled ? 0.9 : 0.18;
        BackgroundDimLayer.Opacity = custom
            ? Lyrics.BackgroundImageDim
            : Lyrics.BackgroundMode == LyricsBackgroundMode.CoverBlur ? Lyrics.CoverBlurDim : 0;
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
        CoverDeck.Width = CoverDeck.Height = size;
        ReflectionView.Width = size;
        ReflectionView.Height = size * 0.55;
        Canvas.SetTop(ReflectionView, size + 2);
        UpdateCoverStyleGeometry(size);
        ApplyLyricsAppearance();
        CommentsPane.Width = Math.Max(0, Math.Min(420, ActualWidth - 32));
        if (_appearanceOpen) AppearancePane.MaxHeight = Math.Max(0, ActualHeight - 178);
        UpdateChromeInsets();
        UpdateCoverScale();
        UpdateVinylSpin();
        // 首轮布局完成后 ActualWidth 才是真的，进度条要按真实宽度再补一次。
        UpdateMp3Screen();
    }

    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(PlayerViewModel.CoverImage))
        {
            // 绑定会先把新图挂到 DetailCover 上，这里留一份上一张给换碟动画的滑出阶段用。
            _previousCoverImage = _lastCoverImage;
            _lastCoverImage = Player.CoverImage;
        }

        if (args.PropertyName is nameof(PlayerViewModel.CurrentTrackId) or nameof(PlayerViewModel.CurrentCoverUri))
        {
            _ = RefreshSolidColorAsync();
            _ = RefreshMp3ShellColorAsync();
        }

        if (args.PropertyName is nameof(PlayerViewModel.IsPlaying) or nameof(PlayerViewModel.IsSeekPending))
        {
            UpdateCoverScale();
            UpdateVinylSpin();
        }
        if (args.PropertyName is nameof(PlayerViewModel.Title) or nameof(PlayerViewModel.ArtistText)
            or nameof(PlayerViewModel.CurrentTrackId) or nameof(PlayerViewModel.PositionSeconds)
            or nameof(PlayerViewModel.DurationSeconds) or nameof(PlayerViewModel.Volume)
            or nameof(PlayerViewModel.NextTrackTitle))
        {
            UpdateMp3Screen();
        }
        if (args.PropertyName == nameof(PlayerViewModel.CurrentTrackId))
        {
            ShowChrome();

            // 菜单里那一批动作要跟着曲目换。顺手把菜单收起来 ——
            // 留着它就会出现「菜单里的歌和正在播的歌对不上」。
            MoreFlyout.Hide();
            RebuildTrackMenu();

            if (Player.CurrentTrackId is > 0) _ = Comments.LoadBadgeAsync(Player.CurrentTrackId.Value);
            else Comments.CancelBadgeRequest();
            PlayCoverSwap();
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
        var size = CoverDeck.Width;
        // 黑胶与随身听都是「一台摆在桌上的机器」，暂停时不缩；只有方形封面会轻微回缩。
        var anchored = Lyrics.CoverStyle is LyricsCoverStyle.Vinyl or LyricsCoverStyle.Mp3;
        if (_coverPlaying == Player.IsPlaying && _coverSize == size && _coverAnchored == anchored) return;
        _coverPlaying = Player.IsPlaying;
        _coverSize = size;
        _coverAnchored = anchored;
        var visual = ElementCompositionPreview.GetElementVisual(CoverDeck);
        visual.CenterPoint = new Vector3((float)(size / 2), (float)(size / 2), 0);
        using var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
        var scale = anchored || Player.IsPlaying ? 1 : 0.94f;
        animation.InsertKeyFrame(1, new Vector3(scale, scale, 1));
        animation.Duration = TimeSpan.FromMilliseconds(400);
        visual.StartAnimation("Scale", animation);
    }

    /// <summary>把播放状态写进随身听的屏幕、进度条与音量轮。</summary>
    private void UpdateMp3Screen()
    {
        if (Lyrics.CoverStyle != LyricsCoverStyle.Mp3) return;

        Mp3TitleText.Text = Player.Title;
        Mp3ArtistText.Text = Player.ArtistText;

        var next = Player.NextTrackTitle;
        Mp3NextText.Text = string.IsNullOrEmpty(next) ? "" : $"下一首 · {next}";
        Mp3NextText.Visibility = string.IsNullOrEmpty(next) ? Visibility.Collapsed : Visibility.Visible;

        var ratio = Math.Clamp(Player.PositionSeconds / Math.Max(1, Player.ProgressMaximum), 0, 1);
        // 首轮布局前 ActualWidth 还是 0；屏幕内框宽 164，按它先画一版。
        var width = Mp3ProgressTrack.ActualWidth;
        if (!double.IsFinite(width) || width <= 0) width = 164;
        Mp3ProgressFill.Width = Math.Round(width * ratio, 2);

        Mp3ElapsedText.Text = Formats.Duration(TimeSpan.FromSeconds(Math.Max(0, Player.PositionSeconds)));
        Mp3DurationText.Text = Formats.Duration(TimeSpan.FromSeconds(Math.Max(0, Player.DurationSeconds)));
    }

    private void OnMp3ClockTick(DispatcherQueueTimer sender, object args) => UpdateMp3StatusBar();

    /// <summary>状态栏的时钟与电量；没有电池的机器按满格显示。</summary>
    private void UpdateMp3StatusBar()
    {
        Mp3ClockText.Text = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);

        var percent = 100;
        try
        {
            percent = Math.Clamp(PowerManager.RemainingChargePercent, 0, 100);
        }
        catch
        {
            // 取不到电池信息时保持满格，不要留一个空图标。
        }

        // 外框内宽 13，左右各留 1px，填充最大 11。
        Mp3BatteryFill.Width = Math.Max(1, 11 * percent / 100.0);
        Mp3BatteryText.Text = $"{percent}%";
    }

    /// <summary>方向键上/下调音量，步长与播放条一致。</summary>
    private void OnMp3VolumeUpClick(object sender, RoutedEventArgs e)
        => Player.Volume = Math.Clamp(Player.Volume + 5, 0, 100);

    private void OnMp3VolumeDownClick(object sender, RoutedEventArgs e)
        => Player.Volume = Math.Clamp(Player.Volume - 5, 0, 100);

    /// <summary>扇形键的悬停反馈：只换扇形填充，不加描边，避免破坏四等分的分割线。</summary>
    private void OnMp3SectorEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button { Content: Microsoft.UI.Xaml.Shapes.Path path }) path.Fill = _mp3SectorHoverBrush;
    }

    private void OnMp3SectorExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button { Content: Microsoft.UI.Xaml.Shapes.Path path }) path.Fill = _mp3SectorBrush;
    }

    /// <summary>切封面样式：方形卡片、黑胶唱片与随身听三者互斥，共用同一个 DetailCover。</summary>
    /// <remarks>
    /// 不换图片实例、不隐藏 DetailCover —— 共享封面动画靠这个元素测量起点与落点。
    /// 黑胶模式下它缩成中心标签，飞入 / 飞出的圆角由 CoverFrame 的 CornerRadius 提供。
    /// </remarks>
    private void ApplyCoverStyle()
    {
        var vinyl = Lyrics.CoverStyle == LyricsCoverStyle.Vinyl;
        var mp3 = Lyrics.CoverStyle == LyricsCoverStyle.Mp3;
        // 切样式时把换碟动画收干净：可能停在滑出 / 滑入的中途。
        _coverSwapVersion++;
        CoverSwapLabel.Visibility = Visibility.Collapsed;
        CoverSwapLabelImage.Source = null;
        AppMotion.Reset(VinylSpinLayer);
        VinylPlatter.Visibility = vinyl ? Visibility.Visible : Visibility.Collapsed;
        VinylDisc.Visibility = vinyl ? Visibility.Visible : Visibility.Collapsed;
        VinylSpindle.Visibility = vinyl ? Visibility.Visible : Visibility.Collapsed;
        TonearmHost.Visibility = vinyl ? Visibility.Visible : Visibility.Collapsed;
        ReflectionView.Visibility = vinyl || mp3 ? Visibility.Collapsed : Visibility.Visible;
        Mp3DeviceBack.Visibility = mp3 ? Visibility.Visible : Visibility.Collapsed;
        ApplyMp3ShellColor();
        if (mp3 && Lyrics.Mp3ShellColorAuto) _ = RefreshMp3ShellColorAsync();
        // 随身听的专辑图是屏幕里的缩略图，封面槽底色会从机身周围透出来，必须清掉。
        if (!_entranceActive) CoverFrame.Background = mp3 ? null : _coverFrameBaseBackground;

        UpdateCoverStyleGeometry(CoverDeck.Width);

        // 每次应用样式（含进入歌词页）都把角度归零：入场飞行期间封面是正的，
        // 落位后再接着转；方形封面本来就不转，清零同样无害。
        _vinylSpinning = false;
        _vinylTimer.Stop();
        _vinylAngle = 0;
        ElementCompositionPreview.GetElementVisual(VinylSpinLayer).RotationAngleInDegrees = 0;

        UpdateTonearm(animate: false);
        // 未挂到树上时先不碰封面缩放：缓存会记住「已经缩过」，动画却可能丢；
        // Loaded 时的 UpdateLayoutSizing 会按真实尺寸补一次。
        if (IsLoaded) UpdateCoverScale();
        UpdateVinylSpin();
        UpdateMp3Screen();
    }

    /// <summary>按封面槽尺寸重算黑胶的圆盘、中心标签与唱臂几何；方形模式只把封面还原成铺满。</summary>
    private void UpdateCoverStyleGeometry(double size)
    {
        if (!double.IsFinite(size) || size <= 0) return;
        VinylClipHost.Width = VinylClipHost.Height = size;
        VinylSpinLayer.Width = VinylSpinLayer.Height = size;
        TonearmHost.Width = TonearmHost.Height = size;

        // 机身按 320 设计稿画，整层等比缩放到封面槽；屏幕缩略图的分数坐标与它对得上。
        var mp3Scale = size / 320.0;
        Mp3DeviceBack.RenderTransform = new ScaleTransform { ScaleX = mp3Scale, ScaleY = mp3Scale };

        var vinyl = Lyrics.CoverStyle == LyricsCoverStyle.Vinyl;
        var mp3 = Lyrics.CoverStyle == LyricsCoverStyle.Mp3;
        // 放台比唱片大一圈；唱片再比封面槽小一圈，右上角留出枢轴，唱头才能既落在盘缘、又摆出盘外。
        var platter = Math.Round(size * 0.94);
        VinylPlatter.Width = VinylPlatter.Height = platter;
        Canvas.SetLeft(VinylPlatter, (size - platter) / 2);
        Canvas.SetTop(VinylPlatter, (size - platter) / 2);
        var disc = Math.Round(size * 0.88);
        VinylDiscBase.Width = VinylDiscBase.Height = disc;
        // 三种模式共用 CoverFrame：方形铺满封面槽，黑胶缩成中心标签，
        // 播放器缩进屏幕左侧的 60×60 图位（320 槽位），位置与 Mp3DeviceBack 的屏幕几何对齐。
        double label;
        CornerRadius frameRadius;
        if (mp3)
        {
            label = Math.Round(size * 0.1875);
            frameRadius = new CornerRadius(Math.Round(label * 0.11));
        }
        else if (vinyl)
        {
            label = Math.Round(disc * 0.56);
            frameRadius = new CornerRadius(label / 2);
        }
        else
        {
            label = size;
            frameRadius = new CornerRadius(16);
        }

        CoverFrame.Width = CoverFrame.Height = label;
        CoverFrame.CornerRadius = frameRadius;
        CoverSwapLabel.Width = CoverSwapLabel.Height = label;
        CoverSwapLabel.CornerRadius = frameRadius;

        // CoverFrame 是 Grid 子元素：播放器要挪到屏幕左侧图位，其余两种必须回正居中。
        if (mp3)
        {
            CoverFrame.HorizontalAlignment = HorizontalAlignment.Left;
            CoverFrame.VerticalAlignment = VerticalAlignment.Top;
            CoverFrame.Margin = new Thickness(
                Math.Round(size * 0.25625), Math.Round(size * 0.175), 0, 0);
        }
        else
        {
            CoverFrame.HorizontalAlignment = HorizontalAlignment.Center;
            CoverFrame.VerticalAlignment = VerticalAlignment.Center;
            CoverFrame.Margin = new Thickness(0);
        }

        for (var index = 0; index < _vinylGrooves.Count; index++)
        {
            // 纹路压在 0.60–0.985 盘径的最外圈，彼此只差 1% 盘径，看起来是一圈细密纹理。
            var fraction = 0.6 + 0.385 * index / (_vinylGrooves.Count - 1);
            _vinylGrooves[index].Width = _vinylGrooves[index].Height = Math.Round(disc * fraction);
        }

        VinylDiscRim.Width = VinylDiscRim.Height = Math.Max(0, disc - 2);
        VinylSpindle.Width = VinylSpindle.Height = Math.Max(5, Math.Round(size * 0.028));

        // 折线唱臂：枢轴在盘外右上角，上段竖直向下，折点后下段朝左下搭到盘缘。
        // 0° 时唱头落在最外圈纹路，待机时整根臂绕枢轴抬起、摆到唱片外。
        var pivotX = size * 0.9;
        var pivotY = size * 0.03;
        var pivotLocalX = size * 0.24;
        var pivotLocalY = size * 0.03;
        var rotorWidth = size * 0.3;
        var rotorHeight = size * 0.3;
        TonearmRotor.Width = rotorWidth;
        TonearmRotor.Height = rotorHeight;
        Canvas.SetLeft(TonearmRotor, pivotX - pivotLocalX);
        Canvas.SetTop(TonearmRotor, pivotY - pivotLocalY);

        var armWidth = Math.Max(3, Math.Round(size * 0.012));
        var upper = size * TonearmUpperLength;
        var lower = size * TonearmLowerLength;
        var bend = TonearmBendDegrees * Math.PI / 180;
        var bendX = pivotLocalX;
        var bendY = pivotLocalY + upper;
        var endX = bendX - lower * Math.Sin(bend);
        var endY = bendY + lower * Math.Cos(bend);

        // 折点用二次贝塞尔过渡：先把直杆画到折点前 curve 处，再弯过折点接到下段。
        var curve = Math.Min(upper, lower) * 0.28;

        // 视觉臂杆和命中区各要一份几何：WinUI 的 Geometry 不能同时挂到两个 Path 上。
        PathFigure CreateFigure()
        {
            var figure = new PathFigure
            {
                StartPoint = new Point(pivotLocalX, pivotLocalY),
                IsClosed = false,
                IsFilled = false,
            };
            figure.Segments.Add(new LineSegment { Point = new Point(bendX, bendY - curve) });
            figure.Segments.Add(new QuadraticBezierSegment
            {
                Point1 = new Point(bendX, bendY),
                Point2 = new Point(bendX - curve * Math.Sin(bend), bendY + curve * Math.Cos(bend)),
            });
            figure.Segments.Add(new LineSegment { Point = new Point(endX, endY) });
            return figure;
        }

        TonearmRod.Data = new PathGeometry { Figures = { CreateFigure() } };
        TonearmRod.StrokeThickness = armWidth;
        // 命中区只把线加粗；杆宽只有几像素，直接点很难命中。
        TonearmHitPath.Data = new PathGeometry { Figures = { CreateFigure() } };
        TonearmHitPath.StrokeThickness = Math.Max(24, size * 0.08);
        TonearmHeadTilt.Angle = TonearmBendDegrees;

        TonearmHead.Width = Math.Max(8, size * 0.045);
        TonearmHead.Height = Math.Max(10, size * 0.05);
        TonearmHead.RadiusX = TonearmHead.RadiusY = Math.Max(3, size * 0.012);
        Canvas.SetLeft(TonearmHead, endX - TonearmHead.Width / 2);
        Canvas.SetTop(TonearmHead, endY - TonearmHead.Height / 2);

        TonearmWeight.Width = Math.Max(8, size * 0.035);
        TonearmWeight.Height = Math.Max(9, size * 0.04);
        Canvas.SetLeft(TonearmWeight, pivotLocalX - TonearmWeight.Width / 2);
        Canvas.SetTop(TonearmWeight, pivotLocalY - TonearmWeight.Height * 0.9);

        TonearmPivot.Width = TonearmPivot.Height = Math.Max(8, Math.Round(size * 0.045));
        Canvas.SetLeft(TonearmPivot, pivotX - TonearmPivot.Width / 2);
        Canvas.SetTop(TonearmPivot, pivotY - TonearmPivot.Height / 2);

        // 旋转中心随尺寸变化：整张碟（盘面 + 中心封面）绕槽中心转；唱臂绕枢轴。
        ElementCompositionPreview.GetElementVisual(VinylSpinLayer).CenterPoint =
            new Vector3((float)(size / 2), (float)(size / 2), 0);
        ElementCompositionPreview.GetElementVisual(TonearmRotor).CenterPoint =
            new Vector3((float)pivotLocalX, (float)pivotLocalY, 0);
    }

    /// <summary>黑胶是否该转：只有黑胶样式、正在播放且页面可见未挂起时才转。</summary>
    /// <remarks>
    /// 停下时把当前角度记进 <see cref="_vinylAngle"/>，恢复播放从同一角度接着转，不跳回起点。
    /// 角度由定时器按「起始角度 + 已播秒数 × 转速」算出，只做 360° 取模，没有循环接缝。
    /// </remarks>
    private void UpdateVinylSpin()
    {
        // IsLoaded 必须参与判断：OnNavigatedTo 早于 Loaded，此时给未挂到树上的元素起动画可能丢失；
        // 而丢了之后状态已经记成「在转」，后续调用会提前返回。Loaded 时的布局会再进来一次。
        // 入场飞行期间不转：共享封面按中心标签的外框落位，旋转中的方形包围盒会被放大。
        // 跳转等待声音接上时也不转：这时歌词已经卡住，唱片还在转就是没和播放状态对齐。
        var shouldSpin = IsLoaded && Lyrics.CoverStyle == LyricsCoverStyle.Vinyl && !_entranceActive
            && Player.IsPlaying && !Player.IsSeekPending
            && Lyrics.IsOpen && _windowVisible && !_window.IsRenderingSuspended;
        if (shouldSpin == _vinylSpinning) return;

        var visual = ElementCompositionPreview.GetElementVisual(VinylSpinLayer);
        var half = (float)(VinylSpinLayer.Width / 2);
        visual.CenterPoint = new Vector3(half, half, 0);

        if (shouldSpin)
        {
            _vinylSpinning = true;
            _vinylSpinStarted = Stopwatch.GetTimestamp();
            visual.RotationAngleInDegrees = (float)_vinylAngle;
            _vinylTimer.Start();
            UpdateTonearm(animate: true);
            return;
        }

        _vinylSpinning = false;
        _vinylTimer.Stop();
        _vinylAngle = (_vinylAngle
            + Stopwatch.GetElapsedTime(_vinylSpinStarted).TotalSeconds * VinylDegreesPerSecond) % 360;
        visual.RotationAngleInDegrees = (float)_vinylAngle;
        UpdateTonearm(animate: true);
    }

    private void OnVinylTimer(DispatcherQueueTimer sender, object args)
    {
        if (!_vinylSpinning)
        {
            sender.Stop();
            return;
        }

        var elapsed = Stopwatch.GetElapsedTime(_vinylSpinStarted).TotalSeconds;
        var angle = (_vinylAngle + elapsed * VinylDegreesPerSecond) % 360;
        ElementCompositionPreview.GetElementVisual(VinylSpinLayer).RotationAngleInDegrees = (float)angle;
    }

    /// <summary>点唱臂切换播放：与播放条按键走同一条命令，不在这里另判引擎状态。</summary>
    private void OnTonearmTapped(object sender, TappedRoutedEventArgs args)
    {
        if (!Player.HasTrack) return;
        args.Handled = true;
        Player.TogglePlayPauseCommand.Execute(null);
    }

    /// <summary>播放时唱臂落到盘面，暂停时抬回待机位；切换样式时直接摆到位，不做过渡。</summary>
    private void UpdateTonearm(bool animate)
    {
        if (Lyrics.CoverStyle != LyricsCoverStyle.Vinyl) return;
        var angle = Player.IsPlaying ? TonearmPlayingAngle : TonearmRestingAngle;
        if (!animate)
        {
            var visual = ElementCompositionPreview.GetElementVisual(TonearmRotor);
            visual.StopAnimation("RotationAngleInDegrees");
            visual.RotationAngleInDegrees = angle;
            return;
        }

        AnimateTonearm(angle, TimeSpan.FromMilliseconds(360));
    }

    private void AnimateTonearm(float angle, TimeSpan duration)
    {
        var visual = ElementCompositionPreview.GetElementVisual(TonearmRotor);
        using var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(1, angle);
        animation.Duration = duration;
        visual.StartAnimation("RotationAngleInDegrees", animation);
    }

    /// <summary>换碟：提起唱臂，旧碟带旧封面从左滑出，新碟从右滑入，最后再落针。</summary>
    /// <remarks>
    /// 切歌时真实的 DetailCover 已经先换成新图，滑出阶段用 <c>CoverSwapLabel</c> 盖住它，
    /// 滑出去的才是旧封面。位移都在 <c>VinylClipHost</c> 的裁剪区内完成，不会压到窗口左侧。
    /// 连续切歌时版本号会让上一轮后续步骤作废。
    /// </remarks>
    private async void PlayCoverSwap()
    {
        if (!IsLoaded || !AppMotion.IsEnabled || Lyrics.CoverStyle != LyricsCoverStyle.Vinyl) return;
        var next = Player.CoverImage;
        var previous = _previousCoverImage;
        if (next is null || previous is null || ReferenceEquals(next, previous)) return;

        var size = (float)VinylClipHost.ActualWidth;
        if (size <= 0) return;

        var version = ++_coverSwapVersion;
        var armDuration = TimeSpan.FromMilliseconds(220);

        // 下一首：旧碟向左、新碟从右；上一首反过来。其他切换方式（点选、恢复）按下一首处理。
        var forwards = Player.TrackChangeDirection >= 0;
        var exitTo = forwards ? new Vector3(-size, 0, 0) : new Vector3(size, 0, 0);
        var enterFrom = forwards ? new Vector3(size, 0, 0) : new Vector3(-size, 0, 0);

        // 旧封面遮罩必须在提起唱臂之前就盖上：切歌瞬间绑定已经把真实标签换成了新图，
        // 晚一步盖就会在抬臂那两百毫秒里露出下一首的封面。
        CoverSwapLabelImage.Source = previous;
        CoverSwapLabel.Visibility = Visibility.Visible;

        // 第一拍：提起唱臂。
        AnimateTonearm(TonearmRestingAngle, armDuration);
        await Task.Delay(armDuration);
        if (version != _coverSwapVersion) return;

        // 第二拍：整层带旧封面滑出去。
        var moved = await AppMotion.PlayAsync(VinylSpinLayer, Vector3.Zero, exitTo,
            1, 1, Vector3.One, Vector3.One, AppMotion.Standard);
        if (!moved || version != _coverSwapVersion) return;

        // 第三拍：此时整层在裁剪区外，收起旧封面并从另一侧滑入新碟。
        CoverSwapLabel.Visibility = Visibility.Collapsed;
        await AppMotion.PlayAsync(VinylSpinLayer, enterFrom, Vector3.Zero,
            1, 1, Vector3.One, Vector3.One, AppMotion.Standard);
        if (version != _coverSwapVersion) return;

        // 第四拍：还在播放才落回盘面；暂停中保持抬起。
        if (Player.IsPlaying) AnimateTonearm(TonearmPlayingAngle, armDuration);
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
            || _progressSeeking || _keyboardSeeking || _volumePopupOpen || _moreFlyoutOpen || _shareFlyoutOpen
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
            || _progressSeeking || _keyboardSeeking || _volumePopupOpen || _moreFlyoutOpen || _shareFlyoutOpen
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

    /// <remarks>
    /// 与「更多」同一套 popup 守卫：面板开着时控制台不能自己淡出，
    /// 否则会剩一个悬空的菜单挂在那儿。见 <see cref="ScheduleChromeHide"/>。
    /// </remarks>
    private void OnShareFlyoutOpening(object? sender, object e)
    {
        _shareFlyoutOpen = true;
        ShowChrome();
        ApplyFlyoutTheme(ShareMenu);

        var hasLyrics = !Lyrics.Document.IsEmpty && !Lyrics.IsLoading;
        MakePosterButton.IsEnabled = hasLyrics;
        MakePosterSubtitle.Text = hasLyrics
            ? "把歌词做成一张图片"
            : Lyrics.IsLoading ? "歌词加载中" : "当前歌曲没有歌词";
    }

    private void OnShareFlyoutOpened(object? sender, object e) => ApplyFlyoutTheme(ShareMenu);

    private void OnShareFlyoutClosed(object? sender, object e)
    {
        _shareFlyoutOpen = false;
        ScheduleChromeHide();
    }

    private void OnCopyShareLinkClick(object sender, RoutedEventArgs args)
    {
        ShareFlyout.Hide();

        // 沿用原来的分享命令：上报 + 复制链接 + 提示都在里面。
        Player.ShareCommand.Execute(null);
    }

    /// <summary>打开歌词海报：选句 → 预览 → 保存 / 复制。</summary>
    private async void OnMakePosterClick(object sender, RoutedEventArgs args)
    {
        ShareFlyout.Hide();

        if (Player.CurrentTrack is not { } track || Player.CreateShareLink() is not { } shareLink)
        {
            return;
        }

        var lines = LyricPosterComposer.Compose(Lyrics.PosterSource);
        if (lines.Count == 0)
        {
            ((INoticeSink)_trackActions).Show("当前歌曲没有歌词", NoticeSeverity.Error);
            return;
        }

        // 默认勾选当前播放句；开场前（还没到第一句）退回第一句，保证一进来就能生成。
        var defaultIndex = Math.Max(0, LyricPosterComposer.IndexAt(lines, Lyrics.TimelinePosition));

        var context = new LyricPosterDialogContext(
            track,
            shareLink,
            lines,
            defaultIndex,
            XamlRoot,
            _window.ThemeRoot.ActualTheme,
            _windowHandle,
            _clipboard,
            async () =>
            {
                // 对话框开着时自动切歌也照常发生；切走了就不上报，避免把分享数记到别的曲目上。
                if (Player.CurrentTrackId == track.Id)
                {
                    await Player.Statistics.ReportShareAsync();
                }
            },
            _trackActions,
            _logger,
            _fontOptions,
            _posterSettings,
            _posterPaletteLoader);

        try
        {
            await LyricPosterDialog.ShowAsync(context);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "打开歌词海报失败：{Title}", track.Title);
            ((INoticeSink)_trackActions).Show("海报生成失败，请稍后再试。", NoticeSeverity.Error);
        }
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

    /// <summary>播放器外壳的预设色板，与纯色背景共用同一组深色。</summary>
    private void BuildMp3ShellPalette()
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
            button.Click += (_, _) => Lyrics.SetMp3ShellColor(value, auto: false);
            ToolTipService.SetToolTip(button, name);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"外壳颜色：{name}");
            _mp3ShellDots.Add((dot, value));
            Mp3ShellPalette.Children.Add(button);
        }

        _customMp3ShellDot = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(ToColor(Lyrics.Mp3ShellCustomColorArgb)),
            VerticalAlignment = VerticalAlignment.Center,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
        };

        var applyButton = new Button
        {
            Content = _customMp3ShellDot,
            Width = 28,
            Height = 28,
            MinWidth = 0,
            Padding = new Thickness(5),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
        };
        applyButton.Click += (_, _) => Lyrics.SetMp3ShellColor(Lyrics.Mp3ShellCustomColorArgb, auto: false);
        ToolTipService.SetToolTip(applyButton, "应用自定义颜色");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(applyButton, "应用自定义外壳颜色");

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
        pencilButton.Click += (_, _) => _ = OpenMp3ShellColorDialogAsync();
        ToolTipService.SetToolTip(pencilButton, "编辑自定义颜色");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(pencilButton, "编辑自定义外壳颜色");

        var customCell = new Grid { Width = 28, Height = 28 };
        customCell.Children.Add(applyButton);
        customCell.Children.Add(pencilButton);
        Mp3ShellPalette.Children.Add(customCell);
    }

    private void UpdateMp3ShellSelection()
    {
        foreach (var (dot, argb) in _mp3ShellDots)
        {
            var selected = !Lyrics.Mp3ShellColorAuto && Lyrics.Mp3ShellColorArgb == argb;
            dot.BorderBrush = new SolidColorBrush(selected
                ? Color.FromArgb(0xFF, 0x00, 0xD9, 0xA0)
                : Color.FromArgb(0x66, 0x80, 0x80, 0x80));
            dot.BorderThickness = new Thickness(selected ? 2 : 1);
        }

        if (_customMp3ShellDot is { } custom)
        {
            custom.Background = new SolidColorBrush(ToColor(Lyrics.Mp3ShellCustomColorArgb));
            var selected = !Lyrics.Mp3ShellColorAuto
                && Lyrics.Mp3ShellColorArgb == Lyrics.Mp3ShellCustomColorArgb;
            custom.BorderBrush = new SolidColorBrush(selected
                ? Color.FromArgb(0xFF, 0x00, 0xD9, 0xA0)
                : Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
            custom.BorderThickness = new Thickness(selected ? 2 : 1);
        }
    }

    /// <summary>外壳当前实际使用的颜色：自动模式下取封面派生色。</summary>
    private Color Mp3ShellDisplayColor
        => Lyrics.Mp3ShellColorAuto && _autoMp3ShellColor is { } auto
            ? auto
            : ToColor(Lyrics.Mp3ShellColorArgb);

    /// <summary>把外壳颜色铺成上亮下暗的哑光渐变，扇形键跟着壳体取一层更浅的填充。</summary>
    private void ApplyMp3ShellColor()
    {
        var baseColor = Mp3ShellDisplayColor;
        Mp3Shell.Background = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops =
            {
                new GradientStop { Color = Mix(baseColor, Colors.White, 0.14), Offset = 0 },
                new GradientStop { Color = Mix(baseColor, Colors.White, 0.04), Offset = 0.55 },
                new GradientStop { Color = Mix(baseColor, Colors.Black, 0.30), Offset = 1 },
            },
        };
        Mp3Shell.BorderBrush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops =
            {
                new GradientStop { Color = Mix(baseColor, Colors.White, 0.38), Offset = 0 },
                new GradientStop { Color = Mix(baseColor, Colors.White, 0.12), Offset = 0.35 },
                new GradientStop { Color = Mix(baseColor, Colors.Black, 0.12), Offset = 1 },
            },
        };

        _mp3SectorBrush.Color = Mix(baseColor, Colors.White, 0.05);
        _mp3SectorHoverBrush.Color = Mix(baseColor, Colors.White, 0.14);
        foreach (var button in new[] { Mp3UpButton, Mp3DownButton, Mp3PreviousButton, Mp3NextButton })
        {
            if (button.Content is Microsoft.UI.Xaml.Shapes.Path path) path.Fill = _mp3SectorBrush;
        }
    }

    private static Color Mix(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(0xFF,
            (byte)Math.Round(from.R + ((to.R - from.R) * amount)),
            (byte)Math.Round(from.G + ((to.G - from.G) * amount)),
            (byte)Math.Round(from.B + ((to.B - from.B) * amount)));
    }

    /// <summary>外壳跟随封面取色：与纯色背景同一条取色管线，同样固定取深色。</summary>
    private async Task RefreshMp3ShellColorAsync()
    {
        if (Lyrics.CoverStyle != LyricsCoverStyle.Mp3 || !Lyrics.Mp3ShellColorAuto)
        {
            return;
        }

        var request = ++_autoMp3ShellRequest;
        var seeds = await _mp3PaletteLoader.LoadAsync(Player.CurrentTrackId ?? 0, Player.CurrentCoverUri);

        if (request != _autoMp3ShellRequest
            || Lyrics.CoverStyle != LyricsCoverStyle.Mp3
            || !Lyrics.Mp3ShellColorAuto)
        {
            return;
        }

        var dark = ColorPalette.SolidBackground(seeds, AmbientTheme.Dark);
        _autoMp3ShellColor = Color.FromArgb(255, dark.R, dark.G, dark.B);
        ApplyMp3ShellColor();
        if (_appearanceOpen) SyncAppearanceControls();
    }

    private void OnMp3ShellAutoToggled(object sender, RoutedEventArgs args)
    {
        if (_syncingAppearance || !_appearanceOpen) return;
        Lyrics.SetMp3ShellColorAuto(Mp3ShellAutoToggle.IsOn);
    }

    private async Task OpenMp3ShellColorDialogAsync()
    {
        if (!_appearanceOpen) return;

        var current = ToColor(Lyrics.Mp3ShellCustomColorArgb);
        var preview = new Border
        {
            Height = 120,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(current),
        };
        preview.Child = new TextBlock
        {
            Text = "外壳颜色预览",
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
        };

        var chosen = await ColorPickerDialog.ShowAsync(
            current,
            XamlRoot,
            _themeRoot?.ActualTheme ?? ElementTheme.Default,
            preview,
            color => preview.Background = new SolidColorBrush(color),
            _ => "外壳会按这个颜色生成上亮下暗的哑光渐变",
            _logger);

        if (chosen is { } argb)
        {
            Lyrics.SetMp3ShellCustomColor(argb);
        }
    }

    /// <summary>把 ViewModel 的当前值回填到面板控件；回填期间不反向写设置。</summary>
    private void SyncAppearanceControls()
    {
        _syncingAppearance = true;
        try
        {
            LyricsFontPicker.SelectedIndex = FontIndexOf(Lyrics.FontFamily);
            TranslationSettingsPanel.Visibility = Lyrics.HasTranslation
                ? Visibility.Visible
                : Visibility.Collapsed;
            LyricsCoverStyleCombo.SelectedIndex = Lyrics.CoverStyle switch
            {
                LyricsCoverStyle.Vinyl => 1,
                LyricsCoverStyle.Mp3 => 2,
                _ => 0,
            };
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
            var coverBlur = Lyrics.BackgroundMode == LyricsBackgroundMode.CoverBlur;
            CoverBlurPanel.Visibility = coverBlur ? Visibility.Visible : Visibility.Collapsed;
            CoverBlurDimSlider.Value = Math.Round(Lyrics.CoverBlurDim * 100);
            CoverBlurDimValue.Text = $"{(int)Math.Round(Lyrics.CoverBlurDim * 100)}%";
            CoverBlurAmountSlider.Value = Lyrics.CoverBlurAmount;
            CoverBlurAmountValue.Text = ((int)Math.Round(Lyrics.CoverBlurAmount)).ToString();
            var solidBackground = Lyrics.BackgroundMode == LyricsBackgroundMode.SolidColor;
            SolidColorPanel.Visibility = solidBackground ? Visibility.Visible : Visibility.Collapsed;
            var micaTint = Lyrics.BackgroundMode == LyricsBackgroundMode.Mica && MainWindow.IsMicaSupported;
            MicaTintPanel.Visibility = micaTint ? Visibility.Visible : Visibility.Collapsed;
            MicaTintToggle.IsOn = Lyrics.MicaTintEnabled;
            SolidColorAutoToggle.IsOn = Lyrics.BackgroundColorAuto;
            var displayColor = SolidDisplayColor;
            SolidColorHex.Text = Lyrics.BackgroundColorAuto
                ? $"跟随封面 · #{displayColor.R:X2}{displayColor.G:X2}{displayColor.B:X2}"
                : $"#{displayColor.R:X2}{displayColor.G:X2}{displayColor.B:X2}";
            UpdateSolidColorSelection();
            Mp3ShellColorPanel.Visibility = Lyrics.CoverStyle == LyricsCoverStyle.Mp3
                ? Visibility.Visible
                : Visibility.Collapsed;
            Mp3ShellAutoToggle.IsOn = Lyrics.Mp3ShellColorAuto;
            var mp3ShellColor = Mp3ShellDisplayColor;
            Mp3ShellHex.Text = Lyrics.Mp3ShellColorAuto
                ? $"跟随封面 · #{mp3ShellColor.R:X2}{mp3ShellColor.G:X2}{mp3ShellColor.B:X2}"
                : $"#{mp3ShellColor.R:X2}{mp3ShellColor.G:X2}{mp3ShellColor.B:X2}";
            UpdateMp3ShellSelection();
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
            InactiveLineOpacitySlider.Value = Math.Round(Lyrics.InactiveLineOpacity * 100);
            InactiveLineOpacityValue.Text = $"{(int)Math.Round(Lyrics.InactiveLineOpacity * 100)}%";
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

    private void OnLyricsCoverStyleSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_syncingAppearance || !_appearanceOpen) return;
        Lyrics.SetCoverStyle(LyricsCoverStyleCombo.SelectedIndex switch
        {
            1 => LyricsCoverStyle.Vinyl,
            2 => LyricsCoverStyle.Mp3,
            _ => LyricsCoverStyle.Cover,
        });
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

    private void OnMicaTintToggled(object sender, RoutedEventArgs args)
    {
        if (_syncingAppearance || !_appearanceOpen) return;
        Lyrics.SetMicaTintEnabled(MicaTintToggle.IsOn);
    }

    private async Task OpenSolidColorDialogAsync()
    {
        if (!_appearanceOpen) return;

        // 编辑的是「保存的自定义色」，不是当前生效的预设色；
        // 否则每次打开都会被最近切换的颜色覆盖掉。
        var current = ToColor(Lyrics.BackgroundCustomColorArgb);

        var preview = new Border
        {
            Height = 170,
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
        preview.Child = previewLines;

        var chosen = await ColorPickerDialog.ShowAsync(
            current,
            XamlRoot,
            _themeRoot?.ActualTheme ?? ElementTheme.Default,
            preview,
            color => preview.Background = new SolidColorBrush(color),
            color => ColorPickerDialog.RelativeLuminance(color) > 0.45
                ? "颜色偏亮，白色歌词可能不够清晰"
                : "对比度良好",
            _logger);

        if (chosen is { } argb)
        {
            Lyrics.SetBackgroundCustomColor(argb);
        }
    }

    /// <summary>
    /// 纯色背景自动取色：与外壳同一条多色取色管线，但歌词页固定取深色。
    /// </summary>
    /// <remarks>
    /// 歌词页永远是白字深底，只需要深色变体；外壳有深色与浅色两种主题，
    /// 由 <see cref="ColorPalette.SolidBackground"/> 按主题各自映射。
    /// </remarks>
    private async Task RefreshSolidColorAsync()
    {
        if (Lyrics.BackgroundMode != LyricsBackgroundMode.SolidColor || !Lyrics.BackgroundColorAuto)
        {
            return;
        }

        var request = ++_autoColorRequest;
        var trackId = Player.CurrentTrackId ?? 0;
        var cover = Player.CurrentCoverUri;
        var seeds = await _paletteLoader.LoadAsync(trackId, cover);

        if (request != _autoColorRequest
            || Lyrics.BackgroundMode != LyricsBackgroundMode.SolidColor
            || !Lyrics.BackgroundColorAuto)
        {
            return;
        }

        var dark = ColorPalette.SolidBackground(seeds, AmbientTheme.Dark);
        _autoSolidColor = Color.FromArgb(255, dark.R, dark.G, dark.B);
        ApplyBackgroundMode();
        if (_appearanceOpen) SyncAppearanceControls();
    }


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

            var fileName = await _backgroundImages.ImportAsync(file, "lyrics-bg");
            if (fileName is null) return false;

            var dim = await _backgroundImages.EstimateDimAsync(
                file, LyricsSettings.MaximumBackgroundDim, LyricsSettings.DefaultBackgroundDim);

            var crop = await BackgroundCropDialog.ShowAsync(
                AppPaths.BackgroundFile(fileName),
                BackgroundAspect,
                0.5,
                0.5,
                1,
                XamlRoot,
                _themeRoot?.ActualTheme ?? ElementTheme.Default,
                _logger);

            if (crop is not { } value)
            {
                // 取消裁剪就不使用这张图，别在背景目录里留下孤儿文件。
                _backgroundImages.Delete(fileName);
                return false;
            }

            var previous = Lyrics.BackgroundImageFile;
            Lyrics.SetBackgroundImage(fileName, dim, value.FocusX, value.FocusY, value.Zoom);

            if (previous.Length > 0 && !string.Equals(previous, fileName, StringComparison.Ordinal))
            {
                _backgroundImages.Delete(previous);
            }

            return true;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "选择歌词背景图片失败");
            return false;
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

        var crop = await BackgroundCropDialog.ShowAsync(
            path,
            BackgroundAspect,
            Lyrics.BackgroundImageFocusX,
            Lyrics.BackgroundImageFocusY,
            Lyrics.BackgroundImageZoom,
            XamlRoot,
            _themeRoot?.ActualTheme ?? ElementTheme.Default,
            _logger);

        if (crop is { } value)
        {
            Lyrics.SetBackgroundImageCrop(value.FocusX, value.FocusY, value.Zoom);
        }
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

    private void OnCoverBlurDimChanged(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (_syncingAppearance || !_appearanceOpen) return;
        CoverBlurDimValue.Text = $"{(int)Math.Round(args.NewValue)}%";
        Lyrics.SetCoverBlurDim(args.NewValue / 100);
    }

    private void OnCoverBlurAmountChanged(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (_syncingAppearance || !_appearanceOpen) return;
        CoverBlurAmountValue.Text = ((int)Math.Round(args.NewValue)).ToString();
        Lyrics.SetCoverBlurAmount(args.NewValue);
    }

    private void OnInactiveLineOpacityChanged(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (_syncingAppearance || !_appearanceOpen) return;
        InactiveLineOpacityValue.Text = $"{(int)Math.Round(args.NewValue)}%";
        Lyrics.SetInactiveLineOpacity(args.NewValue / 100);
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

    private void OnResetCoverBlurDimClick(object sender, RoutedEventArgs args)
    {
        if (!_appearanceOpen) return;
        Lyrics.SetCoverBlurDim(LyricsSettings.DefaultCoverBlurDim);
    }

    private void OnResetCoverBlurAmountClick(object sender, RoutedEventArgs args)
    {
        if (!_appearanceOpen) return;
        Lyrics.SetCoverBlurAmount(LyricsSettings.DefaultCoverBlurAmount);
    }

    private void OnResetInactiveLineOpacityClick(object sender, RoutedEventArgs args)
    {
        if (!_appearanceOpen) return;
        Lyrics.SetInactiveLineOpacity(LyricsSettings.DefaultInactiveLineOpacity);
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
        _canvas.SetInactiveLineOpacity(Lyrics.InactiveLineOpacity);
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
            // 当前歌曲没有译文时，外观面板里的「译文」分组直接收起来。
            if (_appearanceOpen)
            {
                TranslationSettingsPanel.Visibility = Lyrics.HasTranslation
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            return;
        }

        if (args.PropertyName == nameof(LyricsViewModel.CoverStyle))
        {
            ApplyCoverStyle();
            if (_appearanceOpen) SyncAppearanceControls();
            return;
        }

        if (args.PropertyName is nameof(LyricsViewModel.Mp3ShellColorArgb)
            or nameof(LyricsViewModel.Mp3ShellCustomColorArgb)
            or nameof(LyricsViewModel.Mp3ShellColorAuto))
        {
            if (Lyrics.CoverStyle == LyricsCoverStyle.Mp3 && Lyrics.Mp3ShellColorAuto)
            {
                _ = RefreshMp3ShellColorAsync();
            }

            ApplyMp3ShellColor();
            if (_appearanceOpen) SyncAppearanceControls();
            return;
        }

        if (args.PropertyName is nameof(LyricsViewModel.BackgroundMode)
            or nameof(LyricsViewModel.CoverBlurDim)
            or nameof(LyricsViewModel.CoverBlurAmount)
            or nameof(LyricsViewModel.BackgroundImageFile)
            or nameof(LyricsViewModel.BackgroundImageDim)
            or nameof(LyricsViewModel.BackgroundImageBlur)
            or nameof(LyricsViewModel.BackgroundImageFocusX)
            or nameof(LyricsViewModel.BackgroundImageFocusY)
            or nameof(LyricsViewModel.BackgroundImageZoom)
            or nameof(LyricsViewModel.BackgroundColorArgb)
            or nameof(LyricsViewModel.BackgroundCustomColorArgb)
            or nameof(LyricsViewModel.BackgroundColorAuto)
            or nameof(LyricsViewModel.MicaTintEnabled))
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
            or nameof(LyricsViewModel.InactiveLineOpacity)
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
