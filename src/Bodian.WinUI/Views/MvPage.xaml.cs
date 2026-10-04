using System.ComponentModel;
using Bodian.Core.Models;
using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using VirtualKey = Windows.System.VirtualKey;

namespace Bodian.WinUI.Views;

/// <summary>
/// MV 播放页。全窗沉浸，与歌词页并列。
/// </summary>
/// <remarks>
/// <b>进入时暂停音频、退出时恢复</b>：MV 自己有声，两路同时响会打架。
/// 恢复只针对「本次进来时是自己暂停的」那一种，用户本来按了暂停就不去动它 ——
/// 见 <see cref="MvViewModel.PauseAudioForVideo"/>。
/// </remarks>
public sealed partial class MvPage : Page, INavigationAware
{
    private readonly MainWindow _window;
    private readonly MvViewModel _viewModel;
    private readonly ILogger<MvPage> _logger;
    private readonly bool _diagnostics;

    public MvPage(MainWindow window, MvViewModel viewModel, Track track)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(track);

        _window = window;
        _viewModel = viewModel;
        Track = track;
        _logger = (Application.Current.Resources["BodianLoggerFactory"] as ILoggerFactory
            ?? NullLoggerFactory.Instance).CreateLogger<MvPage>();
        _diagnostics = Environment.GetEnvironmentVariable("BODIAN_MV_DIAGNOSTICS") == "1";

        InitializeComponent();

        VideoSurface.SetMediaPlayer(viewModel.Player);
        _viewModel.PropertyChanged += OnViewModelChanged;

        // caption 的宽度随窗口尺寸（其实是随 DPI）变，跟着重算。
        MvRoot.SizeChanged += (_, _) => UpdateChromeInsets();
        PositionSlider.AddHandler(PointerPressedEvent, new PointerEventHandler(OnSeekPressed), true);
        PositionSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnSeekReleased), true);
        PositionSlider.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnSeekReleased), true);
        PositionSlider.AddHandler(KeyDownEvent, new KeyEventHandler(OnSeekKeyDown), true);
        PositionSlider.AddHandler(KeyUpEvent, new KeyEventHandler(OnSeekKeyUp), true);
        UpdateTimes();
    }

    /// <summary>
    /// 这一页在放哪首歌。
    /// </summary>
    /// <remarks>
    /// <b>刻意是 internal 而不是 public</b>：XAML 类型信息生成器会为公开属性里的每个类型
    /// 生成激活代码，而 <see cref="Track"/> 有 <c>required</c> 成员 —— 生成器造不出实例，
    /// 直接编译失败（<c>XamlTypeInfo.g.cs</c>）。这个属性只在本页用，收起来没有代价。
    /// </remarks>
    internal Track Track { get; }

    public MvViewModel ViewModel => _viewModel;

    public void OnNavigatedTo()
    {
        _window.EnterImmersive(MvTitleBar);
        _viewModel.PauseAudioForVideo();
        UpdateChromeInsets();
        _ = _viewModel.LoadAsync(Track);
    }

    public void OnNavigatedFrom()
    {
        _viewModel.PropertyChanged -= OnViewModelChanged;
        _viewModel.RestoreAudioAfterVideo();

        // ★ 顺序不能换：必须先把元素与播放器解绑，再释放播放器。
        //
        // 导航的顺序是「通知离场 → 把本页从 ContentControl 摘掉」。这里只是第一步，
        // 此时 MediaPlayerElement 还挂在树上、还持着那个 MediaPlayer；如果在这就把它
        // Dispose 掉，等第二步卸载元素时它会去碰已经释放的原生对象，抛的是
        // COMException(0x80004004)，走不到托管 catch，应用直接闪退。
        VideoSurface.SetMediaPlayer(null);

        _window.ExitImmersive();
        _viewModel.Dispose();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(MvViewModel.PositionSeconds) or nameof(MvViewModel.DurationSeconds))
        {
            UpdateTimes();
        }

        // 解码器报出画面尺寸、或用户换了缩放方式，都要重算矩形。
        if (args.PropertyName is nameof(MvViewModel.NaturalAspect) or nameof(MvViewModel.FitMode))
        {
            UpdateVideoRect();
        }
    }

    // ── 画面比例 ────────────────────────────────────────────────────────────

    private void OnVideoHostSizeChanged(object sender, SizeChangedEventArgs args) => UpdateVideoRect();

    // ── 标题栏 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 修正标题栏里留给系统那三颗窗口按钮的位置。
    /// </summary>
    /// <remarks>
    /// 内置 <c>TitleBar</c> 给 caption 预留的是**物理像素**，没有除缩放 ——
    /// 在 125% / 150% 的屏幕上会宽出一截，右边那颗按钮就被推得离系统按钮很远。
    /// 歌词页有同一段修正（<c>LyricsPage.UpdateChromeInsets</c>）。
    /// </remarks>
    private void UpdateChromeInsets()
    {
        if (XamlRoot is null) return;

        var scale = XamlRoot.RasterizationScale;
        var captionWidth = _window.IsLyricsFullscreen
            ? 0
            : Math.Max(0, _window.AppWindow.TitleBar.RightInset / scale);

        MvTitleBar.ApplyTemplate();

        if (FindTemplatePart<Grid>(MvTitleBar, "PART_LayoutRoot") is { ColumnDefinitions.Count: > 11 } layout)
        {
            layout.ColumnDefinitions[11].Width = new GridLength(captionWidth);
        }

        // 标题要压着两边对称，右边那份也得让开 caption。
        HeadingHost.Margin = new Thickness(Math.Max(64, captionWidth + 56), 0, Math.Max(64, captionWidth + 56), 0);
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

    private void OnFitOptionClick(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is MvFitOption option)
        {
            SetFitMode(option.Mode);
        }
    }

    private void SetFitMode(MvFitMode mode)
    {
        _viewModel.FitMode = mode;
        UpdateVideoRect();
    }

    /// <summary>
    /// 按当前缩放方式算出画面矩形。
    /// </summary>
    /// <remarks>
    /// 元素统一用 <c>Stretch=Fill</c> 填满这个矩形。除了「拉伸填满」，算出来的矩形本身
    /// 就带着画面的宽高比，所以 Fill 不会造成变形 —— 四个模式于是都只由这一个矩形决定，
    /// 不必依赖 <c>Uniform</c> / <c>UniformToFill</c> 那种「裁切方向随比例翻转」的语义。
    /// </remarks>
    private void UpdateVideoRect()
    {
        var width = VideoHost.ActualWidth;
        var height = VideoHost.ActualHeight;

        if (width <= 0 || height <= 0)
        {
            return;
        }

        VideoHost.Clip = new RectangleGeometry { Rect = new Rect(0, 0, width, height) };

        var aspect = _viewModel.NaturalAspect;

        if (aspect <= 0)
        {
            // 解码器还没报出尺寸：让控件自己按 Uniform 摆着，别拿未知比例铺满。
            if (_diagnostics)
            {
                _logger.LogInformation(
                    "MV 画面矩形：比例未知，退回 Uniform（宿主 {HostW:F0}×{HostH:F0}）", width, height);
            }

            VideoSurface.Width = double.NaN;
            VideoSurface.Height = double.NaN;
            VideoSurface.Stretch = Stretch.Uniform;
            return;
        }

        VideoSurface.Stretch = Stretch.Fill;

        if (_diagnostics)
        {
            _logger.LogInformation(
                "MV 画面矩形：模式 {Mode}，宿主 {HostW:F0}×{HostH:F0}，比例 {Aspect:F4}",
                _viewModel.FitMode, width, height, aspect);
        }

        switch (_viewModel.FitMode)
        {
            case MvFitMode.FitWidth:
                VideoSurface.Width = width;
                VideoSurface.Height = width / aspect;
                break;

            case MvFitMode.FitHeight:
                VideoSurface.Width = height * aspect;
                VideoSurface.Height = height;
                break;

            case MvFitMode.Stretch:
                VideoSurface.Width = width;
                VideoSurface.Height = height;
                break;

            default:
                // 适应屏幕：整幅都在，差的那一边留边。
                if (width / height > aspect)
                {
                    VideoSurface.Height = height;
                    VideoSurface.Width = height * aspect;
                }
                else
                {
                    VideoSurface.Width = width;
                    VideoSurface.Height = width / aspect;
                }

                break;
        }
    }

    private void UpdateTimes()
    {
        if (ProgressTimeText is null)
        {
            return;
        }

        ProgressTimeText.Text =
            $"{Formats.Seconds(_viewModel.PositionSeconds)} / {Formats.Seconds(_viewModel.DurationSeconds)}";
    }

    // ── 进度 ────────────────────────────────────────────────────────────────

    private void OnSeekPressed(object sender, PointerRoutedEventArgs args)
    {
        if (!args.GetCurrentPoint(PositionSlider).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _viewModel.IsSeeking = true;
    }

    private void OnSeekReleased(object sender, PointerRoutedEventArgs args)
    {
        if (!_viewModel.IsSeeking)
        {
            return;
        }

        _viewModel.IsSeeking = false;
        _viewModel.Seek(PositionSlider.Value);
        UpdateTimes();
    }

    private void OnSeekKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Home or VirtualKey.End)
        {
            _viewModel.IsSeeking = true;
        }
    }

    private void OnSeekKeyUp(object sender, KeyRoutedEventArgs args)
    {
        if (!_viewModel.IsSeeking)
        {
            return;
        }

        _viewModel.IsSeeking = false;
        _viewModel.Seek(PositionSlider.Value);
        UpdateTimes();
    }

    // ── 命令 ────────────────────────────────────────────────────────────────

    private void OnPlayPauseClick(object sender, RoutedEventArgs args) => _viewModel.TogglePlayPause();

    // ── 全屏 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 取图标路径文本。只有需要由代码切图标的地方（全屏两态）才用它。
    /// </summary>
    /// <remarks>
    /// 与 <c>LyricsPage</c> 里同名的那份一致：资源字典存的是字符串而不是 Geometry，
    /// 见 <see cref="Controls.IconGeometry"/>。路径与主题无关，颜色走 Foreground 继承。
    /// </remarks>
    private static string ResourceIcon(string key)
        => Application.Current is { } app && app.Resources.TryGetValue(key, out var value)
            ? value as string ?? ""
            : "";

    private void SyncFullscreen()
    {
        var fullscreen = _window.IsLyricsFullscreen;
        FullscreenIcon.Data = ResourceIcon(fullscreen ? "IconExitFullScreen" : "IconFullScreen");
        ToolTipService.SetToolTip(FullscreenButton, fullscreen ? "退出全屏 (F11)" : "进入全屏 (F11)");

        // 全屏时没有 caption 按钮，那一列要让出来。
        UpdateChromeInsets();
    }

    private async Task ToggleFullscreenAsync()
    {
        FullscreenButton.IsEnabled = false;
        try
        {
            await _window.ToggleImmersiveFullscreenAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "切换 MV 全屏失败");
        }
        finally
        {
            FullscreenButton.IsEnabled = true;
        }

        SyncFullscreen();
    }

    private async void OnFullscreenClick(object sender, RoutedEventArgs args) => await ToggleFullscreenAsync();

    private async void OnFullscreenInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await ToggleFullscreenAsync();
    }

    private void OnSwitchToAudioClick(object sender, RoutedEventArgs args)
    {
        _viewModel.SwitchToAudio();
        _window.GoBack();
    }

    /// <summary>
    /// 左上那颗向下箭头：一路退出沉浸，回到进入沉浸之前那个常规页。
    /// </summary>
    /// <remarks>
    /// 不退到歌词页 —— 那和同页的「只听歌」是同一件事，两个按钮重复。
    /// 见 <see cref="MainWindow.ExitImmersiveToShell"/>。
    /// </remarks>
    private void OnBackClick(object sender, RoutedEventArgs args) => _window.ExitImmersiveToShell();

    private void OnPlayPauseInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (PositionSlider.FocusState == FocusState.Keyboard)
        {
            return;
        }

        _viewModel.TogglePlayPause();
        args.Handled = true;
    }

    private async void OnEscapeInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;

        // 与歌词页一致：全屏时 Esc 先退全屏，不直接离开这一页。
        if (_window.IsLyricsFullscreen)
        {
            await ToggleFullscreenAsync();
            return;
        }

        _window.ExitImmersiveToShell();
    }
}
