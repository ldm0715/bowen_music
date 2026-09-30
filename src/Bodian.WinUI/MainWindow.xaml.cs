using Bodian.Core.Api;
using Bodian.Core.Services.Abstractions;
using Microsoft.UI.Xaml;

namespace Bodian.WinUI;

/// <summary>
/// P1 的主窗口。它的作用不是「界面」，而是**零网络地证明 DI 链真的通了**。
/// </summary>
/// <remarks>
/// 窗口上显示的 devid 来自 <see cref="IDeviceIdentity"/> 的默认实现，
/// 也就是 <c>%LOCALAPPDATA%\Bodian\devid.txt</c>——
/// 它必须与 P0 探针用的是**同一个值**。若这里显示的是新生成的标识，
/// 说明路径被改过，而那在账号风控看来是异常信号。
/// </remarks>
public sealed partial class MainWindow : Window
{
    public MainWindow(IDeviceIdentity device, BodianSession session)
    {
        InitializeComponent();

        Title = $"波点音乐（非官方客户端）— devid {device.Value}";

        DeviceIdText.Text = $"devid（与 P0 探针必须是同一个值）：{device.Value}";
        SessionText.Text = session.IsAuthenticated
            ? $"会话：已登录 uid={session.Uid}"
            : "会话：未登录（P2 接扫码登录）";
    }
}
