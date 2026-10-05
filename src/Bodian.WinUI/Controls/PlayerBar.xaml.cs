using System.ComponentModel;
using Bodian.Core.Models;
using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
using Bodian.WinUI.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace Bodian.WinUI.Controls;

/// <summary>底部播放条。</summary>
public sealed partial class PlayerBar : UserControl
{
    private readonly INavigationService _navigation;
    private FrameworkElement? _progressThumb;
    private Rectangle? _progressTrack;
    private Rectangle? _progressFill;
    private bool _isProgressPointerOver;
    private double _progressPointerX;

    public PlayerBar(
        PlayerViewModel viewModel,
        LyricsViewModel lyrics,
        DesktopLyricsViewModel desktopLyrics,
        INavigationService navigation)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(lyrics);
        ArgumentNullException.ThrowIfNull(desktopLyrics);
        ArgumentNullException.ThrowIfNull(navigation);

        ViewModel = viewModel;
        Lyrics = lyrics;
        DesktopLyrics = desktopLyrics;
        _navigation = navigation;

        InitializeComponent();
        UpdateDesktopLyricsButton();

        // Slider 会处理内部指针事件，仍需接收它们以更新气泡和拖动状态。
        PositionSlider.AddHandler(PointerEnteredEvent, new PointerEventHandler(OnProgressPointerEntered), true);
        PositionSlider.AddHandler(PointerExitedEvent, new PointerEventHandler(OnProgressPointerExited), true);
        PositionSlider.AddHandler(PointerPressedEvent, new PointerEventHandler(OnSliderPressed), true);
        PositionSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnSliderReleased), true);
        PositionSlider.AddHandler(PointerMovedEvent, new PointerEventHandler(OnProgressPointerMoved), true);
        PositionSlider.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnProgressCaptureLost), true);
        PositionSlider.AddHandler(PointerCanceledEvent, new PointerEventHandler(OnProgressCaptureLost), true);

        PositionSlider.Loaded += OnProgressSliderLoaded;
        PositionSlider.GotFocus += (_, _) => UpdateProgressAppearance();
        PositionSlider.LostFocus += (_, _) => UpdateProgressAppearance();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += (_, _) =>
        {
            ClosePopups();
            UpdateTitleWidth();
        };
    }

    public PlayerViewModel ViewModel { get; }

    private void OnQualitySelected(object? sender, EventArgs e) => QualityFlyout.Hide();

    /// <summary>歌词页是否活跃。封面按它置灰。</summary>
    public LyricsViewModel Lyrics { get; }

    /// <summary>桌面歌词条的开关与偏好。那颗按钮直接绑它的命令。</summary>
    public DesktopLyricsViewModel DesktopLyrics { get; }

    /// <summary>
    /// 点了「播放列表」。
    /// </summary>
    /// <remarks>
    /// <b>只抛事件，不开面板</b>：队列抽屉要盖住内容区，而播放条自己就占着窗口最下面那一行，
    /// 在这一层放不下。抽屉归主窗口管，开合也在那边。
    /// </remarks>
    public event EventHandler? PlaylistRequested;

    private void OnPlaylistClick(object sender, RoutedEventArgs e) => PlaylistRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// 点了 MV 按钮。载荷就是当前曲目。
    /// </summary>
    /// <remarks>
    /// <b>只抛事件，不自己导航</b>：MV 页要带曲目构造，而那个工厂在容器里、
    /// 外壳才拿得到。<b>也不能反过来把导航器注入 <c>PlayerViewModel</c></b> ——
    /// <c>TrackActionsService</c>（导航器的实现）已经依赖 <c>PlayerViewModel</c>，会成环。
    /// 与 <see cref="PlaylistRequested"/> 是同一种接线。
    /// </remarks>
    public event EventHandler<Track>? MvRequested;

    private void OnMvClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CurrentTrack is { } track)
        {
            MvRequested?.Invoke(this, track);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged += OnPlayerPropertyChanged;
        DesktopLyrics.PropertyChanged += OnDesktopLyricsPropertyChanged;
        UpdateDesktopLyricsButton();
        UpdateTitleWidth();
    }

    /// <summary>在 XAML 状态中保留 ThemeResource，主题切换时选中态画刷也重新求值。</summary>
    private void UpdateDesktopLyricsButton()
        => VisualStateManager.GoToState(this,
            DesktopLyrics.IsEnabled ? "DesktopLyricsActive" : "DesktopLyricsInactive", useTransitions: false);

    private void OnDesktopLyricsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DesktopLyricsViewModel.IsEnabled))
        {
            UpdateDesktopLyricsButton();
        }
    }

    private void OnDesktopLyricsClick(object sender, RoutedEventArgs e)
        => DesktopLyrics.ToggleCommand.Execute(null);

    /// <summary>
    /// 曲名的宽度上限。**在代码里算，不用 <c>x:Bind</c> 绑 <c>ActualWidth</c>。**
    /// </summary>
    /// <remarks>
    /// <para>
    /// 实测 <c>x:Bind</c> 对 <c>ActualWidth</c> 的依赖属性回调**在首轮测量（宽度还是 0）之后
    /// 没有再触发**，函数绑定就停在下限上不走了 —— 表现是**每首歌名都被截到 4 个字**
    /// （2026-10-04 发现：「答应不爱你」显示成「答应不…」）。同样的写法在 MV 页与歌词页的
    /// 标题上也用过，那两处同样是坏的。
    /// </para>
    /// <para>
    /// 尺寸变化与首次布局都会走到这里，算出来的才是真的。列宽还没量到时直接返回，
    /// 免得把 0 当成真实宽度算出下限。
    /// </para>
    /// </remarks>
    private void UpdateTitleWidth()
    {
        var columnWidth = TransportRow.ColumnDefinitions[0].ActualWidth;
        if (columnWidth <= 0)
        {
            return;
        }

        TitleText.MaxWidth = Formats.PlayerTitleMaxWidth(
            columnWidth, ViewModel.IsAudition, ViewModel.HasPayLabel);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnPlayerPropertyChanged;
        DesktopLyrics.PropertyChanged -= OnDesktopLyricsPropertyChanged;
        ViewModel.IsSeeking = false;
        _isProgressPointerOver = false;
        ClosePopups();
    }

    private void ClosePopups()
    {
        ProgressTimePopup.IsOpen = false;
        VolumeControl.ClosePopup();
    }

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.DurationSeconds) && ProgressTimePopup.IsOpen)
        {
            UpdateProgressTime();
        }

        // 徽标出现/消失会占掉一截宽度，上限跟着变。
        if (e.PropertyName is nameof(PlayerViewModel.IsAudition) or nameof(PlayerViewModel.HasPayLabel))
        {
            UpdateTitleWidth();
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

    /// <summary>封面和「词」按钮共用歌词页导航。</summary>
    private void OnLyricsClick(object sender, RoutedEventArgs e) => _navigation.Navigate<LyricsPage>();
}
