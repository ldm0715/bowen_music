using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Bodian.Core.Api;
using Bodian.Core.Models;
using Bodian.Core.Services.Abstractions;
using Bodian.WinUI.Controls;
using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
using Bodian.WinUI.Views;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;

namespace Bodian.WinUI;

/// <summary>
/// 主窗口外壳：顶部自定义标题栏、左侧导航、右侧内容区、底部常驻播放条。
/// </summary>
/// <remarks>
/// <para>
/// 宿主是 <see cref="ContentControl"/> 而不是 <c>Frame</c> —— 返回栈要挂回「原来那个页面实例」，
/// 而 <c>Frame.Content</c> 不接受重复挂载同一个实例。理由见 <c>INavigationService</c> 的说明。
/// </para>
/// <para>
/// <b>侧栏高亮跟的是「根页」而不是「当前页」</b>：从「我喜欢的」点进一个歌单详情时，
/// 详情压在栈上、根仍是「我喜欢的」，侧栏就该继续高亮「我喜欢的」。
/// </para>
/// </remarks>
public sealed partial class MainWindow : Window, IPlaylistLibrarySink, IWindowHandleProvider
{
    /// <summary>侧栏项的标记。固定项与紧凑栏那颗歌单图标都靠它分发，见 <see cref="OnItemInvoked"/>。</summary>
    private const string DiscoverTag = "discover";

    private const string BangsTag = "bangs";

    private const string LibraryTag = "library";

    private const string FavoritesTag = "favorites";

    private const string RecentTag = "recent";

    private const string CollectedAlbumsTag = "collected-albums";

    private const string CollectedPlaylistsTag = "collected-playlists";

    /// <summary>紧凑栏里那颗「创建的歌单」图标。点它弹歌单列表，不换页。</summary>
    private const string PlaylistsRailTag = "playlists-rail";

    /// <summary>侧栏里的歌单都是账号自建歌单，<c>source = 5</c>。</summary>
    private const int SidebarPlaylistSource = 5;

    /// <summary>页脚行自身的下边距（模板里 FooterContentBorder 的 Margin 0,0,0,4）。</summary>
    private const double PaneFooterBottomMargin = 4;

    /// <summary>
    /// 段高再让出的余量，见 <see cref="SyncPlaylistSectionHeight"/>。
    /// </summary>
    /// <remarks>
    /// 量的是最后一项容器的 <c>ActualHeight</c>，**它不含该项的下外边距**
    /// （<c>NavigationViewItemButtonMargin</c> 下 2），所以算出来的底边比菜单内容的真实高度
    /// 短几像素。照直写进去，星号行就会比内容矮那几像素 → 菜单区溢出 → 上半截冒出一条滚动条。
    /// 让出这一点余量，代价是面板底部留几像素空白（看不见），换菜单区永不溢出。
    /// </remarks>
    private const double PlaylistSectionSlack = 8;

    /// <summary>
    /// 「创建的歌单」这一段的高度下限（标题 + 两行）。
    /// </summary>
    /// <remarks>
    /// 见 <see cref="SyncPlaylistSectionHeight"/>：窗口压到最小（800×560）时，
    /// 面板里放不下固定项 + 这一段，此时把这一段保到下限，代价是菜单区自己出现滚动条。
    /// 这是唯一的取舍点，改这一个常量就能换。
    /// </remarks>
    private const double MinPlaylistSectionHeight = 132;

    private readonly INavigationService _navigation;
    private readonly IBodianLogin _login;
    private readonly SidebarViewModel _sidebar;
    private readonly IWindowPlacementStore _placement;
    private readonly WindowRenderActivity _renderActivity;
    private readonly Func<Playlist, int, PlaylistDetailPage> _playlistDetailFactory;

    /// <summary>
    /// 上一次算高度时的两个输入。**没有它就会死循环**：见 <see cref="SyncPlaylistSectionHeight"/>。
    /// </summary>
    private double _lastNavHeight = double.NaN;
    private double _lastAnchorBottom = double.NaN;

    /// <summary>收起态下那颗图标开关的浮层是否开着。</summary>
    private bool _playlistsPaneOpen;
    private OverlappedPresenter? _lyricsRestorePresenter;
    private NativeMethods.WindowPlacement _lyricsRestorePlacement;
    private nint _lyricsRestoreStyle;
    private bool _lyricsFullscreen;
    private bool _closed;
    private FrameworkElement? _lyricsTitleBar;
    private bool _immersiveVisible;
    private bool _isChangingLyricsPresenter;
    private bool _isNavigatingBack;
    private bool _lyricsChromeVisible = true;
    private OverlappedPresenter? _hiddenCaptionPresenter;
    private bool _captionRestoreBorder;
    private bool _captionRestoreTitleBar;
    private (bool Dark, Color Foreground)? _captionPalette;

    public MainWindow(
        INavigationService navigation,
        IBodianLogin login,
        PlayerViewModel playerViewModel,
        LyricsViewModel lyricsViewModel,
        DesktopLyricsViewModel desktopLyrics,
        MiniPlayerViewModel miniPlayer,
        PlayQueueViewModel queueViewModel,
        AccountViewModel account,
        SidebarViewModel sidebar,
        SearchViewModel search,
        ThemeViewModel theme,
        IWindowPlacementStore placement,
        Func<Playlist, int, PlaylistDetailPage> playlistDetailFactory,
        Func<Track, MvPage> mvFactory,
        TrackActionsService trackActions,
        NotificationViewModel notifications)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(login);
        ArgumentNullException.ThrowIfNull(playerViewModel);
        ArgumentNullException.ThrowIfNull(lyricsViewModel);
        ArgumentNullException.ThrowIfNull(desktopLyrics);
        ArgumentNullException.ThrowIfNull(miniPlayer);
        ArgumentNullException.ThrowIfNull(queueViewModel);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(sidebar);
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(playlistDetailFactory);
        ArgumentNullException.ThrowIfNull(trackActions);
        ArgumentNullException.ThrowIfNull(notifications);

        _navigation = navigation;
        _login = login;
        _sidebar = sidebar;
        _placement = placement;
        _playlistDetailFactory = playlistDetailFactory;

        Player = playerViewModel;
        Notifications = notifications;
        Queue = queueViewModel;
        MiniPlayer = miniPlayer;
        Account = account;
        Search = search;
        Theme = theme;

        InitializeComponent();
        SearchBox.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnSearchPointerPressed), true);
        ShellRoot.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnShellPointerPressed), true);
        ShellRoot.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnShellKeyDown), true);
        ShellRoot.GotFocus += OnShellGotFocus;
        ShellRoot.SizeChanged += (_, _) =>
        {
            if (SearchPanel.Visibility == Visibility.Visible) PositionSearchPanel();
            QueuePane.Width = Math.Max(0, Math.Min(QueuePaneWidth, ShellRoot.ActualWidth - 32));
        };
        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated) DismissSearchUi();
        };

        // 先同步保存的档位，再订阅选择事件，避免初始化时把设置改成第一个选项。
        SyncThemeSelection();
        ThemeOptions.SelectionChanged += OnThemeSelectionChanged;

        MiniPlayer.PropertyChanged += OnMiniPlayerPropertyChanged;
        UpdateMiniPlayerButtonState();

        // 曲目列表靠这个知道「哪一行在播」。
        //
        // ★ 为什么走 App 资源而不是给 6 个页面各传一份：TrackListView 是 XAML 实例化的，
        //   构造函数必须无参，拿不到 DI 容器。显式传就要改 6 份 XAML + 6 个 ViewModel，
        //   而这里放一份引用就够了。控件侧仍保留一个 NowPlaying 依赖属性，
        //   显式指定时优先 —— 见 TrackListView.NowPlaying 的说明。
        Application.Current.Resources["BodianNowPlaying"] = playerViewModel;

        // 曲目行「更多」菜单的装配点。同一个理由：行内控件是 XAML 实例化的，
        // 构造函数拿不到容器，显式传就要改 9 份宿主 XAML 与它们的 ViewModel。
        Application.Current.Resources["BodianTrackActions"] = trackActions;

        ApplyWindowPlacement();
        ConfigureTitleBar();
        _renderActivity = new WindowRenderActivity(WinRT.Interop.WindowNative.GetWindowHandle(this), DispatcherQueue,
            Application.Current.Resources["BodianLoggerFactory"] as ILoggerFactory);
        _renderActivity.Changed += (_, _) => RenderingStateChanged?.Invoke(this, EventArgs.Empty);
        _renderActivity.InteractionChanged += (_, _) =>
        {
            WindowViewport.IsInteractive = _renderActivity.IsInteractive;
            if (!_renderActivity.IsInteractive)
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (!IsMinimized && !_immersiveVisible) AppTitleBar.RecomputeDragRegions();
                });
        };
        Closed += (_, _) =>
        {
            _closed = true;
            MiniPlayer.PropertyChanged -= OnMiniPlayerPropertyChanged;
            _renderActivity.Dispose();
            SaveWindowPlacement();
        };

        // 歌词页与 MV 页都是全窗沉浸，进 ImmersiveHost；其余进常规的 PageHost。
        _navigation.Attach(PageHost, page => page is LyricsPage or MvPage ? ImmersiveHost : PageHost);
        _navigation.Navigated += OnNavigated;

        var playerBar = new PlayerBar(playerViewModel, lyricsViewModel, desktopLyrics, navigation);
        playerBar.PlaylistRequested += (_, _) => ToggleQueue();

        // MV 页要带曲目构造，工厂只有这里拿得到 —— 播放条只抛事件。
        playerBar.MvRequested += (_, track) => navigation.Navigate(mvFactory(track));

        // 第二行的歌手名与专辑名。歌手不能走页面工厂 —— 合唱曲目要先解析出全部歌手、
        // 多位就弹选择框，那套降级逻辑在 TrackActionsViewModel 里（与曲目行「查看歌手」同一份）。
        playerBar.ArtistRequested += async (_, track) => await ArtistPickerDialog.ShowPickerAsync(
            trackActions.Create(track), ShellRoot.XamlRoot, ShellRoot.ActualTheme);
        playerBar.AlbumRequested += (_, track) => trackActions.Create(track).OpenAlbum();
        PlayerHost.Content = playerBar;

        // 抽屉的滑入用 Translation 独立于布局（与歌词页的评论面板同一套），先打开这个通道。
        ElementCompositionPreview.SetIsTranslationEnabled(QueuePane, true);

        _login.AccountChanged += OnAccountChanged;
        PageHost.Loaded += OnHostLoaded;

        // 收起 / 展开侧栏时，「创建的歌单」在「面板里的列表」与「轨上一颗图标」之间切换。
        Nav.RegisterPropertyChangedCallback(
            NavigationView.IsPaneOpenProperty, (_, _) => UpdateSidebarPaneMode());

        // 这一段的高度跟着窗口尺寸与菜单内容走。挂在 LayoutUpdated 而不是 SizeChanged：
        // 菜单项的高度也会变（字体、DPI、主题），都从这一条通道过；方法内部有收敛判据。
        Nav.LayoutUpdated += (_, _) => SyncPlaylistSectionHeight();
    }

    /// <summary>给 <c>x:Bind</c> 用。</summary>
    public PlayerViewModel Player { get; }

    /// <summary>播放条上方那条浮层通知。全应用唯一的短提示出口。</summary>
    public NotificationViewModel Notifications { get; }

    /// <summary>右侧播放队列抽屉。绑在 <c>QueueOverlay</c> 的显隐上。</summary>
    public PlayQueueViewModel Queue { get; }

    /// <summary>标题栏那颗小窗按钮的开关。</summary>
    public MiniPlayerViewModel MiniPlayer { get; }

    /// <summary>标题栏账号入口的数据源。</summary>
    public AccountViewModel Account { get; }

    /// <summary>顶部搜索框的数据源。<b>与搜索页是同一个实例</b>，所以框里的词与结果永远一致。</summary>
    public SearchViewModel Search { get; }

    /// <summary>外观切换。绑在根 <c>Grid</c> 的 <c>RequestedTheme</c> 上。</summary>
    public ThemeViewModel Theme { get; }

    // Window.Content 是外层 ResponsiveViewport；实际应用主题设置在 ShellRoot。
    internal FrameworkElement ThemeRoot => ShellRoot;

    /// <summary>替换系统标题栏；不改变窗口的尺寸、位置或保存的窗口矩形。</summary>
    private void ConfigureTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppTitleBar.AutoRefreshDragRegions = false;
        SetTitleBar(AppTitleBar);

        ShellRoot.ActualThemeChanged += (_, _) => UpdateCaptionButtonColors();
        AppTitleBar.RegisterPropertyChangedCallback(Control.ForegroundProperty, (_, _) => UpdateCaptionButtonColors());
        UpdateCaptionButtonColors();
    }

    /// <summary>原生窗口按钮共用透明背景，并随实际主题更新前景色。</summary>
    private void UpdateCaptionButtonColors()
    {
        var titleBar = AppWindow.TitleBar;
        var isDark = _immersiveVisible || ShellRoot.ActualTheme == ElementTheme.Dark;
        var foreground = _immersiveVisible ? Colors.White : AppTitleBar.Foreground is SolidColorBrush brush
            ? brush.Color
            : isDark ? Colors.White : Colors.Black;
        if (_captionPalette is { } palette && palette.Dark == isDark && palette.Foreground == foreground) return;
        _captionPalette = (isDark, foreground);
        var hoverBackground = isDark
            ? Color.FromArgb(24, 255, 255, 255)
            : Color.FromArgb(16, 0, 0, 0);

        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonInactiveForegroundColor = isDark
            ? Color.FromArgb(153, 255, 255, 255)
            : Color.FromArgb(153, 0, 0, 0);
        titleBar.ButtonHoverBackgroundColor = hoverBackground;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedBackgroundColor = hoverBackground;
        titleBar.ButtonPressedForegroundColor = foreground;
    }

    private void OnThemeFlyoutOpening(object sender, object args) => SyncThemeSelection();

    /// <summary>
    /// 账号下拉框打开时补拉统计。
    /// </summary>
    /// <remarks>
    /// <b>不 await</b>：Opening 是同步事件，这里等一个网络往返会把下拉框卡住。
    /// 异常由 <see cref="AccountViewModel.RefreshStatsAsync"/> 自己吞掉 ——
    /// 漏出去会变成 UnobservedTaskException，被 App 记成 Critical 日志。
    /// </remarks>
    private void OnAccountFlyoutOpening(object sender, object args) => _ = Account.RefreshStatsAsync();

    private void SyncThemeSelection() => ThemeOptions.SelectedItem = Theme.Current switch
    {
        AppTheme.Light => LightThemeOption,
        AppTheme.Dark => DarkThemeOption,
        _ => SystemThemeOption,
    };

    private void OnMiniPlayerClick(object sender, RoutedEventArgs e)
        => MiniPlayer.ToggleCommand.Execute(null);

    private void OnMiniPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MiniPlayerViewModel.IsEnabled))
        {
            UpdateMiniPlayerButtonState();
        }
    }

    /// <remarks>
    /// 激活态的着色走 XAML 里的 VisualState，不在这里直接设画刷 ——
    /// 直接设会用一次 <c>ThemeResource</c> 的结果永久盖住，切主题后前景不跟着变。
    /// </remarks>
    private void UpdateMiniPlayerButtonState()
        => VisualStateManager.GoToState(
            MiniPlayerButton, MiniPlayer.IsEnabled ? "MiniPlayerActive" : "MiniPlayerInactive", false);

    private void OnThemeSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (ThemeOptions.SelectedItem is not ListViewItem selected)
        {
            return;
        }

        Theme.Select(selected == LightThemeOption
            ? AppTheme.Light
            : selected == DarkThemeOption ? AppTheme.Dark : AppTheme.System);
    }

    /// <summary>
    /// 首次运行（或记录不可用）时的默认窗口尺寸，单位是**逻辑像素**。
    /// </summary>
    /// <remarks>
    /// 与参照项目 <c>LyciaMusic</c> 的 <c>src-tauri/tauri.conf.json</c> 对齐（1200×800、居中）。
    /// Tauri 配的也是逻辑像素，所以两边概念一致，数值可以直接抄。
    /// </remarks>
    private const int DefaultWidthDip = 1200;

    private const int DefaultHeightDip = 800;

    /// <summary>
    /// 摆窗口：有记录就还原，没有就默认尺寸 + 居中。顺带设最小尺寸。
    /// </summary>
    /// <remarks>
    /// <b>最小尺寸不是可选的润色。</b> 播放条是三栏布局，窗口窄到一定程度后中间那栏会被压得
    /// 比按钮还窄，表现是「播放按钮的图标显示不全」——控件没坏，是被裁了。
    /// 下限大约 56(封面) + 220(信息) + 控制区 + 200(音质音量) + 间距，取 800。
    /// </remarks>
    private void ApplyWindowPlacement()
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter)
        {
            return;
        }

        presenter.PreferredMinimumWidth = 800;
        presenter.PreferredMinimumHeight = 560;

        // 有记录、且那块屏幕还在，就原样还原。
        if (_placement.Load() is { } saved && IsOnSomeDisplay(saved))
        {
            AppWindow.MoveAndResize(new RectInt32(saved.X, saved.Y, saved.Width, saved.Height));

            return;
        }

        CenterAtDefaultSize();
    }

    /// <summary>
    /// 记录里的坐标还在某块屏幕上吗。
    /// </summary>
    /// <remarks>
    /// <b>这一步不能省。</b> 上次把窗口放在副屏上、这次副屏拔了，直接还原会把窗口丢到
    /// 看不见的地方 —— 用户看到的是「应用打不开」。
    /// </remarks>
    private bool IsOnSomeDisplay(WindowPlacement placement)
    {
        var rect = new RectInt32(placement.X, placement.Y, placement.Width, placement.Height);

        // Fallback.None：不在任何屏幕上就返回 null，而不是硬塞给主屏。
        return DisplayArea.GetFromRect(rect, DisplayAreaFallback.None) is not null;
    }

    /// <summary>默认尺寸 + 居中。</summary>
    private void CenterAtDefaultSize()
    {
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;

        // AppWindow 收的是**物理像素**，而默认尺寸是按逻辑像素定的 —— 不换算的话，
        // 125% 缩放下窗口会比预期小一圈。
        var scale = NativeMethods.GetDpiForWindow(
            Win32Interop.GetWindowFromWindowId(AppWindow.Id)) / 96.0;

        // 也别超出工作区（小屏或缩放很大的机器上会）。
        var width = Math.Min((int)Math.Round(DefaultWidthDip * scale), work.Width);
        var height = Math.Min((int)Math.Round(DefaultHeightDip * scale), work.Height);

        AppWindow.MoveAndResize(new RectInt32(
            work.X + ((work.Width - width) / 2),
            work.Y + ((work.Height - height) / 2),
            width,
            height));
    }

    /// <summary>
    /// 关闭时记下窗口位置。
    /// </summary>
    /// <remarks>
    /// <b>最大化状态下不记。</b> 那时记录的是最大化后的尺寸，下次还原会得到一个
    /// 铺满屏幕、且无法再放大的窗口。保留上一次普通状态的记录才有意义。
    /// </remarks>
    private void SaveWindowPlacement()
    {
        if (IsLyricsFullscreen)
        {
            return;
        }

        if (AppWindow.Presenter is OverlappedPresenter { State: not OverlappedPresenterState.Restored })
        {
            return;
        }

        _placement.Save(new WindowPlacement(
            AppWindow.Position.X,
            AppWindow.Position.Y,
            AppWindow.Size.Width,
            AppWindow.Size.Height));
    }

    /// <summary>
    /// 第一次布局完成后决定去哪一页。
    /// </summary>
    /// <remarks>
    /// <b>这里不拉侧栏。</b> 恢复会话会同步触发 <see cref="OnAccountChanged"/>，
    /// 而侧栏（放出来 + 拉歌单）是那件事的职责 —— 两边各拉一次的话，
    /// 每次启动都会把 <c>userCreate</c> 发两遍（实测日志里能直接看到同一秒两条「自建歌单 0 个」）。
    /// 所以这里只决定「落在哪一页」：恢复成功去我喜欢的，否则回登录页。
    /// </remarks>
    private void OnHostLoaded(object sender, RoutedEventArgs e)
    {
        PageHost.Loaded -= OnHostLoaded;

        if (_login.TryRestorePersistedSession())
        {
            _navigation.NavigateRoot<FavoritesPage>();
        }
        else
        {
            ShowLogin();
        }
    }

    /// <summary>
    /// 会话没了（用户登出，或服务端返回 11012 把它清掉）就回登录页。
    /// </summary>
    /// <remarks>
    /// <b>登录成功也会触发这个事件</b>，但那时 <c>IsAuthenticated</c> 为真：不在这里导航
    /// （由登录页自己切页，避免两处同时导航），只负责把侧栏的数据换成新账号的。
    /// </remarks>
    private void OnAccountChanged(object? sender, EventArgs e)
    {
        if (!_login.IsAuthenticated)
        {
            ShowLogin();
            return;
        }

        ShowShell();

        // 换号之后「创建的歌单」是另一份：先清空再重拉。
        // 清空不能只指望 LoadAsync —— 它现在失败时不再动列表（见 SidebarViewModel.LoadAsync），
        // 少这一步，重拉失败就会把上一个账号的歌单留给新账号看。
        _sidebar.Reset();
        _ = LoadSidebarAsync();
    }

    private void OnSearchGotFocus(object sender, RoutedEventArgs args)
    {
        // 禁用返回按钮、卸载页面或 Esc 收起浮窗都可能迁移焦点；只有键盘主动切入才展开。
        if (_isNavigatingBack
            || FocusManager.GetFocusedElement(ShellRoot.XamlRoot) is not Control { FocusState: FocusState.Keyboard } focused
            || !IsWithin(focused, SearchBox)) return;
        ShowSearchPanel();
    }

    private void OnSearchPointerPressed(object sender, PointerRoutedEventArgs args) => ShowSearchPanel();

    private void ShowSearchPanel()
    {
        if (!string.IsNullOrWhiteSpace(SearchBox.Text) || SearchBarHost.Visibility != Visibility.Visible) return;
        Search.ClearSuggestions();
        SearchBox.IsSuggestionListOpen = false;
        PositionSearchPanel();
        // 不调用 Focus、不创建 light-dismiss 遮罩；输入继续留在搜索框。
        SearchPanel.Visibility = Visibility.Visible;
        _ = Search.EnsureHotWordsAsync();
    }

    private void PositionSearchPanel()
    {
        var anchor = SearchBox.TransformToVisual(ShellRoot).TransformPoint(new Windows.Foundation.Point(0, SearchBox.ActualHeight));
        SearchPanel.Width = SearchBox.ActualWidth;
        SearchPanel.MaxHeight = Math.Max(100, ShellRoot.ActualHeight - anchor.Y - 12);
        SearchPanel.Margin = new Thickness(anchor.X, anchor.Y + 6, 0, 0);
    }

    private static bool IsWithin(DependencyObject? element, DependencyObject ancestor)
    {
        while (element is not null)
        {
            if (ReferenceEquals(element, ancestor)) return true;
            element = VisualTreeHelper.GetParent(element);
        }
        return false;
    }

    private void OnShellPointerPressed(object sender, PointerRoutedEventArgs args)
    {
        var source = args.OriginalSource as DependencyObject;
        if (IsWithin(source, SearchBox) || IsWithin(source, SearchPanel)) return;
        // 只关闭，不吞掉点击；侧栏、主题等控件仍响应这一次点击。
        DismissSearchUi();
    }

    private void OnShellGotFocus(object sender, RoutedEventArgs args)
    {
        var focused = FocusManager.GetFocusedElement(ShellRoot.XamlRoot) as DependencyObject;
        if (!IsWithin(focused, SearchBox) && !IsWithin(focused, SearchPanel)) DismissSearchUi();
    }

    private void OnShellKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != Windows.System.VirtualKey.Escape) return;

        // 歌单浮层压在整个第 1 行上（含播放队列抽屉的左半边），Esc 先收它。
        if (_playlistsPaneOpen)
        {
            HidePlaylistsPane();
            args.Handled = true;
            return;
        }

        // 抽屉盖在最上面，Esc 先收它。
        if (Queue.IsOpen)
        {
            CloseQueue();
            args.Handled = true;
            return;
        }

        if (SearchPanel.Visibility != Visibility.Visible && !SearchBox.IsSuggestionListOpen) return;
        DismissSearchUi();
        SearchBox.Focus(FocusState.Programmatic);
        args.Handled = true;
    }

    /// <summary>抽屉的宽度上限。窄窗口下会被 <c>SizeChanged</c> 压到窗口内。</summary>
    private const double QueuePaneWidth = 380;

    /// <summary>
    /// 常规态下通知条距**内容区**底边的空隙。
    /// </summary>
    /// <remarks>
    /// 那底下还有 80 高的播放条（第 2 行），所以通知条实际落在离窗口底 92 处。
    /// 与 XAML 里 <c>NotificationBar</c> 的初始 <c>Margin</c> 是同一个数。
    /// </remarks>
    private const double NotificationBottomGap = 12;

    /// <summary>常规态下两张浮层卡片距**内容区**上沿的空隙。与 XAML 里那两处初始 <c>Margin</c> 同值。</summary>
    private const double OverlayTopGap = 8;

    /// <summary>
    /// 常规态下浮层距**内容区**底边的空隙。
    /// </summary>
    /// <remarks>
    /// 常规态下浮层只跨第 1 行，底下还有 80 高的播放条，碰不到任何东西，留一点边距就够。
    /// </remarks>
    private const double OverlayBottomGap = 12;

    /// <summary>
    /// 沉浸态下浮层距**窗口**底边的距离。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 沉浸页把播放条收进 <c>PlayerHost.Visibility = Collapsed</c>，第 2 行高度归零 ——
    /// 浮层会一路铺到窗口底部，压住歌词页/MV 页自己的进度条与那排传输按钮。
    /// </para>
    /// <para>
    /// <b>取 96 的依据与 <see cref="ImmersiveNotificationInset"/> 同源</b>：歌词页的进度滑块
    /// 上沿在距窗底 91（<c>LyricsPage.xaml</c> 里那段算式），96 落在它上方；MV 页对应位置是 85。
    /// 传输按钮比滑块还低（距窗底 63），所以这一档一并盖住了它。
    /// </para>
    /// </remarks>
    private const double ImmersiveOverlayBottom = 96;

    /// <summary>
    /// 沉浸态下浮层要额外往下让开的高度。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>沉浸页自己的标题栏是一条真正的非客户区拖拽带。</b> <c>LyricsTitleBar</c> / <c>MvTitleBar</c>
    /// 都是 <c>Height="48"</c>、<c>VerticalAlignment="Top"</c>，由 <c>SetTitleBar</c> 交给系统 ——
    /// 那条带子里除了沉浸页左端的几颗按钮，中段是拖拽区，右端是系统的最小化/最大化/关闭。
    /// </para>
    /// <para>
    /// 而沉浸态下外层第 0 行被收起来（<c>AppTitleBar.Visibility = Collapsed</c>）、**行高归零**，
    /// 于是 <c>Grid.Row="1"</c> 的浮层从窗口顶端 8px 处就开始，头部那颗收起按钮正好落进带子里。
    /// </para>
    /// <para>
    /// <b>非客户区是在 <c>WM_NCHITTEST</c> 阶段处理的，先于 XAML 命中测试</b> ——
    /// 所以浮层画得再靠上也没用，那颗按钮收不到点击，表现为「抽屉开出来就关不掉了」。
    /// 只能让浮层让开这条带子。
    /// </para>
    /// <para>
    /// <b>刻意不跟着「沉浸页 chrome 是否可见」变</b>：chrome 藏起来时窗口连边框带标题栏一起被摘掉
    /// （<c>SetLyricsChromeVisible</c> 那条路），理论上不需要让开，但那条路上先前设过的拖拽区
    /// 是否还留着没有把握。多让 48 的代价只是浮层顶上多一段空白，少让的代价是按钮点不动 ——
    /// 两边的代价不对称，取稳的那边。
    /// </para>
    /// </remarks>
    private const double ImmersiveChromeHeight = 48;

    /// <summary>
    /// 沉浸态下通知条距**窗口**底边的距离。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 沉浸页把播放条收进 <c>PlayerHost.Visibility = Collapsed</c>，第 2 行高度归零 ——
    /// 通知条会跟着掉到窗口底部，正好压在歌词页/MV 页自己的传输按钮上。
    /// 所以这一档要按沉浸页自己的底栏量。
    /// </para>
    /// <para>
    /// <b>取 96 的依据</b>：歌词页的进度滑块上沿在距窗底 91（见 <c>LyricsPage.xaml</c> 里那段算式），
    /// MV 页对应位置是 85，96 让两边都落在进度条上方、碰不到按钮。
    /// </para>
    /// <para>
    /// <b>已知代价：会盖住歌词页那条 64 高的频谱。</b> 频谱占距窗底 92~156，
    /// 而进度条上沿 91、频谱下沿 92 —— 中间一像素空档都没有。
    /// 那一页底部就是这么挤，不压按钮就必然压频谱（频谱是装饰，压它比压按钮好些）。
    /// </para>
    /// </remarks>
    private const double ImmersiveNotificationInset = 96;

    /// <summary>开合播放队列抽屉。播放条与歌词页的队列按钮都走这里。</summary>
    public void ToggleQueue()
    {
        if (Queue.IsOpen)
        {
            CloseQueue();
            return;
        }

        Queue.IsOpen = true;

        // Translation 独立于 XAML 布局，只给抽屉一个轻微的滑入动画。收起时不放动画：
        // 元素马上就 Collapsed 了，看不着。
        var visual = ElementCompositionPreview.GetElementVisual(QueuePane);
        using var slide = visual.Compositor.CreateScalarKeyFrameAnimation();
        slide.InsertKeyFrame(0, 28);
        slide.InsertKeyFrame(1, 0);
        slide.Duration = TimeSpan.FromMilliseconds(220);
        visual.StartAnimation("Translation.X", slide);
    }

    private void CloseQueue() => Queue.IsOpen = false;

    private void OnQueueDismissTapped(object sender, TappedRoutedEventArgs args)
    {
        CloseQueue();
        args.Handled = true;
    }

    private void OnQueueCloseRequested(object? sender, EventArgs args) => CloseQueue();

    /// <summary>
    /// 清空队列。
    /// </summary>
    /// <remarks>
    /// 确认框放在窗口而不是 ViewModel：那是一个界面决策（要不要弹、按钮怎么摆），
    /// 而 <c>XamlRoot</c> 也拿不到 ViewModel 里去。做法与 <c>RecentPage</c> 清空播放记录一致。
    /// </remarks>
    private async void OnClearQueueRequested(object? sender, EventArgs args)
    {
        var dialog = CreateAppDialog("清空播放列表？");
        dialog.Content = "只会清掉这一份播放队列，正在播的这首会继续放完。";
        dialog.PrimaryButtonText = "清空";
        dialog.CloseButtonText = "取消";
        dialog.DefaultButton = ContentDialogButton.Close;

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        Queue.Clear();
    }

    private void DismissSearchUi()
    {
        SearchPanel.Visibility = Visibility.Collapsed;
        Search.ClearSuggestions();
        SearchBox.IsSuggestionListOpen = false;
    }

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        Search.Keyword = sender.Text;
        if (string.IsNullOrWhiteSpace(sender.Text)) ShowSearchPanel();
        else
        {
            SearchPanel.Visibility = Visibility.Collapsed;
            _ = Search.UpdateSuggestionsAsync(sender.Text);
        }
    }

    private void OnSearchQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) =>
        SubmitSearch(args.ChosenSuggestion as string ?? args.QueryText);

    private void OnSearchHotWordsRetry(object sender, RoutedEventArgs args) => _ = Search.EnsureHotWordsAsync();

    private void OnSearchHotWordClick(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is SearchHotWord word) SubmitSearch(word.Keyword);
    }

    private void OnSearchHistoryClick(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is string keyword) SubmitSearch(keyword);
    }

    private void OnBackClick(object sender, RoutedEventArgs args) => GoBack();

    /// <summary>
    /// 一路退出沉浸，回到进入沉浸之前那个常规页。MV 页左上那颗向下箭头用它。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>退一层不够。</b> 沉浸页是从常规页压上来的，而 MV 又是从歌词页压上来的 ——
    /// 只退一层会落回歌词页，那还是沉浸页，且和 MV 页上的「只听歌」做的是同一件事。
    /// 所以退到落点不再是沉浸页为止。
    /// </para>
    /// <para>
    /// 从常规页（播放条那颗 MV 按钮）直接进来的情况自然只退一层：落点本来就不是沉浸页。
    /// </para>
    /// </remarks>
    public void ExitImmersiveToShell()
    {
        GoBack();

        while (_navigation.Current is LyricsPage or MvPage && _navigation.CanGoBack)
        {
            GoBack();
        }
    }

    public void GoBack()
    {
        if (_isChangingLyricsPresenter) return;
        if (!_navigation.CanGoBack) return;

        _isNavigatingBack = true;
        try
        {
            DismissSearchUi();
            _navigation.GoBack();
            // 返回到根页时按钮会被禁用，把焦点放回页面，避免落到旁边的搜索框。
            if (_navigation.Current is { } page)
            {
                var target = FocusManager.FindFirstFocusableElement(page) as UIElement ?? page;
                target.Focus(FocusState.Programmatic);
            }
            DismissSearchUi();
        }
        finally
        {
            _isNavigatingBack = false;
        }
    }

    private void SubmitSearch(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return;
        DismissSearchUi();
        Search.Keyword = keyword.Trim();
        Search.SearchCommand.Execute(null);
        _navigation.Navigate<SearchPage>();
    }

    /// <summary>
    /// 回登录页，<b>并把侧栏与搜索框整个藏起来</b>。
    /// </summary>
    /// <remarks>
    /// 登录宿主显示居中弹窗；侧栏留在旁边会露出上一个账号的歌单名 —— 那既是错的信息，
    /// 也是不该在未登录状态出现的信息。搜索框同理：未登录时搜出来也播不了。
    /// </remarks>
    private void ShowLogin()
    {
        Nav.IsPaneVisible = false;
        HidePlaylistsPane();

        // 登出后这段本来就看不见，但列表还留着上一个账号的歌单名 —— 清掉。
        _sidebar.Reset();
        SearchPanel.Visibility = Visibility.Collapsed;
        Search.ClearSuggestions();
        SearchBarHost.Visibility = Visibility.Collapsed;
        AccountButton.Visibility = Visibility.Collapsed;
        _navigation.Reset<LoginPage>();
    }

    /// <summary>登录之后显示侧栏、标题栏搜索框与账号入口。</summary>
    private void ShowShell()
    {
        Nav.IsPaneVisible = true;
        SearchBarHost.Visibility = Visibility.Visible;
        AccountButton.Visibility = Visibility.Visible;
    }

    private async Task LoadSidebarAsync()
    {
        await _sidebar.LoadAsync().ConfigureAwait(true);
        AfterSidebarChanged();
    }

    /// <summary>
    /// 侧栏歌单数据换过之后的收尾：显不显示这一段、高亮还成不成立、高度要不要重算。
    /// </summary>
    /// <remarks>
    /// 列表内容本身不重建：<c>ItemsSource</c> 直接绑 <see cref="SidebarViewModel.Playlists"/>
    /// （<c>ObservableCollection</c>），增删自己会同步。这跟以前「整段重建」的理由不同 ——
    /// 现在没有「项」要建，只有一段的显隐与高度。
    /// </remarks>
    private void AfterSidebarChanged()
    {
        UpdateSidebarPaneMode();

        // 浮层里那份列表没有失败行控件（它复用 PlaylistListView），自己的重试行单独管。
        PaneRetryRow.Visibility = _sidebar.ErrorText is null ? Visibility.Collapsed : Visibility.Visible;
        ToolTipService.SetToolTip(PaneRetryRow, _sidebar.ErrorText);

        // 列表换了，原来那一行可能已经不在里面。
        SyncSelection();

        // 失败行的出现/消失会改变这一段的高度，重算一次。
        SyncPlaylistSectionHeight();
    }

    /// <summary>
    /// 侧栏展开 ↔ 紧凑栏（48px 图标轨）切换时，这一段在「列表」与「一颗图标」之间换形态。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>判据是 <see cref="NavigationView.IsPaneOpen"/>，不是 <c>DisplayMode</c>。</b>
    /// <c>PaneDisplayMode=Left</c> 时 WinUI 的 <c>UpdateAdaptiveLayout</c> 把 <c>DisplayMode</c>
    /// 写死成 <c>Expanded</c>，窗口再窄也不变；真正的收起态是 <c>IsPaneOpen=false</c> +
    /// <c>SplitView</c> 的 ClosedCompact，对应模板里的 <c>ListSizeCompact</c> 视觉状态
    /// （<c>PaneContentGrid.Width = CompactPaneLength = 48</c>）。拿 <c>DisplayModeChanged</c>
    /// 当判据的话，这里一次都不会被触发。
    /// </para>
    /// <para>
    /// 收起时整段藏起来还有一个原因：紧凑态下模板把 <c>FooterContentBorder</c> 压进 48px
    /// 并裁切（上游 issue #10415 未修），留着就是一条被切坏的内容。
    /// </para>
    /// </remarks>
    private void UpdateSidebarPaneMode()
    {
        var compact = !Nav.IsPaneOpen;

        // ★ 判据是「有没有登录」，**不是「有没有歌单」**。
        //   这一段现在带着「新建歌单」入口，零歌单时恰恰最需要它 ——
        //   原来的 hasPlaylists 门控会让新账号把两处入口一起藏掉，等于没有入口可用。
        //   失败那两行重试入口仍由 ErrorText 单独驱动，与这里正交。
        var showPane = _login.IsAuthenticated;

        PlaylistSection.Visibility = showPane && !compact ? Visibility.Visible : Visibility.Collapsed;
        CompactPlaylistsItem.Visibility = showPane && compact ? Visibility.Visible : Visibility.Collapsed;

        // 标题栏左侧那块 Logo 占位要和栏宽对齐，否则收起后搜索框与返回键还停在 200px 处，
        // 中间空出一段被压住的侧栏。宽度直接问 Nav 要，不另抄一份常量 ——
        // OpenPaneLength / CompactPaneLength 就是布局真正用的两个值，抄一份迟早会和它们漂开。
        // 这是行 0 的独立一列，不会自己跟着行 1 的侧栏动，只能手动同步。
        LogoHost.Width = compact ? Nav.CompactPaneLength : Nav.OpenPaneLength;

        // 拖拽区按子元素边界算，而 AutoRefreshDragRegions 是关掉的，宽度变了要重算一次。
        // 排到队列尾：本方法由 IsPaneOpen 的属性回调触发，此刻模板还在切视觉状态。
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_immersiveVisible && !IsMinimized) AppTitleBar.RecomputeDragRegions();
        });

        // 展开态自己有那一段，浮层就没用了；收起时反过来，浮层不该留着。
        if (!compact)
        {
            HidePlaylistsPane();
        }

        // 展开之后页脚才量得到高度。
        SyncPlaylistSectionHeight();
    }

    /// <summary>收起态：轨道上那颗图标点一下开、再点一下关。</summary>
    private void TogglePlaylistsPane()
    {
        _playlistsPaneOpen = !_playlistsPaneOpen;
        PlaylistsOverlay.Visibility = _playlistsPaneOpen ? Visibility.Visible : Visibility.Collapsed;
    }

    private void HidePlaylistsPane()
    {
        _playlistsPaneOpen = false;
        PlaylistsOverlay.Visibility = Visibility.Collapsed;
    }

    /// <summary>点面板以外的地方收起。与播放队列抽屉同一种收法。</summary>
    private void OnPlaylistsDismissTapped(object sender, TappedRoutedEventArgs e) => HidePlaylistsPane();

    /// <summary>浮层右上角的收起按钮。</summary>
    private void OnPlaylistsCloseClick(object sender, RoutedEventArgs e) => HidePlaylistsPane();

    /// <summary>展开态标题行与浮层标题行里那颗「新建歌单」，两处共用同一个动作。</summary>
    private async void OnCreatePlaylistClick(object sender, RoutedEventArgs e)
        => await ShowCreatePlaylistDialogAsync();

    /// <summary>
    /// 两颗「刷新」。与两处失败重试（<see cref="OnPlaylistsRetryRequested"/> /
    /// <see cref="OnPaneRetryClick"/>）是同一个动作，只是那两行只在失败时才出现。
    /// </summary>
    private void OnRefreshPlaylistsClick(object sender, RoutedEventArgs e) => _ = LoadSidebarAsync();

    /// <summary>
    /// 详情页删掉了一个自建歌单：把侧栏那一行摘掉，并离开那一页。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>摘掉是本地动作，不重拉列表</b> —— 删除的写请求已经成功，为此再拉一次是白跑。
    /// </para>
    /// <para>
    /// <b>为什么是换根而不是返回</b>：自建歌单详情是从侧栏 <c>NavigateRoot</c> 进来的根页，
    /// 栈里没有上一页，<c>GoBack</c> 无处可去。落点与启动时一致（「我喜欢的」）。
    /// </para>
    /// </remarks>
    void IPlaylistLibrarySink.OnPlaylistRemoved(long playlistId)
    {
        _sidebar.RemovePlaylist(playlistId);
        AfterSidebarChanged();

        if (_navigation.Current is PlaylistDetailPage page && page.ViewModel.Playlist.Id == playlistId)
        {
            _navigation.NavigateRoot<FavoritesPage>();
        }
    }

    /// <summary>编辑过的歌单：侧栏那一行的名字与封面就地换掉，**不导航**（歌单还在，用户也还停在详情页）。</summary>
    void IPlaylistLibrarySink.OnPlaylistUpdated(long playlistId, string name, Uri? cover)
    {
        _sidebar.UpdatePlaylist(playlistId, name, cover);
        AfterSidebarChanged();
    }

    /// <summary>桌面端 WinRT 互操作（选文件等）要拿宿主窗口来初始化。</summary>
    nint IWindowHandleProvider.WindowHandle => WinRT.Interop.WindowNative.GetWindowHandle(this);

    /// <summary>
    /// 新建歌单的输入框：名字 + 一行「设置为隐私歌单」。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>校验靠 <c>IsPrimaryButtonEnabled</c>，不用 <c>PrimaryButtonClick</c> + deferral。</b>
    /// 名字为空时主按钮就是灰的，回车与点击都无效 —— 不用把已经弹出来的对话框再拦住。
    /// </para>
    /// <para>
    /// <b>默认按钮是「创建」</b>，与清空队列、取消收藏那类破坏性操作「默认落在取消」相反：
    /// 用户是按了 + 才进来的，这是个建设性动作。
    /// </para>
    /// <para>
    /// <b>不设长度上限</b>：实测服务端对 50 字的名字照建（文档 2.4），客户端加上限只会挡住合法输入。
    /// 反过来空白必须自己挡 —— 实测服务端对空名字也照建。
    /// </para>
    /// <para>
    /// <b>建完不收浮层</b>：浮层里那份列表绑的就是 <c>_sidebar.Playlists</c>，
    /// 新行当场出现在最上面，这就是成功反馈；收起态（48 DIP 图标轨）没有别的地方能显示它。
    /// </para>
    /// </remarks>
    private async Task ShowCreatePlaylistDialogAsync()
    {
        var nameBox = new TextBox
        {
            PlaceholderText = "给你的歌单起个名字吧...",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var privateSwitch = new ToggleSwitch
        {
            IsOn = false,
            MinWidth = 0,
            OffContent = "",
            OnContent = "",
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var privateRow = new Grid { ColumnSpacing = 12 };
        privateRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        privateRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        privateRow.Children.Add(new TextBlock
        {
            Text = "设置为隐私歌单",
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetColumn(privateSwitch, 1);
        privateRow.Children.Add(privateSwitch);

        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(nameBox);
        content.Children.Add(privateRow);

        var dialog = CreateAppDialog("新建歌单");
        dialog.Content = content;
        dialog.PrimaryButtonText = "创建";
        dialog.CloseButtonText = "取消";
        dialog.DefaultButton = ContentDialogButton.Primary;
        dialog.IsPrimaryButtonEnabled = false;

        nameBox.TextChanged += (_, _) =>
            dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(nameBox.Text);

        // 打开就聚焦输入框，直接敲名字 + 回车即提交（单行 TextBox 不吞 Enter）。
        dialog.Opened += (_, _) => nameBox.Focus(FocusState.Programmatic);

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var name = nameBox.Text.Trim();

        if (await _sidebar.CreateAsync(name, privateSwitch.IsOn))
        {
            AfterSidebarChanged();
            Notifications.Show($"已创建「{name}」", NoticeSeverity.Success);
            return;
        }

        var failed = CreateAppDialog("没能新建歌单");
        failed.Content = _sidebar.CreateErrorText ?? "请稍后再试。";
        failed.CloseButtonText = "知道了";

        await failed.ShowAsync();
    }

    /// <summary>
    /// 通知条的关闭按钮：收起当前这条，队列里排着的紧接着上。
    /// </summary>
    /// <remarks>
    /// 关掉按钮是自绘的，显隐完全由 <c>Notifications.IsOpen</c> 驱动 ——
    /// 这里只负责通知，不碰界面。
    /// </remarks>
    private void OnNotificationCloseClick(object sender, RoutedEventArgs e) => Notifications.Dismiss();

    /// <summary>
    /// 造一个跟随应用主题、并且收紧了内边距的对话框。
    /// </summary>
    /// <remarks>
    /// 主题与内边距的规矩收在 <see cref="AppDialogs"/> 里，与详情页的编辑对话框共用一份 ——
    /// 那边拿不到这个私有方法，各写一份必然会漂移。
    /// </remarks>
    private ContentDialog CreateAppDialog(string title)
        => AppDialogs.Create(title, ShellRoot.XamlRoot, Theme.RequestedTheme);

    /// <summary>浮层里点了某个歌单：换根进详情，顺手把浮层收掉。</summary>
    private void OnPanePlaylistInvoked(object? sender, Playlist playlist)
    {
        HidePlaylistsPane();
        _navigation.NavigateRoot(_playlistDetailFactory(playlist, SidebarPlaylistSource));
    }

    /// <summary>
    /// 给「创建的歌单」这一段一个高度：从固定项之下一直铺到面板底部。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 页脚那一行是 Auto，内容多高就多高，NavigationView 不替我们算上限 —— 不给高度时
    /// 这一段会把菜单区顶掉、自己溢出面板底部被裁掉。所以这里自己算。
    /// </para>
    /// <para>
    /// <b>两条判据缺一不可，它们共同保证「设高度 → 再布局 → 再算」必然收敛。</b>
    /// </para>
    /// <para>
    /// 一是<b>输入必须与本段高度无关</b>：只量「最后一个固定项的底边」与面板高度，
    /// 两者都在页脚上方，本段怎么变都不影响它们。
    /// ★ 别改成量菜单区的 <c>MenuItemsHost</c>：它在滚动区里会被拉伸到视口高，
    /// 而视口 = 星号行 = 面板高减页脚高 —— 于是本段一变、量到的值就跟着变，永远算不完，
    /// 表现是整个窗口卡死（实测踩过）。
    /// </para>
    /// <para>
    /// 二是<b>输入没变就不重算</b>：本方法挂在 <c>LayoutUpdated</c> 上，每次布局都会来。
    /// 缓存上一次的两个输入，只有它们真的变了才写 <c>Height</c>，否则「设高度触发的额外布局」
    /// 会把同一件事再做一遍。
    /// </para>
    /// <para>
    /// 窗口压到最小（800×560）时剩余空间不够，这时保到 <see cref="MinPlaylistSectionHeight"/>，
    /// 代价是菜单区自己出现滚动条 —— 那种高度下没有既让固定项不滚、又看得见歌单的排法。
    /// </para>
    /// </remarks>
    private void SyncPlaylistSectionHeight()
    {
        if (PlaylistSection.Visibility != Visibility.Visible)
        {
            return;
        }

        var navHeight = Nav.ActualHeight;
        var anchorBottom = MeasureMenuAnchorBottom();

        // 还没布局完：下一轮 LayoutUpdated 再来。
        if (navHeight <= 0 || double.IsNaN(anchorBottom))
        {
            return;
        }

        if (Math.Abs(navHeight - _lastNavHeight) < 0.5 && Math.Abs(anchorBottom - _lastAnchorBottom) < 0.5)
        {
            return;
        }

        _lastNavHeight = navHeight;
        _lastAnchorBottom = anchorBottom;

        PlaylistSection.Height = Math.Max(
            MinPlaylistSectionHeight,
            navHeight - anchorBottom - PaneFooterBottomMargin - PlaylistSectionSlack);
    }

    /// <summary>
    /// 菜单区最后一个可见项的底边（相对 <see cref="Nav"/>）。
    /// </summary>
    /// <returns>还没布局完时返回 <see cref="double.NaN"/>。</returns>
    /// <remarks>
    /// 取「最后一个可见项」而不是累加全部项，是让它自己跟着菜单内容走：以后往 <c>MenuItems</c>
    /// 里增删项都不用改这里。收起态那颗紧凑栏图标是 <c>Collapsed</c>，自然被跳过；
    /// 短的窗口里用户把菜单区滚下去时，项的位置会整体偏移 —— 那种高度下高度本来就被压到下限，
    /// 偏移只影响下限之上的那几个像素，可以接受。
    /// </remarks>
    private double MeasureMenuAnchorBottom()
    {
        FrameworkElement? anchor = null;

        foreach (var item in Nav.MenuItems)
        {
            // 有容器就量容器（外边距算在容器上），没有就退回量项本身。
            var element = Nav.ContainerFromMenuItem(item) as FrameworkElement ?? item as FrameworkElement;

            if (element is { Visibility: Visibility.Visible, ActualHeight: > 0 })
            {
                anchor = element;
            }
        }

        return anchor is null
            ? double.NaN
            : anchor.TransformToVisual(Nav).TransformPoint(default).Y + anchor.ActualHeight;
    }

    private void OnItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        // 标题与分隔符没有 Tag，点击时不该做任何事。
        if (args.InvokedItemContainer is not NavigationViewItem { Tag: { } tag })
        {
            return;
        }

        switch (tag)
        {
            case DiscoverTag:
                _navigation.NavigateRoot<DiscoverPage>();
                break;

            case BangsTag:
                _navigation.NavigateRoot<BangListPage>();
                break;

            case LibraryTag:
                _navigation.NavigateRoot<LibraryPage>();
                break;

            case FavoritesTag:
                _navigation.NavigateRoot<FavoritesPage>();
                break;

            case RecentTag:
                _navigation.NavigateRoot<RecentPage>();
                break;

            case CollectedAlbumsTag:
                _navigation.NavigateRoot<CollectedAlbumsPage>();
                break;

            case CollectedPlaylistsTag:
                _navigation.NavigateRoot<CollectedPlaylistsPage>();
                break;

            case PlaylistsRailTag:
                // 收起态那颗图标：开关歌单浮层。这一项配了 SelectsOnInvoked=False，
                // 所以它不会被选成高亮，也不会污染 Nav.SelectedItem。
                TogglePlaylistsPane();
                break;
        }
    }

    /// <summary>面板底部那份列表里点了某个歌单。侧栏里的都是账号自建歌单，<c>source = 5</c>。</summary>
    private void OnSidebarPlaylistInvoked(object? sender, Playlist playlist) =>
        _navigation.NavigateRoot(_playlistDetailFactory(playlist, SidebarPlaylistSource));

    private void OnPlaylistsRetryRequested(object? sender, EventArgs e) => _ = LoadSidebarAsync();

    /// <summary>浮层里的重试行。与 <see cref="OnPlaylistsRetryRequested"/> 同一个动作，只是委托签名不同。</summary>
    private void OnPaneRetryClick(object sender, RoutedEventArgs e) => _ = LoadSidebarAsync();

    public event EventHandler? RenderingStateChanged;
    public bool IsRenderingSuspended => _renderActivity.IsSuspended;
    public bool IsMinimized => _renderActivity.IsMinimized;

    internal bool IsChangingLyricsPresenter => _isChangingLyricsPresenter;

    public bool IsLyricsFullscreen => _lyricsFullscreen;

    /// <summary>
    /// 进入沉浸态：收起外壳（侧栏、播放条、标题栏），只留 <c>ImmersiveHost</c>。
    /// </summary>
    /// <remarks>
    /// 歌词页与 MV 页共用这一套 —— 它做的本来就不是「歌词」的事，只是最早只有歌词页用，
    /// 才叫了这个名字。两者要隐藏和恢复的东西完全一致。
    /// </remarks>
    public void EnterImmersive(FrameworkElement titleBar)
    {
        RestoreNativeCaption();
        _immersiveVisible = true;
        _lyricsChromeVisible = true;
        _lyricsTitleBar = titleBar;
        DismissSearchUi();
        ShellBackdrop.Visibility = Visibility.Collapsed;
        AppTitleBar.Visibility = Visibility.Collapsed;
        Nav.Visibility = Visibility.Collapsed;
        PlayerHost.Visibility = Visibility.Collapsed;

        // 播放条一收，第 2 行的高度就归零，通知条会跟着掉到窗口底部、压在沉浸页自己的传输按钮上。
        // 按沉浸页的量抬一次，见 ImmersiveNotificationInset。
        NotificationBar.Margin = new Thickness(0, 0, 0, ImmersiveNotificationInset);

        // 同一个道理，掉到顶上的是两张浮层：第 0 行一收，它们就从窗口顶端 8px 处开始，
        // 头部按钮落进沉浸页标题栏那条非客户区里，点不动。见 ImmersiveChromeHeight。
        SyncOverlayInsets();

        ImmersiveHost.Visibility = Visibility.Visible;
        SetTitleBar(titleBar);
        UpdateCaptionButtonColors();
    }

    /// <summary>
    /// 在「窗口化」与「无边框全屏」之间切换。
    /// </summary>
    /// <remarks>
    /// 歌词页与 MV 页共用。恢复时把标题栏交还给 <c>_lyricsTitleBar</c> ——
    /// 那个字段由 <see cref="EnterImmersive"/> 填成当前沉浸页的标题栏，所以对两页都对。
    /// </remarks>
    public async Task ToggleImmersiveFullscreenAsync()
    {
        if (_isChangingLyricsPresenter) return;
        var performanceStart = Stopwatch.GetTimestamp();
        var performanceLogger = (Application.Current.Resources["BodianLoggerFactory"] as ILoggerFactory)?.CreateLogger<MainWindow>();
        var performanceEnabled = Environment.GetEnvironmentVariable("BODIAN_LYRICS_DIAGNOSTICS") == "1";
        _isChangingLyricsPresenter = true;
        _renderActivity.BeginTransition();
        if (performanceEnabled) performanceLogger?.LogInformation("全屏阶段：暂停绘制 {Elapsed:F2} ms", Stopwatch.GetElapsedTime(performanceStart).TotalMilliseconds);
        try
        {
            RestoreNativeCaption();
            if (performanceEnabled) performanceLogger?.LogInformation("全屏阶段：恢复按钮累计 {Elapsed:F2} ms", Stopwatch.GetElapsedTime(performanceStart).TotalMilliseconds);
            if (IsLyricsFullscreen)
            {
                await RestoreLyricsPresenterAsync();
            }
            else
            {
                if (AppWindow.Presenter is not OverlappedPresenter presenter) return;
                var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
                var placement = new NativeMethods.WindowPlacement { Length = (uint)Marshal.SizeOf<NativeMethods.WindowPlacement>() };
                if (!NativeMethods.GetWindowPlacement(handle, ref placement))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "无法保存全屏前的窗口位置");
                _lyricsRestorePresenter = presenter;
                _lyricsRestorePlacement = placement;
                _lyricsRestoreStyle = NativeMethods.GetWindowLongPtr(handle, -16);
                var bounds = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).OuterBounds;
                _lyricsFullscreen = true;
                SetTitleBar(null);
                // 一次样式写入 + 一次位置提交，避免逐个 Presenter 属性触发同步窗口变化。
                var style = _lyricsRestoreStyle;
                await Task.Run(() =>
                {
                    NativeMethods.SetWindowLongPtr(handle, -16, (nint)((long)style & ~(0x00C00000L | 0x00040000L | 0x01000000L)));
                    if (!NativeMethods.SetWindowPos(handle, 0, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0034))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "无法进入显示器全屏");
                });
            }
        }
        finally
        {
            _isChangingLyricsPresenter = false;
            if (!_closed) _renderActivity.EndTransition();
            if (performanceEnabled) performanceLogger?.LogInformation("全屏阶段：切换窗口累计 {Elapsed:F2} ms", Stopwatch.GetElapsedTime(performanceStart).TotalMilliseconds);
        }
    }

    private async Task RestoreLyricsPresenterAsync()
    {
        if (_lyricsRestorePresenter is null) return;
        _lyricsFullscreen = false;
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var style = _lyricsRestoreStyle;
        var placement = _lyricsRestorePlacement;
        await Task.Run(() => RestoreLyricsWindow(handle, style, placement));
        _lyricsRestorePresenter = null;
        if (!_closed) SetTitleBar(_lyricsTitleBar);
    }

    private void RestoreLyricsPresenter()
    {
        if (_lyricsRestorePresenter is null) return;
        _lyricsFullscreen = false;
        RestoreLyricsWindow(WinRT.Interop.WindowNative.GetWindowHandle(this), _lyricsRestoreStyle, _lyricsRestorePlacement);
        _lyricsRestorePresenter = null;
        SetTitleBar(_lyricsTitleBar);
    }

    private static void RestoreLyricsWindow(nint handle, nint style, NativeMethods.WindowPlacement placement)
    {
        NativeMethods.SetWindowLongPtr(handle, -16, style);
        if (!NativeMethods.SetWindowPlacement(handle, ref placement))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法恢复全屏前的窗口位置");
        if (!NativeMethods.SetWindowPos(handle, 0, 0, 0, 0, 0, 0x0037))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法恢复窗口边框");
    }

    /// <summary>退出沉浸态，恢复外壳。与 <see cref="EnterImmersive"/> 成对。</summary>
    public void ExitImmersive()
    {
        RestoreNativeCaption();
        RestoreLyricsPresenter();
        _immersiveVisible = false;
        _lyricsChromeVisible = true;
        _lyricsTitleBar = null;
        ImmersiveHost.Visibility = Visibility.Collapsed;
        ShellBackdrop.Visibility = Visibility.Visible;
        AppTitleBar.Visibility = Visibility.Visible;
        Nav.Visibility = Visibility.Visible;
        PlayerHost.Visibility = Visibility.Visible;

        // 播放条回来了，通知条也跟着回到「播放条上方」那个位置。
        NotificationBar.Margin = new Thickness(0, 0, 0, NotificationBottomGap);
        SyncOverlayInsets();

        SetTitleBar(AppTitleBar);
        UpdateCaptionButtonColors();
    }

    /// <summary>
    /// 按当前是不是沉浸态摆正两张浮层卡片的上下边距。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 常规态：落在第 1 行里再加 <see cref="OverlayTopGap"/> / <see cref="OverlayBottomGap"/>，
    /// 与两处 XAML 里的初始值一致。
    /// </para>
    /// <para>
    /// 沉浸态：**上下两头都要改**。第 0 行高度归零，浮层顶端会陷进沉浸页标题栏那条非客户区，
    /// 头部那颗收起按钮点不动（见 <see cref="ImmersiveChromeHeight"/>）；第 2 行也归零，
    /// 浮层会一路铺到窗口底部压住进度条（见 <see cref="ImmersiveOverlayBottom"/>）。
    /// </para>
    /// </remarks>
    private void SyncOverlayInsets()
    {
        var top = OverlayTopGap + (_immersiveVisible ? ImmersiveChromeHeight : 0);
        var bottom = _immersiveVisible ? ImmersiveOverlayBottom : OverlayBottomGap;

        QueuePane.Margin = new Thickness(0, top, 12, bottom);
        PlaylistsPane.Margin = new Thickness(56, top, 0, bottom);
    }

    /// <summary>
    /// 窗口关闭前让当前页收尾：把「必须赶在容器释放之前做的事」做掉。
    /// </summary>
    /// <remarks>
    /// <b>必须在 <c>_host.Dispose()</c> 之前调。</b> 容器会释放它捕获的瞬态 ViewModel，
    /// MV 页的 <c>MediaPlayer</c> 就是其中之一 —— 它得先与 <c>MediaPlayerElement</c> 解绑，
    /// 否则原生对象在元素还持着它时被释放，表现为**关闭即卡死**。
    /// 关窗口不走导航，页面的离场通知收不到，所以这条通道是必要的，见 <see cref="IShutdownAware"/>。
    /// </remarks>
    public void TearDownForShutdown() => (_navigation.Current as IShutdownAware)?.OnShuttingDown();

    public void SetLyricsChromeVisible(bool visible)
    {
        if (IsMinimized || _lyricsChromeVisible == visible) return;
        _lyricsChromeVisible = visible;
        if (IsLyricsFullscreen) return;
        if (visible)
        {
            RestoreNativeCaption();
        }
        else if (_immersiveVisible && AppWindow.Presenter is OverlappedPresenter presenter)
        {
            _hiddenCaptionPresenter = presenter;
            _captionRestoreBorder = presenter.HasBorder;
            _captionRestoreTitleBar = presenter.HasTitleBar;
            presenter.SetBorderAndTitleBar(_captionRestoreBorder, false);
        }
        UpdateCaptionButtonColors();
    }

    private void RestoreNativeCaption()
    {
        if (_hiddenCaptionPresenter is not { } presenter) return;
        presenter.SetBorderAndTitleBar(_captionRestoreBorder, _captionRestoreTitleBar);
        _hiddenCaptionPresenter = null;
    }

    private void OnNavigated(object? sender, Page page)
    {
        SyncSelection();
        if (!_immersiveVisible) DispatcherQueue.TryEnqueue(() =>
        {
            if (!_immersiveVisible && !IsMinimized) AppTitleBar.RecomputeDragRegions();
        });
    }

    /// <summary>
    /// 把侧栏高亮同步到当前**根页**。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 没有对应项的根页（搜索页）会让侧栏全不高亮 —— 那比错误高亮一项好，
    /// 用户能看出「我当前不在侧栏列出的任何一段里」。
    /// </para>
    /// <para>
    /// 歌单详情同样不进 <c>Nav.SelectedItem</c>：它的高亮画在「创建的歌单」列表里，
    /// 侧栏本体保持全不高亮。两个实例（页脚那份与弹层那份）都要推，否则从轨上点进来的
    /// 那次会只剩一边亮着。
    /// </para>
    /// </remarks>
    private void SyncSelection()
    {
        Nav.SelectedItem = _navigation.Root switch
        {
            DiscoverPage => DiscoverItem,
            BangListPage => BangsItem,
            LibraryPage => LibraryItem,
            FavoritesPage => FavoritesItem,
            RecentPage => RecentItem,
            CollectedAlbumsPage => CollectedAlbumsItem,
            CollectedPlaylistsPage => CollectedPlaylistsItem,
            _ => null,
        };

        // 只推面板底部那一份：收起态浮层里用的是 PlaylistListView，它不支持选中态
        // （那是一个「挑一个就走」的浮层，不留高亮）。
        FooterPlaylistList.SelectPlaylist((_navigation.Root as PlaylistDetailPage)?.ViewModel.Playlist.Id);
    }
}
