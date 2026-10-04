using System.ComponentModel;
using System.Diagnostics;
using System.Numerics;
using Bodian.Core.Models;
using Bodian.WinUI.Controls;
using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
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
using VirtualKey = Windows.System.VirtualKey;

namespace Bodian.WinUI.Views;

public sealed partial class LyricsPage : Page, INavigationAware
{
    private void OnQualitySelected(object? sender, EventArgs e) => QualityFlyout.Hide();
    private readonly MainWindow _window;
    private readonly INavigationService _navigation;
    private readonly Func<Track, MvPage> _mvFactory;
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
    private bool _pointerOverChrome;
    private bool _volumePopupOpen;
    private FrameworkElement? _progressThumb;
    private Rectangle? _progressTrack;
    private Rectangle? _progressFill;
    private bool _progressPointerOver;
    private double _progressPointerX;

    public LyricsPage(MainWindow window, LyricsCanvasView canvas, AudioSpectrumView spectrum, PlayerViewModel player,
        LyricsViewModel lyrics, SongCommentsViewModel comments,
        INavigationService navigation, Func<Track, MvPage> mvFactory)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(spectrum);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(lyrics);
        ArgumentNullException.ThrowIfNull(comments);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(mvFactory);
        _window = window;
        _navigation = navigation;
        _mvFactory = mvFactory;
        _logger = (Application.Current.Resources["BodianLoggerFactory"] as ILoggerFactory
            ?? NullLoggerFactory.Instance).CreateLogger<LyricsPage>();
        _canvas = canvas;
        _spectrum = spectrum;
        Player = player;
        Lyrics = lyrics;
        Comments = comments;
        InitializeComponent();
        _commentsPanel = new SongCommentsPanel(comments);
        _commentsPanel.CloseRequested += (_, _) => CloseComments();
        CommentsHost.Content = _commentsPanel;
        ElementCompositionPreview.SetIsTranslationEnabled(CommentsPane, true);
        CanvasHost.Content = canvas;
        SpectrumHost.Content = spectrum;
        canvas.BrowsingChanged += (_, _) => FollowButton.Visibility = canvas.IsBrowsing ? Visibility.Visible : Visibility.Collapsed;
        _chromeTimer = DispatcherQueue.CreateTimer();
        _chromeTimer.Interval = TimeSpan.FromSeconds(3);
        _chromeTimer.IsRepeating = false;
        _chromeTimer.Tick += (_, _) => HideChrome();
        _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        _pointerTimer = DispatcherQueue.CreateTimer();
        _pointerTimer.Interval = TimeSpan.FromMilliseconds(200);
        _pointerTimer.Tick += (_, _) => UpdatePointerLocation();
        _layoutTimer = DispatcherQueue.CreateTimer();
        _layoutTimer.Interval = TimeSpan.FromMilliseconds(120);
        _layoutTimer.IsRepeating = false;
        _layoutTimer.Tick += (_, _) =>
        {
            if (_window.IsRenderingSuspended) return;
            _layoutPending = false;
            UpdateLayoutSizing();
        };
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
        _themeRoot = _window.ThemeRoot;
        _themeRoot.ActualThemeChanged += OnAppThemeChanged;
        SyncCommentsTheme();
        if (Player.CurrentTrackId is > 0) _ = Comments.LoadBadgeAsync(Player.CurrentTrackId.Value);
        _window.VisibilityChanged += OnWindowVisibilityChanged;
        _window.RenderingStateChanged += OnWindowRenderingStateChanged;
        _window.AppWindow.Changed += OnAppWindowChanged;
        Player.PropertyChanged += OnPlayerChanged;
        _pointerOverChrome = false;
        _keyboardInteractionActive = false;
        _lastCursorPoint = null;
        _window.EnterImmersive(LyricsTitleBar);
        Lyrics.IsOpen = true;
        UpdatePause();
        SyncFullscreen();
        _pointerTimer.Start();
        UpdatePointerLocation();
        AnimateEntrance();
    }

    public void OnNavigatedFrom()
    {
        if (_themeRoot is not null) _themeRoot.ActualThemeChanged -= OnAppThemeChanged;
        _themeRoot = null;
        CloseComments(restoreFocus: false);
        Comments.CancelBadgeRequest();
        _chromeTimer.Stop();
        _pointerTimer.Stop();
        _layoutTimer.Stop();
        ProgressTimePopup.IsOpen = false;
        _window.VisibilityChanged -= OnWindowVisibilityChanged;
        _window.RenderingStateChanged -= OnWindowRenderingStateChanged;
        _window.AppWindow.Changed -= OnAppWindowChanged;
        Player.PropertyChanged -= OnPlayerChanged;
        _volumePopupOpen = false;
        _progressSeeking = _keyboardSeeking = false;
        Player.IsSeeking = false;
        Lyrics.IsOpen = false;
        UpdatePause();
        _window.ExitImmersive();
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
        BackdropView.IsPaused = _canvas.IsPaused;
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
        _canvas.SetFontSize(Math.Max(ActualHeight * 0.05, ActualWidth * 0.025));
        CommentsPane.Width = Math.Max(0, Math.Min(420, ActualWidth - 32));
        UpdateChromeInsets();
        UpdateCoverScale();
    }

    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(PlayerViewModel.IsPlaying)) UpdateCoverScale();
        if (args.PropertyName == nameof(PlayerViewModel.CurrentTrackId))
        {
            ShowChrome();
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

    private void AnimateEntrance()
    {
        var visual = ElementCompositionPreview.GetElementVisual(LyricsRoot);
        using var opacity = visual.Compositor.CreateScalarKeyFrameAnimation();
        opacity.InsertKeyFrame(0, 0);
        opacity.InsertKeyFrame(1, 1);
        opacity.Duration = TimeSpan.FromMilliseconds(400);
        visual.StartAnimation("Opacity", opacity);
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
            || _progressSeeking || _keyboardSeeking || _volumePopupOpen || HasChromeKeyboardFocus()) return;
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
            || _progressSeeking || _keyboardSeeking || _volumePopupOpen || HasChromeKeyboardFocus())
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
    }

    /// <summary>音量弹层开着时控制台不能自己收走，否则展开的那颗按钮会跟着消失。</summary>
    private void OnVolumePopupChanged(object? sender, EventArgs args)
    {
        _volumePopupOpen = VolumeControl.IsPopupOpen;
        if (_volumePopupOpen) ShowChrome();
        else ScheduleChromeHide();
    }

    private void OnAppThemeChanged(FrameworkElement sender, object args) => SyncCommentsTheme();

    private void SyncCommentsTheme()
    {
        // 歌词舞台固定深色，评论控件应跟随主窗口的实际主题，包含“跟随系统”。
        var theme = _window.ThemeRoot.ActualTheme;
        CommentsPane.RequestedTheme = theme;
        _commentsPanel.RequestedTheme = theme;
        CommentsImageViewer.RequestedTheme = theme;
    }

    private async void OnCommentsClick(object sender, RoutedEventArgs args)
    {
        if (Comments.IsOpen)
        {
            CloseComments();
            return;
        }
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
        DispatcherQueue.TryEnqueue(() =>
        {
            if (Comments.IsOpen) _commentsPanel.FocusCloseButton();
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
        _commentsPanel.FocusCloseButton();
    }

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
    private async void OnFullscreenClick(object sender, RoutedEventArgs args) => await ToggleFullscreenAsync();
    private async void OnFullscreenInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    { args.Handled = true; await ToggleFullscreenAsync(); }
    private async void OnEscapeInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
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
