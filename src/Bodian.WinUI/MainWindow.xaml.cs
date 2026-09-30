using Bodian.Core.Api;
using Bodian.WinUI.Controls;
using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
using Bodian.WinUI.Views;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Bodian.WinUI;

/// <summary>
/// 主窗口，也就是外壳：上面一块内容宿主放页面，下面一条常驻的播放条。
/// </summary>
/// <remarks>
/// 宿主是 <see cref="ContentControl"/> 而不是 <see cref="Frame"/> —— 返回栈要挂回「原来那个页面实例」，
/// 而 <c>Frame.Content</c> 不接受重复挂载同一个实例。理由见 <c>INavigationService</c> 的说明。
/// </remarks>
public sealed partial class MainWindow : Window
{
    private readonly INavigationService _navigation;
    private readonly IBodianLogin _login;

    /// <summary>给 <c>x:Bind</c> 用。</summary>
    public PlayerViewModel Player { get; }

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

        InitializeComponent();

        ApplyMinimumSize();

        _navigation.Attach(PageHost);
        PlayerHost.Content = new PlayerBar(playerViewModel, lyricsViewModel, navigation);

        _login.AccountChanged += OnAccountChanged;
        PageHost.Loaded += OnHostLoaded;
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

    private void OnHostLoaded(object sender, RoutedEventArgs e)
    {
        PageHost.Loaded -= OnHostLoaded;

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
