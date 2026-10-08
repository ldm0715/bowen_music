using Bodian.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Bodian.WinUI.Controls;

/// <summary>
/// 登录对话框的内容体：扫码 / 手机号两个页签，加上右上角的关闭。
/// </summary>
/// <remarks>
/// <b>它只做界面，不碰会话。</b> 取二维码、轮询、换取会话都在
/// <see cref="LoginViewModel"/> 与 <c>IBodianLogin</c> 里；关闭则由
/// <see cref="CloseRequested"/> 交给宿主去关那个 <see cref="ContentDialog"/> ——
/// 内容体拿不到自己的弹窗，也不该去拿。
/// </remarks>
public sealed partial class LoginDialogContent : UserControl
{
    public LoginDialogContent(LoginViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        ViewModel = viewModel;

        InitializeComponent();
    }

    public LoginViewModel ViewModel { get; }

    /// <summary>用户要求关闭（右上角 ✕ 或 Esc）。</summary>
    public event EventHandler? CloseRequested;

    private void OnCloseClick(object sender, RoutedEventArgs e) => RequestClose();

    /// <summary>
    /// Esc 关掉。
    /// </summary>
    /// <remarks>
    /// 关闭按钮是自绘的 ✕，<c>ContentDialog</c> 那条「Esc → 关闭按钮」的内置路径就没有了，
    /// 得自己接一次 —— 否则键盘用户只能靠鼠标点右上角。
    /// </remarks>
    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape)
        {
            return;
        }

        e.Handled = true;
        RequestClose();
    }

    private void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
}
