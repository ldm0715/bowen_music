using Bodian.Core.Api;
using Bodian.WinUI.Controls;
using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
using Bodian.WinUI.Views;
using Microsoft.UI.Xaml;

namespace Bodian.WinUI;

/// <summary>
/// 主窗口，也就是外壳：上面一块 <see cref="Frame"/> 放页面，下面一条常驻的播放条。
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly INavigationService _navigation;
    private readonly IBodianLogin _login;

    public MainWindow(
        INavigationService navigation,
        IBodianLogin login,
        PlayerViewModel playerViewModel)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(login);
        ArgumentNullException.ThrowIfNull(playerViewModel);

        _navigation = navigation;
        _login = login;

        InitializeComponent();

        _navigation.Attach(RootFrame);
        PlayerHost.Content = new PlayerBar(playerViewModel);

        _login.AccountChanged += OnAccountChanged;
        RootFrame.Loaded += OnRootLoaded;
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
