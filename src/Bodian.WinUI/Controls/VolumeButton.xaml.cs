using System.ComponentModel;
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
    /// <summary>鼠标移开之后多久收起滑条。</summary>
    private static readonly TimeSpan PointerLeaveDelay = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// 音量在别处被改了（快捷键）时，弹层多留一会儿的时长。
    /// 用鼠标移开那 250 毫秒的话，一按键弹层就闪一下没了，用户根本来不及看调到多少。
    /// </summary>
    private static readonly TimeSpan ExternalChangeHold = TimeSpan.FromMilliseconds(1600);

    private readonly DispatcherQueueTimer _closeTimer;
    private IVolumeSource? _subscribed;
    private bool _pointerOver;
    private bool _dragging;

    public VolumeButton()
    {
        InitializeComponent();

        _closeTimer = DispatcherQueue.CreateTimer();
        _closeTimer.Interval = PointerLeaveDelay;
        _closeTimer.IsRepeating = false;

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

        Loaded += (_, _) => { _closeTimer.Tick += OnCloseTimer; AttachVolumeSource(); };
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
        DetachVolumeSource();
        ClosePopup();
        _closeTimer.Tick -= OnCloseTimer;
    }

    private void OnCloseTimer(DispatcherQueueTimer sender, object args)
    {
        if (!_pointerOver && !_dragging && VolumeSlider.FocusState != FocusState.Keyboard) SetPopupOpen(false);
    }

    /// <remarks>
    /// 订阅音源是为了「音量在别处被改了就把滑条露出来」——快捷键调音量时，
    /// 不露出来的话用户只能盲调，不知道调到了多少。先摘再挂，重复调用是安全的。
    /// </remarks>
    private void AttachVolumeSource()
    {
        DetachVolumeSource();

        if (ViewModel is not { } source)
        {
            return;
        }

        source.PropertyChanged += OnVolumeSourceChanged;
        _subscribed = source;
    }

    private void DetachVolumeSource()
    {
        if (_subscribed is not null)
        {
            _subscribed.PropertyChanged -= OnVolumeSourceChanged;
        }

        _subscribed = null;
    }

    private void OnVolumeSourceChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(IVolumeSource.Volume))
        {
            ShowVolumeForExternalChange();
        }
    }

    /// <summary>
    /// 音量刚在别处被改过：把弹层露出来，并在 <see cref="ExternalChangeHold"/> 之后收起。
    /// </summary>
    /// <remarks>
    /// <b>已经开着就什么都不做。</b> 用户正在拖滑条时音量也在变，那种情况下重新定位弹层
    /// 会让它跟着手指跳。不可见或还没挂进可视树时也不做 —— 播放条在沉浸态是藏起来的，
    /// 那时该由歌词页那份实例去露。
    /// </remarks>
    private void ShowVolumeForExternalChange()
    {
        if (IsPopupOpen || Visibility != Visibility.Visible || XamlRoot is null)
        {
            return;
        }

        ShowVolumePopup();
        ScheduleVolumeClose(ExternalChangeHold);
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

    private void ScheduleVolumeClose() => ScheduleVolumeClose(PointerLeaveDelay);

    private void ScheduleVolumeClose(TimeSpan delay)
    {
        if (!_pointerOver && !_dragging)
        {
            // 每次重新设并重启：连着按快捷键时，计时从最后一次按键算起。
            _closeTimer.Interval = delay;
            _closeTimer.Stop();
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
