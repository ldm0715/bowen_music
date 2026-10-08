using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Bodian.WinUI.Views;

/// <summary>
/// 【已被取代，待删】登录框的旧宿主。
/// </summary>
/// <remarks>
/// <para>
/// <b>不要再用它。</b> 现在是 <c>Controls/LoginDialogContent</c>（内容）+
/// <c>Controls/LoginDialog</c>（宿主）：挂在窗口的 <c>XamlRoot</c> 上、不进导航栈。
/// 本页作为「一页压在栈上的宿主」有个致命毛病 —— 弹层一旦丢失（窗口隐藏到托盘之后
/// 就会），它的 <c>ShowAsync</c> 永不返回、退栈永不执行，这一页就永远留在栈顶，
/// 此后 <c>Navigate&lt;LoginPage&gt;</c> 被「当前页已经是这个页面」静默挡掉，**再也打不开**。
/// 详见 <c>docs/anonymous-browse.md</c>。
/// </para>
/// <para>
/// 保留只是因为删除文件要先问过用户；除 <c>App.xaml.cs</c> 里那行 DI 注册外无人引用。
/// </para>
/// </remarks>
/// <para>
/// <b>它不是一个「必须登录才能离开」的宿主页。</b> 未登录有自己的稳态（匿名外壳，见
/// <c>docs/anonymous-browse.md</c>），所以关掉它只是退栈回到原处：从「添加账号」进来的
/// 就回到原来那个账号，从未登录启动进来的就回到匿名外壳。
/// </para>
/// <para>
/// 关闭入口用 <see cref="ContentDialog.CloseButtonText"/>，<b>Esc 走的是同一颗按钮</b>，
/// 所以不需要为无障碍单独接一次键盘。
/// </para>
/// </remarks>
public sealed partial class LoginPage : Page
{
    private readonly INavigationService _navigation;
    private readonly ILogger<LoginPage> _logger;
    private bool _isActive;

    public LoginPage(LoginViewModel viewModel, INavigationService navigation,
        ILogger<LoginPage>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(navigation);

        ViewModel = viewModel;
        _navigation = navigation;
        _logger = logger ?? NullLogger<LoginPage>.Instance;

        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ActualThemeChanged += (_, _) => LoginDialog.RequestedTheme = ActualTheme;
    }

    public LoginViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isActive)
        {
            return;
        }

        _isActive = true;
        ViewModel.LoggedIn += OnLoggedIn;

        LoginDialog.XamlRoot = XamlRoot;
        LoginDialog.RequestedTheme = ActualTheme;

        // 先显示对话框，再开始扫码；已恢复会话时的同步 LoggedIn 也能正常关闭它。
        // 进出都记一行：登录框「打不开」这种反馈里，只有这几行能区分
        // 「页面没挂上」「对话框没弹出来」「弹出来了但被关掉了」三种情况。
        _logger.LogInformation("登录框：开始显示（XamlRoot {State}）", XamlRoot is null ? "缺失" : "就绪");

        var dialogOperation = LoginDialog.ShowAsync();
        await ViewModel.ActivateAsync();
        await dialogOperation;

        _logger.LogInformation("登录框：已关闭，准备退栈");
        Leave();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isActive = false;
        ViewModel.LoggedIn -= OnLoggedIn;
        ViewModel.Deactivate();
        LoginDialog.Hide();
    }

    /// <summary>
    /// 关掉之后退栈回到原处。
    /// </summary>
    /// <remarks>
    /// <b>不跳「我喜欢的」</b>：从外壳里打开这个框时，用户原来在看什么就留在什么 ——
    /// 登录成功后外壳自己会重拉（侧栏、当前页若是账号级）。
    /// 万一它是栈底（正常路径不会），起码给一块能用的界面，而不是一个空壳。
    /// </remarks>
    private void Leave()
    {
        if (_navigation.CanGoBack)
        {
            _navigation.GoBack();
            return;
        }

        // 正常路径不该走到这里（它是压在栈上的一个入口）。
        // ★ 但也**不能就这么留着**：留下的会是一页空白，而且再点「登录」时
        //   导航会因为「当前页已经是这个身份」而静默不动作 —— 那才是真的打不开。
        _logger.LogWarning("登录框是栈底，退回发现页");
        _navigation.NavigateRoot<DiscoverPage>();
    }

    /// <summary>登录成功：关掉弹窗，剩下的交给 <see cref="Leave"/> 与会话变更。</summary>
    private void OnLoggedIn(object? sender, EventArgs e) => LoginDialog.Hide();

    private void OnCloseClick(object sender, RoutedEventArgs e) => LoginDialog.Hide();

    /// <summary>
    /// Esc 关掉弹窗。
    /// </summary>
    /// <remarks>
    /// 关闭按钮改成自绘的 ✕ 之后，<c>ContentDialog</c> 那条「Esc → 关闭按钮」的内置路径就没了，
    /// 得自己接一次 —— 否则键盘用户只能靠鼠标点右上角。
    /// </remarks>
    private void OnDialogKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape)
        {
            return;
        }

        e.Handled = true;
        LoginDialog.Hide();
    }
}
