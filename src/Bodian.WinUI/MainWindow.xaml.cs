using Bodian.Core.Api;
using Bodian.Core.Models;
using Bodian.Core.Services.Abstractions;
using Bodian.WinUI.Controls;
using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
using Bodian.WinUI.Views;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
public sealed partial class MainWindow : Window
{
    /// <summary>侧栏项的标记。<c>Tag</c> 用字符串的只有内置的几项，歌单项直接放 <see cref="Playlist"/>。</summary>
    private const string DiscoverTag = "discover";

    private const string BangsTag = "bangs";

    private const string LibraryTag = "library";

    private const string FavoritesTag = "favorites";

    private const string RecentTag = "recent";

    private const string PurchasedTag = "purchased";

    private const string CollectedAlbumsTag = "collected-albums";

    private const string ReloadPlaylistsTag = "reload-playlists";

    /// <summary>侧栏里的歌单都是账号自建歌单，<c>source = 5</c>。</summary>
    private const int SidebarPlaylistSource = 5;

    private readonly INavigationService _navigation;
    private readonly IBodianLogin _login;
    private readonly SidebarViewModel _sidebar;
    private readonly IWindowPlacementStore _placement;
    private readonly Func<Playlist, int, PlaylistDetailPage> _playlistDetailFactory;

    /// <summary>侧栏里为「创建的歌单」动态加进去的项。重新加载时要先摘掉它们。</summary>
    private readonly List<NavigationViewItem> _playlistItems = [];
    private AppWindowPresenter? _lyricsRestorePresenter;
    private FrameworkElement? _lyricsTitleBar;
    private bool _lyricsVisible;
    private bool _lyricsChromeVisible = true;
    private OverlappedPresenter? _hiddenCaptionPresenter;
    private bool _captionRestoreBorder;
    private bool _captionRestoreTitleBar;

    public MainWindow(
        INavigationService navigation,
        IBodianLogin login,
        PlayerViewModel playerViewModel,
        LyricsViewModel lyricsViewModel,
        AccountViewModel account,
        SidebarViewModel sidebar,
        SearchViewModel search,
        ThemeViewModel theme,
        IWindowPlacementStore placement,
        Func<Playlist, int, PlaylistDetailPage> playlistDetailFactory)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(login);
        ArgumentNullException.ThrowIfNull(playerViewModel);
        ArgumentNullException.ThrowIfNull(lyricsViewModel);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(sidebar);
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(playlistDetailFactory);

        _navigation = navigation;
        _login = login;
        _sidebar = sidebar;
        _placement = placement;
        _playlistDetailFactory = playlistDetailFactory;

        Player = playerViewModel;
        Account = account;
        Search = search;
        Theme = theme;

        InitializeComponent();

        // 先同步保存的档位，再订阅选择事件，避免初始化时把设置改成第一个选项。
        SyncThemeSelection();
        ThemeOptions.SelectionChanged += OnThemeSelectionChanged;

        // 曲目列表靠这个知道「哪一行在播」。
        //
        // ★ 为什么走 App 资源而不是给 6 个页面各传一份：TrackListView 是 XAML 实例化的，
        //   构造函数必须无参，拿不到 DI 容器。显式传就要改 6 份 XAML + 6 个 ViewModel，
        //   而这里放一份引用就够了。控件侧仍保留一个 NowPlaying 依赖属性，
        //   显式指定时优先 —— 见 TrackListView.NowPlaying 的说明。
        Application.Current.Resources["BodianNowPlaying"] = playerViewModel;

        ApplyWindowPlacement();
        ConfigureTitleBar();
        Closed += (_, _) => SaveWindowPlacement();

        _navigation.Attach(PageHost, page => page is LyricsPage ? ImmersiveHost : PageHost);
        _navigation.Navigated += OnNavigated;

        PlayerHost.Content = new PlayerBar(playerViewModel, lyricsViewModel, navigation);

        _login.AccountChanged += OnAccountChanged;
        PageHost.Loaded += OnHostLoaded;
    }

    /// <summary>给 <c>x:Bind</c> 用。</summary>
    public PlayerViewModel Player { get; }

    /// <summary>标题栏账号入口的数据源。</summary>
    public AccountViewModel Account { get; }

    /// <summary>顶部搜索框的数据源。<b>与搜索页是同一个实例</b>，所以框里的词与结果永远一致。</summary>
    public SearchViewModel Search { get; }

    /// <summary>外观切换。绑在根 <c>Grid</c> 的 <c>RequestedTheme</c> 上。</summary>
    public ThemeViewModel Theme { get; }

    /// <summary>替换系统标题栏；不改变窗口的尺寸、位置或保存的窗口矩形。</summary>
    private void ConfigureTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        SetTitleBar(AppTitleBar);

        ShellRoot.ActualThemeChanged += (_, _) => UpdateCaptionButtonColors();
        AppTitleBar.RegisterPropertyChangedCallback(Control.ForegroundProperty, (_, _) => UpdateCaptionButtonColors());
        UpdateCaptionButtonColors();
    }

    /// <summary>原生窗口按钮共用透明背景，并随实际主题更新前景色。</summary>
    private void UpdateCaptionButtonColors()
    {
        var titleBar = AppWindow.TitleBar;
        var isDark = _lyricsVisible || ShellRoot.ActualTheme == ElementTheme.Dark;
        var foreground = _lyricsVisible ? Colors.White : AppTitleBar.Foreground is SolidColorBrush brush
            ? brush.Color
            : isDark ? Colors.White : Colors.Black;
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

    private void SyncThemeSelection() => ThemeOptions.SelectedItem = Theme.Current switch
    {
        AppTheme.Light => LightThemeOption,
        AppTheme.Dark => DarkThemeOption,
        _ => SystemThemeOption,
    };

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
        if (AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen)
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

        // 换号之后「创建的歌单」是另一份，必须重拉（重拉会先摘掉上一个账号留下的项）。
        _ = LoadSidebarAsync();
    }

    /// <summary>
    /// 顶部搜索框提交。
    /// </summary>
    /// <remarks>
    /// <b>关键词以 <c>args.QueryText</c> 为准再赋一次</b>：<c>AutoSuggestBox.Text</c> 的
    /// 双向绑定不保证每次按键都回写（<c>TextBox</c> 系列默认就不是 <c>PropertyChanged</c>），
    /// 而按下回车就直接走这里。重复赋值是幂等的，代价可以忽略。
    /// </remarks>
    private void OnSearchQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        Search.Keyword = args.QueryText;

        if (!Search.SearchCommand.CanExecute(null))
        {
            return;
        }

        Search.SearchCommand.Execute(null);
        _navigation.NavigateRoot<SearchPage>();
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
        RebuildPlaylistItems();
    }

    /// <summary>
    /// 按 <see cref="SidebarViewModel.Playlists"/> 重建「创建的歌单」那一段。
    /// </summary>
    /// <remarks>
    /// 整段重建而不是增量更新：项数最多几十，重建的代价可以忽略；
    /// 而增量更新要处理「上一次加载失败留下的重试项」这类残留，容易漏。
    /// </remarks>
    private void RebuildPlaylistItems()
    {
        foreach (var item in _playlistItems)
        {
            Nav.MenuItems.Remove(item);
        }

        _playlistItems.Clear();

        foreach (var playlist in _sidebar.Playlists)
        {
            AddPlaylistItem(new NavigationViewItem
            {
                Content = playlist.Name,
                Tag = playlist,
                Icon = new SymbolIcon(Symbol.MusicInfo),
            });
        }

        if (_sidebar.ErrorText is { } error)
        {
            var retry = new NavigationViewItem
            {
                Content = "歌单加载失败，点击重试",
                Tag = ReloadPlaylistsTag,
                Icon = new SymbolIcon(Symbol.Refresh),
            };

            ToolTipService.SetToolTip(retry, error);
            AddPlaylistItem(retry);
        }

        // 一个项都没有时连标题一起收起来，否则侧栏会留一个空标题。
        CreatedHeader.Visibility = _playlistItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        SyncSelection();
    }

    private void AddPlaylistItem(NavigationViewItem item)
    {
        // 追加即可：MenuItems 里「创建的歌单」标题已经是最后一项，动态项自然跟在它后面。
        Nav.MenuItems.Add(item);
        _playlistItems.Add(item);
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

            case PurchasedTag:
                _navigation.NavigateRoot<PurchasedPage>();
                break;

            case CollectedAlbumsTag:
                _navigation.NavigateRoot<CollectedAlbumsPage>();
                break;

            case ReloadPlaylistsTag:
                _ = LoadSidebarAsync();
                break;

            case Playlist playlist:
                // 侧栏里的都是账号自建歌单，source = 5。
                _navigation.NavigateRoot(_playlistDetailFactory(playlist, SidebarPlaylistSource));
                break;
        }
    }

    public bool IsLyricsFullscreen => AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;

    public void EnterLyrics(FrameworkElement titleBar)
    {
        RestoreNativeCaption();
        _lyricsVisible = true;
        _lyricsChromeVisible = true;
        _lyricsTitleBar = titleBar;
        AppTitleBar.Visibility = Visibility.Collapsed;
        Nav.Visibility = Visibility.Collapsed;
        PlayerHost.Visibility = Visibility.Collapsed;
        ImmersiveHost.Visibility = Visibility.Visible;
        SetTitleBar(titleBar);
        UpdateCaptionButtonColors();
    }

    public void ToggleLyricsFullscreen()
    {
        RestoreNativeCaption();
        if (IsLyricsFullscreen)
        {
            RestoreLyricsPresenter();
        }
        else
        {
            _lyricsRestorePresenter = AppWindow.Presenter;
            SetTitleBar(null);
            AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        }
    }

    private void RestoreLyricsPresenter()
    {
        if (_lyricsRestorePresenter is { } presenter)
        {
            AppWindow.SetPresenter(presenter);
            _lyricsRestorePresenter = null;
            SetTitleBar(_lyricsTitleBar);
        }
    }

    public void ExitLyrics()
    {
        RestoreNativeCaption();
        RestoreLyricsPresenter();
        _lyricsVisible = false;
        _lyricsChromeVisible = true;
        _lyricsTitleBar = null;
        ImmersiveHost.Visibility = Visibility.Collapsed;
        AppTitleBar.Visibility = Visibility.Visible;
        Nav.Visibility = Visibility.Visible;
        PlayerHost.Visibility = Visibility.Visible;
        SetTitleBar(AppTitleBar);
        UpdateCaptionButtonColors();
    }

    public void SetLyricsChromeVisible(bool visible)
    {
        if (_lyricsChromeVisible == visible) return;
        _lyricsChromeVisible = visible;
        if (visible)
        {
            RestoreNativeCaption();
        }
        else if (_lyricsVisible && AppWindow.Presenter is OverlappedPresenter presenter)
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

    private void OnNavigated(object? sender, Page page) => SyncSelection();

    /// <summary>
    /// 把侧栏高亮同步到当前**根页**。
    /// </summary>
    /// <remarks>
    /// 没有对应项的根页（搜索页）会让侧栏全不高亮 —— 那比错误高亮一项好，
    /// 用户能看出「我当前不在侧栏列出的任何一段里」。
    /// </remarks>
    private void SyncSelection() =>
        Nav.SelectedItem = _navigation.Root switch
        {
            DiscoverPage => DiscoverItem,
            BangListPage => BangsItem,
            LibraryPage => LibraryItem,
            FavoritesPage => FavoritesItem,
            RecentPage => RecentItem,
            PurchasedPage => PurchasedItem,
            CollectedAlbumsPage => CollectedAlbumsItem,

            PlaylistDetailPage detail => _playlistItems.FirstOrDefault(
                item => item.Tag is Playlist playlist && playlist.Id == detail.ViewModel.Playlist.Id),

            _ => null,
        };
}
