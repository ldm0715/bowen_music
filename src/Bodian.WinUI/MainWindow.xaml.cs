using Bodian.Core.Api;
using Bodian.WinUI.Controls;
using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
using Bodian.WinUI.Views;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Bodian.WinUI;

/// <summary>
/// 主窗口，也就是外壳：上面一块 <see cref="Frame"/> 放页面，下面一条常驻的播放条。
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly INavigationService _navigation;
    private readonly IBodianLogin _login;

    /// <summary>给 <c>x:Bind</c> 用。</summary>
    public PlayerViewModel Player { get; }

    /// <summary>给 <c>x:Bind</c> 用（歌词面板的可见性）。</summary>
    public LyricsViewModel Lyrics { get; }

    public MainWindow(
        INavigationService navigation,
        IBodianLogin login,
        PlayerViewModel playerViewModel,
        LyricsViewModel lyricsViewModel)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(login);
        ArgumentNullException.ThrowIfNull(playerViewModel);
        ArgumentNullException.ThrowIfNull(lyricsViewModel);

        _navigation = navigation;
        _login = login;

        Player = playerViewModel;
        Lyrics = lyricsViewModel;

        InitializeComponent();

        ApplyMinimumSize();

        _navigation.Attach(RootFrame);
        PlayerHost.Content = new PlayerBar(playerViewModel, lyricsViewModel);
        LyricsHost.Content = new LyricsPanel(lyricsViewModel);

        _login.AccountChanged += OnAccountChanged;
        RootFrame.Loaded += OnRootLoaded;
    }

    /// <summary>
    /// 给窗口设最小尺寸。
    /// </summary>
    /// <remarks>
    /// <b>不是可选的润色。</b> 播放条是三栏布局，窗口窄到一定程度后中间那栏会被压得比按钮还窄，
    /// 表现是「播放按钮的图标显示不全」——控件没坏，是被裁了。底部播放条的下限大约
    /// 56(封面) + 220(信息) + 控制区 + 200(音质音量) + 间距，取 800。
    /// </remarks>
    private void ApplyMinimumSize()
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter)
        {
            return;
        }

        presenter.PreferredMinimumWidth = 800;
        presenter.PreferredMinimumHeight = 560;
    }

    private void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        RootFrame.Loaded -= OnRootLoaded;

        // 先试着恢复上次的会话：本机已有登录态时不该每次都让人重新扫码。
        if (_login.TryRestorePersistedSession())
        {
            _navigation.Reset<SearchPage>();
        }
        else
        {
            _navigation.Reset<LoginPage>();
        }
    }

    /// <summary>
    /// 会话没了（用户登出，或服务端返回 11012 把它清掉）就回登录页。
    /// </summary>
    /// <remarks>
    /// 登录成功也会触发这个事件，但那时 <c>IsAuthenticated</c> 为真，不在这里导航 ——
    /// 由登录页自己切到搜索页，避免两处同时导航。
    /// </remarks>
    private void OnAccountChanged(object? sender, EventArgs e)
    {
        if (!_login.IsAuthenticated)
        {
            _navigation.Reset<LoginPage>();
        }
    }
}
