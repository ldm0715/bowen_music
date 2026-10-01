using System.ComponentModel;
using System.Numerics;
using Bodian.WinUI.Controls;
using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using VirtualKey = Windows.System.VirtualKey;

namespace Bodian.WinUI.Views;

public sealed partial class LyricsPage : Page, INavigationAware
{
    private readonly INavigationService _navigation;
    private readonly MainWindow _window;
    private readonly LyricsCanvasView _canvas;
    private readonly DispatcherQueueTimer _chromeTimer;
    private bool _windowVisible = true;
    private bool _progressSeeking;
    private bool _keyboardSeeking;
    private bool _chromeVisible = true;

    public LyricsPage(MainWindow window, LyricsCanvasView canvas, PlayerViewModel player,
        LyricsViewModel lyrics, INavigationService navigation)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(lyrics);
        ArgumentNullException.ThrowIfNull(navigation);
        _window = window;
        _canvas = canvas;
        _navigation = navigation;
        Player = player;
        Lyrics = lyrics;
        InitializeComponent();
        CanvasHost.Content = canvas;
        canvas.BrowsingChanged += (_, _) => FollowButton.Visibility = canvas.IsBrowsing ? Visibility.Visible : Visibility.Collapsed;
        _chromeTimer = DispatcherQueue.CreateTimer();
        _chromeTimer.Interval = TimeSpan.FromSeconds(3);
        _chromeTimer.IsRepeating = false;
        _chromeTimer.Tick += (_, _) => HideChrome();
        AddHandler(PointerMovedEvent, new PointerEventHandler(OnPointerActivity), true);
        AddHandler(PointerPressedEvent, new PointerEventHandler(OnPointerActivity), true);
        PositionSlider.AddHandler(PointerPressedEvent, new PointerEventHandler(OnProgressPressed), true);
        PositionSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnProgressReleased), true);
        PositionSlider.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnProgressReleased), true);
        PositionSlider.AddHandler(PointerCanceledEvent, new PointerEventHandler(OnProgressCanceled), true);
        PositionSlider.AddHandler(KeyDownEvent, new KeyEventHandler(OnProgressKeyDown), true);
        PositionSlider.AddHandler(KeyUpEvent, new KeyEventHandler(OnProgressKeyUp), true);
        Loaded += (_, _) => UpdateLayoutSizing();
    }

    public PlayerViewModel Player { get; }
    public LyricsViewModel Lyrics { get; }
    public string PlayPauseGlyph(bool playing) => playing ? "\uE769" : "\uE768";

    public void OnNavigatedTo()
    {
        _window.VisibilityChanged += OnWindowVisibilityChanged;
        _window.AppWindow.Changed += OnAppWindowChanged;
        Player.PropertyChanged += OnPlayerChanged;
        _window.EnterLyrics(LyricsTitleBar);
        Lyrics.IsOpen = true;
        UpdatePause();
        SyncFullscreen();
        AnimateEntrance();
    }

    public void OnNavigatedFrom()
    {
        _chromeTimer.Stop();
        _window.VisibilityChanged -= OnWindowVisibilityChanged;
        _window.AppWindow.Changed -= OnAppWindowChanged;
        Player.PropertyChanged -= OnPlayerChanged;
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
        if (args.Visible) ShowChrome();
        else _chromeTimer.Stop();
    }

    private void UpdatePause() => _canvas.IsPaused = !Lyrics.IsOpen || !_windowVisible;
    private void OnRootSizeChanged(object sender, SizeChangedEventArgs args) => UpdateLayoutSizing();

    private void UpdateLayoutSizing()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var size = Math.Max(140, Math.Min(Math.Min(ActualHeight * 0.45, ActualWidth * 0.4 - 84), ActualHeight - 342));
        size = Math.Min(size, 580);
        CoverFrame.Width = CoverFrame.Height = size;
        CoverDeck.Width = size;
        _canvas.SetFontSize(Math.Max(ActualHeight * 0.05, ActualWidth * 0.025));
        VolumeDeck.Visibility = ActualWidth < 900 ? Visibility.Collapsed : Visibility.Visible;
        UpdateCoverScale();
    }

    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(PlayerViewModel.IsPlaying)) UpdateCoverScale();
        if (args.PropertyName == nameof(PlayerViewModel.CurrentTrackId)) ShowChrome();
    }

    private void UpdateCoverScale()
    {
        var visual = ElementCompositionPreview.GetElementVisual(CoverFrame);
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
        ShowChrome();
    }

    private void OnPointerActivity(object sender, PointerRoutedEventArgs args) => ShowChrome();

    private void ShowChrome()
    {
        SetChromeVisibility(true);
        _chromeTimer.Stop();
        if (_window.IsLyricsFullscreen && Lyrics.IsOpen && _windowVisible) _chromeTimer.Start();
    }

    private void HideChrome()
    {
        if (!_window.IsLyricsFullscreen || _progressSeeking || _keyboardSeeking
            || PositionSlider.FocusState == FocusState.Keyboard || VolumeSlider.FocusState == FocusState.Keyboard)
            return;
        SetChromeVisibility(false);
    }

    private void SetChromeVisibility(bool visible)
    {
        if (_chromeVisible == visible) return;
        _chromeVisible = visible;
        foreach (var element in new FrameworkElement[] { LyricsTitleBar, ControlDeck })
        {
            element.IsHitTestVisible = visible;
            var visual = ElementCompositionPreview.GetElementVisual(element);
            using var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
            animation.InsertKeyFrame(1, visible ? 1 : 0);
            animation.Duration = TimeSpan.FromMilliseconds(280);
            visual.StartAnimation("Opacity", animation);
        }
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
        ShowChrome();
    }
    private void OnProgressReleased(object sender, PointerRoutedEventArgs args)
    {
        if (!_progressSeeking) return;
        _progressSeeking = false;
        _ = Player.SeekToAsync(PositionSlider.Value);
        ShowChrome();
    }
    private void OnProgressCanceled(object sender, PointerRoutedEventArgs args)
    {
        _progressSeeking = false;
        Player.IsSeeking = false;
    }
    private void OnProgressKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down
            or VirtualKey.Home or VirtualKey.End or VirtualKey.PageUp or VirtualKey.PageDown)
        {
            _keyboardSeeking = true;
            Player.IsSeeking = true;
            ShowChrome();
        }
    }
    private void OnProgressKeyUp(object sender, KeyRoutedEventArgs args)
    {
        if (!_keyboardSeeking) return;
        _keyboardSeeking = false;
        _ = Player.SeekToAsync(PositionSlider.Value);
        ShowChrome();
    }
}
