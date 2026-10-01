using System.ComponentModel;
using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
using Bodian.WinUI.Views;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using VirtualKey = Windows.System.VirtualKey;

namespace Bodian.WinUI.Controls;

/// <summary>底部播放条。</summary>
public sealed partial class PlayerBar : UserControl
{
    private readonly INavigationService _navigation;
    private readonly DispatcherQueueTimer _volumeCloseTimer;
    private FrameworkElement? _progressThumb;
    private Rectangle? _progressTrack;
    private Rectangle? _progressFill;
    private bool _isProgressPointerOver;
    private double _progressPointerX;
    private bool _isVolumePointerOver;
    private bool _isVolumeDragging;

    public PlayerBar(PlayerViewModel viewModel, LyricsViewModel lyrics, INavigationService navigation)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(lyrics);
        ArgumentNullException.ThrowIfNull(navigation);

        ViewModel = viewModel;
        Lyrics = lyrics;
        _navigation = navigation;

        InitializeComponent();

        _volumeCloseTimer = DispatcherQueue.CreateTimer();
        _volumeCloseTimer.Interval = TimeSpan.FromMilliseconds(250);
        _volumeCloseTimer.IsRepeating = false;
        _volumeCloseTimer.Tick += (_, _) =>
        {
            if (!_isVolumePointerOver && !_isVolumeDragging && VolumeSlider.FocusState != FocusState.Keyboard)
            {
                VolumePopup.IsOpen = false;
            }
        };

        // Slider 会处理内部指针事件，仍需接收它们以更新气泡和拖动状态。
        PositionSlider.AddHandler(PointerEnteredEvent, new PointerEventHandler(OnProgressPointerEntered), true);
        PositionSlider.AddHandler(PointerExitedEvent, new PointerEventHandler(OnProgressPointerExited), true);
        VolumeButton.AddHandler(PointerEnteredEvent, new PointerEventHandler(OnVolumePointerEntered), true);
        VolumeButton.AddHandler(PointerExitedEvent, new PointerEventHandler(OnVolumePointerExited), true);
        VolumeButton.AddHandler(PointerMovedEvent, new PointerEventHandler(OnVolumePointerMoved), true);
        VolumePopupHost.AddHandler(PointerEnteredEvent, new PointerEventHandler(OnVolumePointerEntered), true);
        VolumePopupHost.AddHandler(PointerExitedEvent, new PointerEventHandler(OnVolumePointerExited), true);
        VolumePopupHost.AddHandler(PointerMovedEvent, new PointerEventHandler(OnVolumePointerMoved), true);
        PositionSlider.AddHandler(PointerPressedEvent, new PointerEventHandler(OnSliderPressed), true);
        PositionSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnSliderReleased), true);
        PositionSlider.AddHandler(PointerMovedEvent, new PointerEventHandler(OnProgressPointerMoved), true);
        PositionSlider.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnProgressCaptureLost), true);
        PositionSlider.AddHandler(PointerCanceledEvent, new PointerEventHandler(OnProgressCaptureLost), true);
        VolumeSlider.AddHandler(PointerPressedEvent, new PointerEventHandler(OnVolumeSliderPressed), true);
        VolumeSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnVolumeSliderReleased), true);
        VolumeSlider.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnVolumeSliderReleased), true);
        VolumeSlider.AddHandler(PointerCanceledEvent, new PointerEventHandler(OnVolumeSliderReleased), true);

        PositionSlider.Loaded += OnProgressSliderLoaded;
        PositionSlider.GotFocus += (_, _) => UpdateProgressAppearance();
        PositionSlider.LostFocus += (_, _) => UpdateProgressAppearance();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += (_, _) => ClosePopups();
    }

    public PlayerViewModel ViewModel { get; }

    /// <summary>歌词页是否活跃。封面和「词」按钮按它置灰。</summary>
    public LyricsViewModel Lyrics { get; }

    private void OnLoaded(object sender, RoutedEventArgs e) => ViewModel.PropertyChanged += OnPlayerPropertyChanged;

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnPlayerPropertyChanged;
        ViewModel.IsSeeking = false;
        _isProgressPointerOver = false;
        _isVolumePointerOver = false;
        _isVolumeDragging = false;
        ClosePopups();
    }

    private void ClosePopups()
    {
        _volumeCloseTimer.Stop();
        ProgressTimePopup.IsOpen = false;
        VolumePopup.IsOpen = false;
    }

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.DurationSeconds) && ProgressTimePopup.IsOpen)
        {
            UpdateProgressTime();
        }
    }

    private void OnProgressSliderLoaded(object sender, RoutedEventArgs e)
    {
        PositionSlider.ApplyTemplate();
        _progressThumb = FindTemplatePart<FrameworkElement>(PositionSlider, "HorizontalThumb");
        _progressTrack = FindTemplatePart<Rectangle>(PositionSlider, "HorizontalTrackRect");
        _progressFill = FindTemplatePart<Rectangle>(PositionSlider, "HorizontalDecreaseRect");
        UpdateProgressAppearance();
    }

    private static T? FindTemplatePart<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        if (root is T element && element.Name == name)
        {
            return element;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var match = FindTemplatePart<T>(VisualTreeHelper.GetChild(root, i), name);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private void UpdateProgressAppearance()
    {
        var active = _isProgressPointerOver || ViewModel.IsSeeking || PositionSlider.FocusState == FocusState.Keyboard;
        if (_progressThumb is not null)
        {
            _progressThumb.Opacity = active ? 1 : 0;
        }

        if (_progressTrack is not null)
        {
            _progressTrack.Height = active ? 2.5 : 1.5;
        }

        if (_progressFill is not null)
        {
            _progressFill.Height = active ? 2.5 : 1.5;
        }
    }

    private void OnProgressPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _isProgressPointerOver = true;
        OnProgressPointerMoved(sender, e);
    }

    private void OnProgressPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(PositionSlider).Position;
        _progressPointerX = point.X;
        _isProgressPointerOver = point.X >= 0 && point.X <= PositionSlider.ActualWidth
            && point.Y >= 0 && point.Y <= PositionSlider.ActualHeight;
        if (_isProgressPointerOver || ViewModel.IsSeeking)
        {
            UpdateProgressTime();
        }
    }

    private void OnProgressPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _isProgressPointerOver = false;
        UpdateProgressAppearance();
        if (!ViewModel.IsSeeking)
        {
            ProgressTimePopup.IsOpen = false;
        }
    }

    private void OnSliderPressed(object sender, PointerRoutedEventArgs e)
    {
        ViewModel.IsSeeking = true;
        OnProgressPointerMoved(sender, e);
    }

    private void OnSliderReleased(object sender, PointerRoutedEventArgs e)
    {
        // 拖动期间不 seek、松开才 seek，保留原有播放交互。
        _ = ViewModel.SeekToAsync(PositionSlider.Value);
        UpdateProgressAppearance();
        if (_isProgressPointerOver)
        {
            UpdateProgressTime();
        }
        else
        {
            ProgressTimePopup.IsOpen = false;
        }
    }

    private void OnProgressCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        ViewModel.IsSeeking = false;
        UpdateProgressAppearance();
        if (!_isProgressPointerOver)
        {
            ProgressTimePopup.IsOpen = false;
        }
    }

    private void OnProgressValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (ViewModel.IsSeeking)
        {
            UpdateProgressTime();
        }
    }

    private void UpdateProgressTime()
    {
        UpdateProgressAppearance();
        if (XamlRoot is null || PositionSlider.ActualWidth <= 8)
        {
            return;
        }

        // 按原生滑块的实际宽度计算悬停时间，保持预览与拖动行程一致。
        var thumbWidth = _progressThumb?.ActualWidth ?? 8;
        var fraction = Math.Clamp((_progressPointerX - thumbWidth / 2) / Math.Max(1, PositionSlider.ActualWidth - thumbWidth), 0, 1);
        var seconds = ViewModel.IsSeeking ? PositionSlider.Value : fraction * ViewModel.DurationSeconds;
        ProgressTimeText.Text = $"{Formats.Seconds(seconds)} / {Formats.Seconds(ViewModel.DurationSeconds)}";
        ProgressTimeBubble.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        var origin = PositionSlider.TransformToVisual(null).TransformPoint(new Point());
        var hostOrigin = TransformToVisual(null).TransformPoint(new Point());
        var size = ProgressTimeBubble.DesiredSize;
        ProgressTimePopup.XamlRoot = XamlRoot;
        ProgressTimePopup.HorizontalOffset = Math.Clamp(
            origin.X + _progressPointerX - size.Width / 2,
            8,
            Math.Max(8, XamlRoot.Size.Width - size.Width - 8)) - hostOrigin.X;
        ProgressTimePopup.VerticalOffset = Math.Max(0, origin.Y - size.Height - 6) - hostOrigin.Y;
        ProgressTimePopup.IsOpen = true;
    }

    private void OnVolumePointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _isVolumePointerOver = true;
        ShowVolumePopup();
    }

    private void OnVolumePointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var target = (FrameworkElement)sender;
        var point = e.GetCurrentPoint(target).Position;
        _isVolumePointerOver = point.X >= 0 && point.X <= target.ActualWidth
            && point.Y >= 0 && point.Y <= target.ActualHeight;
        if (_isVolumePointerOver)
        {
            ShowVolumePopup();
        }
        else
        {
            ScheduleVolumeClose();
        }
    }

    private void OnVolumePointerExited(object sender, PointerRoutedEventArgs e)
    {
        _isVolumePointerOver = false;
        ScheduleVolumeClose();
    }

    private void OnVolumeClick(object sender, RoutedEventArgs e)
    {
        ShowVolumePopup();
        if (VolumeButton.FocusState == FocusState.Keyboard)
        {
            VolumeSlider.Focus(FocusState.Keyboard);
        }
    }

    private void ShowVolumePopup()
    {
        _volumeCloseTimer.Stop();
        if (XamlRoot is null)
        {
            return;
        }

        ProgressTimePopup.IsOpen = false;
        VolumePopupHost.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var origin = VolumeButton.TransformToVisual(null).TransformPoint(new Point());
        var hostOrigin = TransformToVisual(null).TransformPoint(new Point());
        var size = VolumePopupHost.DesiredSize;
        VolumePopup.XamlRoot = XamlRoot;
        VolumePopup.HorizontalOffset = Math.Clamp(
            origin.X + VolumeButton.ActualWidth / 2 - size.Width / 2,
            8,
            Math.Max(8, XamlRoot.Size.Width - size.Width - 8)) - hostOrigin.X;
        VolumePopup.VerticalOffset = Math.Max(0, origin.Y - size.Height) - hostOrigin.Y;
        VolumePopup.IsOpen = true;
    }

    private void OnVolumeSliderPressed(object sender, PointerRoutedEventArgs e)
    {
        _isVolumeDragging = true;
        _volumeCloseTimer.Stop();
    }

    private void OnVolumeSliderReleased(object sender, PointerRoutedEventArgs e)
    {
        _isVolumeDragging = false;
        ScheduleVolumeClose();
    }

    private void ScheduleVolumeClose()
    {
        if (!_isVolumePointerOver && !_isVolumeDragging)
        {
            _volumeCloseTimer.Start();
        }
    }

    private void OnVolumeLostFocus(object sender, RoutedEventArgs e) => ScheduleVolumeClose();

    private void OnVolumeKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            ClosePopups();
            VolumeButton.Focus(FocusState.Keyboard);
            e.Handled = true;
        }
    }

    /// <summary>封面和「词」按钮共用歌词页导航。</summary>
    private void OnLyricsClick(object sender, RoutedEventArgs e) => _navigation.Navigate<LyricsPage>();
}
