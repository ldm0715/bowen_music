using System.ComponentModel;
using Bowen.Core.Media;
using Bowen.Core.Models;
using Bowen.Core.Services;
using Bowen.WinUI.Controls;
using Bowen.WinUI.Media;
using Bowen.WinUI.Services;
using Bowen.WinUI.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Storage.Pickers;

namespace Bowen.WinUI.Views;

/// <summary>
/// 「设置」页。从标题栏那颗齿轮进入的根页。
/// </summary>
/// <remarks>
/// <para>
/// 页内分「外观 / 播放 / 歌词 / 快捷键 / 存储 / 关于」六类，见 <see cref="SettingsViewModel"/>
/// 与 <c>docs/settings.md</c>。分类切换只在页内换 <c>Visibility</c>，不走导航栈。
/// </para>
/// <para>
/// <b>订阅与退订</b>：本页有若干「下拉框要跟着别处的改动走」的场景（标题栏改主题、
/// 播放条改播放模式、歌词条自己的面板改外观）。订阅那几个单例、在离开时摘掉，与
/// <c>LyricsPage</c> / <c>SearchPage</c> 是同一套写法；不订阅的话临时 ViewModel 会
/// 被单例的事件表一直按住。
/// </para>
/// <para>
/// <b>译文与音质不在订阅之列</b>：它们是「默认值」，只由本页改写，别处动不了，
/// 也就没有需要跟随的外部变化。列表显示同理。
/// </para>
/// </remarks>
public sealed partial class SettingsPage : Page, INavigationAware
{
    /// <summary>
    /// 分类栏的两档宽度与收窄阈值。运行时从 <c>Themes/Tokens.xaml</c> 读，
    /// XAML 里那个字面量只是给解析器用的初值（列宽要 <c>GridLength</c>，转不了 <c>x:Double</c>）。
    /// </summary>
    private readonly double _railWidth = Token("SizeSettingsRailWidth");

    private readonly double _railCompactWidth = Token("SizeSettingsRailCompactWidth");
    private readonly double _railCompactThreshold = Token("SizeSettingsRailCompactThreshold");

    private bool _railCompact;
    private bool _subscribed;

    private readonly NotificationViewModel _notifications;
    private readonly BackgroundImageStore _backgroundImages;
    private readonly IWindowHandleProvider _window;
    private readonly ILogger<SettingsPage> _logger;
    private bool _syncingShellImage;
    private bool _shellModeReady;

    public SettingsPage(
        SettingsViewModel viewModel,
        NotificationViewModel notifications,
        BackgroundImageStore backgroundImages,
        IWindowHandleProvider window,
        ILogger<SettingsPage> logger)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(backgroundImages);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(logger);

        ViewModel = viewModel;
        _notifications = notifications;
        _backgroundImages = backgroundImages;
        _window = window;
        _logger = logger;

        InitializeComponent();

        ShellMicaItem.IsEnabled = MainWindow.IsMicaSupported;

        if (!MainWindow.IsMicaSupported)
        {
            ShellMicaItem.Content = "Mica（需要 Windows 11）";
        }
    }

    public SettingsViewModel ViewModel { get; }

    private void OnCategoryMotionChanged(object sender, SelectionChangedEventArgs args)
        => DispatcherQueue.TryEnqueue(() => Controls.Motion.AnimateTabs(this, 0));

    public void OnNavigatedTo()
    {
        Subscribe();
        SyncShellBackgroundTheme();
        SyncShellImageControls();
        _shellModeReady = true;
        _ = ViewModel.RefreshAsync();
    }

    public void OnNavigatedFrom() => Unsubscribe();

    /// <summary>
    /// 让纯色下拉框跟上设置页的实际主题。
    /// </summary>
    /// <remarks>
    /// 外壳 ViewModel 在主窗口构造时可能还没解析出实际主题（尤其「跟随系统」），
    /// 只靠那里的初始化会留下深色主题的预设列表。设置页自己的 <see cref="FrameworkElement.ActualTheme"/>
    /// 在导航进来时已经确定，这里以它为准再同步一次。
    /// </remarks>
    private void SyncShellBackgroundTheme()
        => ViewModel.ShellBackground.UpdateTheme(
            ActualTheme == ElementTheme.Dark ? AmbientTheme.Dark : AmbientTheme.Light);

    private void OnActualThemeChanged(FrameworkElement sender, object args)
        => SyncShellBackgroundTheme();

    private void OnShellBackgroundModeChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_shellModeReady && ShellBackgroundCombo.SelectedIndex == 2 && !ViewModel.ShellBackground.HasImage)
        {
            DispatcherQueue.TryEnqueue(async () =>
            {
                if (!await PickShellImageAsync())
                {
                    ViewModel.ShellBackground.ModeIndex = 0;
                }
            });
        }

        SyncShellImageControls();
    }

    private async void OnPickShellImageClick(object sender, RoutedEventArgs args)
        => await PickShellImageAsync();

    private async void OnReCropShellImageClick(object sender, RoutedEventArgs args)
    {
        var path = ViewModel.ShellBackground.ImageFilePath;
        if (path.Length == 0 || !File.Exists(path)) return;

        try
        {
            var crop = await BackgroundCropDialog.ShowAsync(
                path,
                BackgroundAspect,
                ViewModel.ShellBackground.ImageFocusX,
                ViewModel.ShellBackground.ImageFocusY,
                ViewModel.ShellBackground.ImageZoom,
                XamlRoot,
                ActualTheme,
                _logger);

            if (crop is { } value)
            {
                ViewModel.ShellBackground.SetImageCrop(value.FocusX, value.FocusY, value.Zoom);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "重新裁剪外壳背景图片失败");
        }
    }

    private void OnShellImageDimChanged(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (_syncingShellImage) return;

        ShellImageDimValue.Text = $"{(int)Math.Round(args.NewValue)}%";
        ViewModel.ShellBackground.SetImageDim(args.NewValue / 100);
    }

    private void OnShellImageBlurChanged(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (_syncingShellImage) return;

        ShellImageBlurValue.Text = ((int)Math.Round(args.NewValue)).ToString();
        ViewModel.ShellBackground.SetImageBlur(args.NewValue);
    }

    /// <summary>选图、导入、裁剪并写回设置。返回假表示用户取消或导入失败。</summary>
    private async Task<bool> PickShellImageAsync()
    {
        try
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".bmp");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _window.WindowHandle);

            var file = await picker.PickSingleFileAsync();
            if (file is null) return false;

            var fileName = await _backgroundImages.ImportAsync(file, "shell-bg");
            if (fileName is null) return false;

            var dim = await _backgroundImages.EstimateDimAsync(
                file, ShellBackgroundSettings.MaximumImageDim, ShellBackgroundSettings.DefaultImageDim);

            var crop = await BackgroundCropDialog.ShowAsync(
                AppPaths.BackgroundFile(fileName),
                BackgroundAspect,
                0.5,
                0.5,
                1,
                XamlRoot,
                ActualTheme,
                _logger);

            if (crop is not { } value)
            {
                // 取消裁剪就不使用这张图，别在背景目录里留下孤儿文件。
                _backgroundImages.Delete(fileName);
                return false;
            }

            var previous = ViewModel.ShellBackground.ImageFile;
            ViewModel.ShellBackground.SetImage(
                fileName,
                dim,
                ViewModel.ShellBackground.ImageBlur,
                value.FocusX,
                value.FocusY,
                value.Zoom);

            if (previous.Length > 0 && !string.Equals(previous, fileName, StringComparison.Ordinal))
            {
                _backgroundImages.Delete(previous);
            }

            SyncShellImageControls();
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "选择外壳背景图片失败");
            return false;
        }
    }

    private void SyncShellImageControls()
    {
        if (ShellImageDimSlider is null) return;

        _syncingShellImage = true;
        var percent = Math.Round(ViewModel.ShellBackground.ImageDim * 100);
        ShellImageDimSlider.Value = percent;
        ShellImageDimValue.Text = $"{(int)percent}%";
        ShellImageBlurSlider.Value = Math.Round(ViewModel.ShellBackground.ImageBlur);
        ShellImageBlurValue.Text = ((int)Math.Round(ViewModel.ShellBackground.ImageBlur)).ToString();
        _syncingShellImage = false;
    }

    /// <summary>外壳背景的宽高比。窗口尺寸拿不到时退回 16:9。</summary>
    private double BackgroundAspect
    {
        get
        {
            var size = XamlRoot?.Size ?? default;
            return size.Width > 1 && size.Height > 1 ? size.Width / size.Height : 16.0 / 9;
        }
    }

    private static double Token(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) && value is double number ? number : 0d;

    private void Subscribe()
    {
        if (_subscribed)
        {
            return;
        }

        _subscribed = true;
        ViewModel.Theme.PropertyChanged += OnThemeChanged;
        ViewModel.Player.PropertyChanged += OnPlayerChanged;
        ViewModel.DesktopLyrics.PropertyChanged += OnDesktopLyricsChanged;
        ActualThemeChanged += OnActualThemeChanged;
    }

    private void Unsubscribe()
    {
        if (!_subscribed)
        {
            return;
        }

        _subscribed = false;
        ViewModel.Theme.PropertyChanged -= OnThemeChanged;
        ViewModel.Player.PropertyChanged -= OnPlayerChanged;
        ViewModel.DesktopLyrics.PropertyChanged -= OnDesktopLyricsChanged;
        ActualThemeChanged -= OnActualThemeChanged;
    }

    private void OnThemeChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ThemeViewModel.Current))
        {
            ViewModel.SyncFromSources();
        }
    }

    /// <remarks>
    /// 音质切换期间 <c>PlayerViewModel</c> 会把档位重置、状态文案置成「正在切换音质…」，
    /// 那一段跟着同步会让下拉框来回跳。所以只在可切换（即已稳定）时才对齐。
    /// </remarks>
    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is not (nameof(PlayerViewModel.Mode)
            or nameof(PlayerViewModel.CurrentQuality)
            or nameof(PlayerViewModel.CanChangeQuality)))
        {
            return;
        }

        if (ViewModel.Player.CanChangeQuality)
        {
            ViewModel.SyncFromSources();
        }
    }

    /// <remarks>
    /// 不过滤属性名：除了颜色与对齐要回写进下拉框，<c>IsEnabled</c> 与 <c>DualLine</c> 还决定
    /// 下面几行是否置灰。同步一次是幂等的，不值得为此维护一张属性名清单。
    /// </remarks>
    private void OnDesktopLyricsChanged(object? sender, PropertyChangedEventArgs args)
        => ViewModel.SyncFromSources();

    /// <summary>
    /// 点某一行的手势按钮：弹录制对话框，录到了就改键。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>必须给 <c>XamlRoot</c> 与 <c>RequestedTheme</c>。</b> 代码构造的 ContentDialog 不在
    /// 可视树里，不给 XamlRoot 直接抛；不给主题的话深色模式会弹出一块白板
    /// （与「最近播放」页那个清空确认同一条，见那边的注释）。
    /// </para>
    /// <para>
    /// <c>ShowAsync</c> 在已经有别的对话框开着时会抛，所以整段包了 try。
    /// </para>
    /// </remarks>
    private async void OnRebindShortcutClick(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement { Tag: ShortcutAction action })
        {
            return;
        }

        var content = new ShortcutRecorderContent();
        content.Configure(action, ViewModel.Shortcuts.ConflictOf);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
            Title = "设置快捷键",
            Content = content,
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,

            // 没收到合法手势之前不给点确定，免得录了个冲突手势还点了确定却什么都没发生。
            IsPrimaryButtonEnabled = false,
        };

        content.Completed += (_, _) => dialog.IsPrimaryButtonEnabled = true;

        try
        {
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && content.Key is { } key)
            {
                ViewModel.Shortcuts.TryRebind(action, key, content.Modifiers, out _);
            }
        }
        catch (Exception exception)
        {
            // 最常见的是「已经有一个对话框开着」。按键位没变，提示一句就够，不必打断用户。
            _notifications.Show($"打开改键窗口失败：{exception.Message}", NoticeSeverity.Error);
        }
    }

    /// <summary>清掉存储分区里的某一项。</summary>
    private async void OnClearStorageClick(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement { Tag: StorageItemKind kind })
        {
            return;
        }

        var row = ViewModel.Storage.Rows.FirstOrDefault(item => item.Kind == kind);
        if (row is null)
        {
            return;
        }

        // 清理内部已经吞掉异常并把结果写回行上，这里不必再包一层。
        await ViewModel.Storage.ClearAsync(row);
    }

    /// <summary>「恢复桌面歌词默认外观」。走的是歌词条自己那颗按钮的同一个命令。</summary>
    private void OnResetDesktopLyricsClick(object sender, EventArgs args)
        => ViewModel.DesktopLyrics.ResetCommand.Execute(null);

    /// <summary>
    /// 「检查更新」。弹的是全应用共用的那一个对话框。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>结果不就地显示在这一页上</b>：启动时的自动检查只能靠对话框，页内再存一份结果，
    /// 两处早晚会对不上（这也是原先 AboutViewModel 里那套状态被拆掉的原因）。
    /// </para>
    /// <para>
    /// <b>异常要接住</b>：已经有别的对话框开着时 <c>ShowAsync</c> 会抛，
    /// 静默失败会表现成「点了没反应」。
    /// </para>
    /// </remarks>
    private async void OnCheckUpdateClick(object sender, RoutedEventArgs args)
    {
        try
        {
            await UpdateDialog.ShowAsync(XamlRoot, ActualTheme, ViewModel.Update, checkFirst: true);
        }
        catch (Exception exception)
        {
            _notifications.Show($"打开更新窗口失败：{exception.Message}", NoticeSeverity.Error);
        }
    }

    /// <summary>
    /// 窗口窄下来时把分类栏收成图标轨。**不折行、也不变成横向条** ——
    /// 那种横排页签正是这一页要避开的形态。
    /// </summary>
    private void OnPageSizeChanged(object sender, SizeChangedEventArgs args)
    {
        var compact = args.NewSize.Width < _railCompactThreshold;
        if (compact == _railCompact)
        {
            return;
        }

        _railCompact = compact;
        RailColumn.Width = new GridLength(compact ? _railCompactWidth : _railWidth);
        ViewModel.SetRailCompact(compact);
    }
}
