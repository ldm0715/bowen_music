using System.ComponentModel;
using System.Numerics;
using Bodian.WinUI.Controls;
using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
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
    private readonly INavigationService _navigation;
    private readonly MainWindow _window;
    private readonly LyricsCanvasView _canvas;
    private readonly AudioSpectrumView _spectrum;
    private readonly DispatcherQueueTimer _chromeTimer;
    private readonly DispatcherQueueTimer _pointerTimer;
    private readonly nint _windowHandle;
    private NativeMethods.NativePoint? _lastCursorPoint;
    private bool _keyboardInteractionActive;
    private bool _windowVisible = true;
    private bool _progressSeeking;
    private bool _keyboardSeeking;
    private bool _chromeVisible = true;
    private bool _pointerOverChrome;
    private bool _volumeSeeking;
    private FrameworkElement? _progressThumb;
    private Rectangle? _progressTrack;
    private Rectangle? _progressFill;
    private bool _progressPointerOver;
    private double _progressPointerX;

    public LyricsPage(MainWindow window, LyricsCanvasView canvas, AudioSpectrumView spectrum, PlayerViewModel player,
        LyricsViewModel lyrics, INavigationService navigation)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(spectrum);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(lyrics);
        ArgumentNullException.ThrowIfNull(navigation);
        _window = window;
        _canvas = canvas;
        _spectrum = spectrum;
        _navigation = navigation;
        Player = player;
        Lyrics = lyrics;
        InitializeComponent();
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
        VolumeSlider.AddHandler(PointerPressedEvent, new PointerEventHandler(OnVolumePressed), true);
        VolumeSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnVolumeReleased), true);
        VolumeSlider.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnVolumeReleased), true);
        VolumeSlider.AddHandler(PointerCanceledEvent, new PointerEventHandler(OnVolumeReleased), true);
        Loaded += (_, _) => UpdateLayoutSizing();
    }

    public PlayerViewModel Player { get; }
    public LyricsViewModel Lyrics { get; }
    public string PlayPauseGlyph(bool playing) => playing ? "\uE769" : "\uE768";
    public string SongHeading(string title, string artist)
        => string.IsNullOrWhiteSpace(artist) ? title : $"{title} - {artist}";

    public void OnNavigatedTo()
    {
        _window.VisibilityChanged += OnWindowVisibilityChanged;
        _window.AppWindow.Changed += OnAppWindowChanged;
        Player.PropertyChanged += OnPlayerChanged;
        _pointerOverChrome = false;
        _keyboardInteractionActive = false;
        _lastCursorPoint = null;
        _window.EnterLyrics(LyricsTitleBar);
        Lyrics.IsOpen = true;
        UpdatePause();
        SyncFullscreen();
        _pointerTimer.Start();
        UpdatePointerLocation();
        AnimateEntrance();
    }

    public void OnNavigatedFrom()
    {
        _chromeTimer.Stop();
        _pointerTimer.Stop();
        ProgressTimePopup.IsOpen = false;
        _window.VisibilityChanged -= OnWindowVisibilityChanged;
        _window.AppWindow.Changed -= OnAppWindowChanged;
        Player.PropertyChanged -= OnPlayerChanged;
        _volumeSeeking = false;
        _progressSeeking = _keyboardSeeking = false;
        Player.IsSeeking = false;
        Lyrics.IsOpen = false;
        UpdatePause();
        _window.ExitLyrics();
    }

    private void OnWindowVisibilityChanged(object? sender, WindowVisibilityChangedEventArgs args)
    {
        _windowVisible = args.Visible;
        UpdatePause();
        if (args.Visible)
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

    private void UpdatePause()
    {
        _canvas.IsPaused = !Lyrics.IsOpen || !_windowVisible;
        _spectrum.IsPaused = _canvas.IsPaused;
        ReflectionView.IsPaused = _canvas.IsPaused;
    }
    private void OnRootSizeChanged(object sender, SizeChangedEventArgs args) => UpdateLayoutSizing();

    private void UpdateLayoutSizing()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var availableHeight = Math.Max(140, MainStage.ActualHeight / 1.8);
        var size = Math.Max(140, Math.Min(Math.Min(ActualHeight * 0.45, ActualWidth * 0.4 - 84), availableHeight));
        size = Math.Min(size, 580);
        CoverFrame.Width = CoverFrame.Height = size;
        CoverDeck.Width = CoverDeck.Height = size;
        ReflectionView.Width = size;
        ReflectionView.Height = size * 0.55;
        Canvas.SetTop(ReflectionView, size + 2);
        _canvas.SetFontSize(Math.Max(ActualHeight * 0.05, ActualWidth * 0.025));
        VolumeDeck.Visibility = ActualWidth < 900 ? Visibility.Collapsed : Visibility.Visible;
        UpdateChromeInsets();
        UpdateCoverScale();
    }

    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(PlayerViewModel.IsPlaying)) UpdateCoverScale();
        if (args.PropertyName == nameof(PlayerViewModel.CurrentTrackId)) ShowChrome();
    }

    private void UpdateCoverScale()
    {
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
        if (args.DidPresenterChange) SyncFullscreen();
    }

    private void SyncFullscreen()
    {
        FullscreenIcon.Glyph = _window.IsLyricsFullscreen ? "\uE73F" : "\uE740";
        ToolTipService.SetToolTip(FullscreenButton, _window.IsLyricsFullscreen ? "退出全屏 (F11)" : "进入全屏 (F11)");
        UpdateChromeInsets();
        ShowChrome();
    }

    private void UpdateChromeInsets()
    {
        var scale = XamlRoot?.RasterizationScale ?? 1;
        var captionWidth = _window.IsLyricsFullscreen ? 0 : _window.AppWindow.TitleBar.RightInset / scale;
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
        if (!Lyrics.IsOpen || !_windowVisible || XamlRoot is null
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
        if (!Lyrics.IsOpen || !_windowVisible || !_chromeVisible || _pointerOverChrome
            || _progressSeeking || _keyboardSeeking || _volumeSeeking || HasChromeKeyboardFocus()) return;
        _chromeTimer.Start();
    }

    private void ShowChrome()
    {
        SetChromeVisibility(true);
        ScheduleChromeHide();
    }

    private void HideChrome()
    {
        if (!Lyrics.IsOpen || !_windowVisible || _pointerOverChrome
            || _progressSeeking || _keyboardSeeking || _volumeSeeking || HasChromeKeyboardFocus())
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

    private void OnVolumePressed(object sender, PointerRoutedEventArgs args)
    {
        _volumeSeeking = true;
        ShowChrome();
    }

    private void OnVolumeReleased(object sender, PointerRoutedEventArgs args)
    {
        _volumeSeeking = false;
        ScheduleChromeHide();
    }

    private void OnBackClick(object sender, RoutedEventArgs args) => _navigation.GoBack();
    private void OnFollowClick(object sender, RoutedEventArgs args) => _canvas.ResumeFollowing();
    private void ToggleFullscreen() { _window.ToggleLyricsFullscreen(); SyncFullscreen(); }
    private void OnFullscreenClick(object sender, RoutedEventArgs args) => ToggleFullscreen();
    private void OnFullscreenInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    { ToggleFullscreen(); args.Handled = true; }
    private void OnEscapeInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_window.IsLyricsFullscreen) ToggleFullscreen();
        else _navigation.GoBack();
        args.Handled = true;
    }
    private void OnPlayPauseInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (PositionSlider.FocusState == FocusState.Keyboard || VolumeSlider.FocusState == FocusState.Keyboard) return;
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
