using Bodian.WinUI.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using VirtualKey = Windows.System.VirtualKey;

namespace Bodian.WinUI.Controls;

/// <summary>
/// 音量入口：一颗图标按钮，悬停或点击才在上方展开竖向滑条。
/// </summary>
/// <remarks>
/// <para>
/// 播放条、歌词页与 MV 页共用这一份 —— <b>音量交互本来就该一样</b>：一颗图标按钮，
/// 悬停或点击才在上方展开竖向滑条。三处的音量数值互不相干（前两处走 libmpv、
/// MV 走 <c>MediaPlayer</c>），把它们接到一起的是 <see cref="IVolumeSource"/>。
/// </para>
/// <para>
/// ★ <b>任何一个宿主页都不许把这个交互改成横向外显的滑条</b> —— MV 页早先就是这么写的，
/// 用户明确要求过不许这样。
/// </para>
/// </remarks>
public sealed partial class VolumeButton : UserControl
{
    private readonly DispatcherQueueTimer _closeTimer;
    private bool _pointerOver;
    private bool _dragging;

    public VolumeButton()
    {
        InitializeComponent();

        _closeTimer = DispatcherQueue.CreateTimer();
        _closeTimer.Interval = TimeSpan.FromMilliseconds(250);
        _closeTimer.IsRepeating = false;
        _closeTimer.Tick += (_, _) =>
        {
            if (!_pointerOver && !_dragging && VolumeSlider.FocusState != FocusState.Keyboard)
            {
                SetPopupOpen(false);
            }
        };

        // Slider 会处理内部指针事件，仍需接收它们以维持拖动状态。
        IconButton.AddHandler(PointerEnteredEvent, new PointerEventHandler(OnVolumePointerEntered), true);
        IconButton.AddHandler(PointerExitedEvent, new PointerEventHandler(OnVolumePointerExited), true);
        IconButton.AddHandler(PointerMovedEvent, new PointerEventHandler(OnVolumePointerMoved), true);
        VolumePopupHost.AddHandler(PointerEnteredEvent, new PointerEventHandler(OnVolumePointerEntered), true);
        VolumePopupHost.AddHandler(PointerExitedEvent, new PointerEventHandler(OnVolumePointerExited), true);
        VolumePopupHost.AddHandler(PointerMovedEvent, new PointerEventHandler(OnVolumePointerMoved), true);
        VolumeSlider.AddHandler(PointerPressedEvent, new PointerEventHandler(OnVolumeSliderPressed), true);
        VolumeSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnVolumeSliderReleased), true);
        VolumeSlider.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnVolumeSliderReleased), true);
        VolumeSlider.AddHandler(PointerCanceledEvent, new PointerEventHandler(OnVolumeSliderReleased), true);

        Unloaded += OnUnloaded;
    }

    private static void OnDisplayChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((VolumeButton)sender).Bindings.Update();

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(IVolumeSource), typeof(VolumeButton), new PropertyMetadata(null, OnDisplayChanged));

    public IVolumeSource ViewModel
    {
        get => (IVolumeSource)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    /// <summary>弹层开合。<b>歌词页靠它把控制台钉住</b> —— 拖音量时控制台不能被自动隐藏带走。</summary>
    public event EventHandler? PopupStateChanged;

    /// <summary>弹层是否展开。</summary>
    public bool IsPopupOpen => VolumePopup.IsOpen;

    /// <summary>收起弹层。宿主在窗口尺寸变化这类场合调用。</summary>
    public void ClosePopup()
    {
        _closeTimer.Stop();
        SetPopupOpen(false);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _pointerOver = false;
        _dragging = false;
        ClosePopup();
    }

    private void OnVolumePointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _pointerOver = true;
        ShowVolumePopup();
    }

    private void OnVolumePointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var target = (FrameworkElement)sender;
        var point = e.GetCurrentPoint(target).Position;
        _pointerOver = point.X >= 0 && point.X <= target.ActualWidth && point.Y >= 0 && point.Y <= target.ActualHeight;
        if (_pointerOver)
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
        _pointerOver = false;
        ScheduleVolumeClose();
    }

    private void OnVolumeClick(object sender, RoutedEventArgs e)
    {
        ShowVolumePopup();
        if (IconButton.FocusState == FocusState.Keyboard)
        {
            VolumeSlider.Focus(FocusState.Keyboard);
        }
    }

    private void ShowVolumePopup()
    {
        _closeTimer.Stop();
        if (XamlRoot is null)
        {
            return;
        }

        VolumePopupHost.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var origin = IconButton.TransformToVisual(null).TransformPoint(new Point());
        var hostOrigin = TransformToVisual(null).TransformPoint(new Point());
        var size = VolumePopupHost.DesiredSize;
        VolumePopup.XamlRoot = XamlRoot;
        VolumePopup.HorizontalOffset = Math.Clamp(
            origin.X + IconButton.ActualWidth / 2 - size.Width / 2,
            8,
            Math.Max(8, XamlRoot.Size.Width - size.Width - 8)) - hostOrigin.X;
        VolumePopup.VerticalOffset = Math.Max(0, origin.Y - size.Height) - hostOrigin.Y;
        SetPopupOpen(true);
    }

    private void OnVolumeSliderPressed(object sender, PointerRoutedEventArgs e)
    {
        _dragging = true;
        _closeTimer.Stop();
    }

    private void OnVolumeSliderReleased(object sender, PointerRoutedEventArgs e)
    {
        _dragging = false;
        ScheduleVolumeClose();
    }

    private void ScheduleVolumeClose()
    {
        if (!_pointerOver && !_dragging)
        {
            _closeTimer.Start();
        }
    }

    private void OnVolumeLostFocus(object sender, RoutedEventArgs e) => ScheduleVolumeClose();

    private void OnVolumeKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            ClosePopup();
            IconButton.Focus(FocusState.Keyboard);
            e.Handled = true;
        }
    }

    private void SetPopupOpen(bool open)
    {
        if (VolumePopup.IsOpen == open)
        {
            return;
        }

        VolumePopup.IsOpen = open;
        PopupStateChanged?.Invoke(this, EventArgs.Empty);
    }
}
