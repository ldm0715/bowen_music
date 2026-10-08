using Bodian.WinUI.ViewModels;
using Microsoft.UI.Xaml;

namespace Bodian.WinUI.Controls;

/// <summary>
/// 弹出登录对话框。<b>挂在传入的 <see cref="XamlRoot"/> 上，不进导航栈。</b>
/// </summary>
/// <remarks>
/// <para>
/// <b>每一次打开都新建一个对话框实例。</b> 复用同一个实例看着更省，但那样会继承上一轮的状态：
/// <c>ContentDialog.IsOpen</c> 是逻辑状态，而弹层可能已经不在了（窗口隐藏到托盘、切主题、
/// 显示器变化都可能让它消失）。逻辑说「开着」而屏幕上没有，就是「点了没反应」——
/// 新建一个把这个问题从根上消掉。
/// </para>
/// <para>
/// <b>它不是一页。</b> 早先它是 <c>LoginPage</c>，被压进导航栈当宿主：一旦弹层丢失，
/// 那一页就永远留在栈顶，之后 <c>Navigate&lt;LoginPage&gt;</c> 会因为「当前页已经是这个页面」
/// 静默不动作 —— 那正是「登录框打不开」的成因。挂在 XamlRoot 上没有栈可卡。
/// </para>
/// <para>
/// <b>不做成 <c>*Page</c> 也不需要调用方导航</b>，所以打开与关闭都不改变用户所在的页面。
/// </para>
/// </remarks>
internal static class LoginDialog
{
    /// <summary>对话框宽度上限。二维码 220 + 内边距，与编辑歌单那类窄框同量级。</summary>
    private const double MaxWidth = 420;

    public static async Task ShowAsync(XamlRoot xamlRoot, ElementTheme theme, LoginViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(xamlRoot);
        ArgumentNullException.ThrowIfNull(viewModel);

        var content = new LoginDialogContent(viewModel);

        // 不设标题：页签本身就是标题，再加一行「登录」是重复的。见 AppDialogs.Create 的说明。
        var dialog = AppDialogs.Create(title: null, xamlRoot, theme, maxWidth: MaxWidth);
        dialog.Content = content;

        void RequestClose() => dialog.Hide();

        // 登录成功后由 ViewModel 通知，而不是在这里 await 一个结果 ——
        // 会话的落地顺序（落盘 → 改内存 → 通知界面）在 Core 里，这里只负责把框关掉。
        void OnLoggedIn(object? sender, EventArgs e) => RequestClose();

        content.CloseRequested += (_, _) => RequestClose();
        viewModel.LoggedIn += OnLoggedIn;

        try
        {
            // 先显示再取码：取码失败时用户至少看得见那个框和它的状态行。
            // 不 await —— 轮询最长 5 分钟，等它等于永远不放框。
            _ = viewModel.ActivateAsync();
            await dialog.ShowAsync();
        }
        finally
        {
            viewModel.LoggedIn -= OnLoggedIn;
            viewModel.Deactivate();
        }
    }
}
