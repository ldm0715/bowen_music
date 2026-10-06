namespace Bodian.WinUI.Services;

/// <summary>
/// 主窗口这次关闭该不该被拦成「隐藏到托盘」。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么单独抽一层。</b> 托盘这一套里，只有这段判断不含 Win32、不含窗口、不含 UI 线程，
/// 所以它是唯一能被离线单测覆盖的部分 —— 测试项目用 <c>&lt;Compile Include&gt;</c> 把本文件
/// 链过去（照 <c>Bodian.Core.Tests.csproj</c> 里链接 ViewModel 的既有做法）。
/// 其余部分（<c>AppWindow.Closing</c>、命名互斥量、窗口消息）都要真窗口真进程，只能手动验。
/// </para>
/// <para>
/// 写成纯函数而不是 <c>MainWindow</c> 上的方法：输入是显式的两个 bool，测试不必造一个窗口来喂它。
/// </para>
/// </remarks>
internal static class TrayCloseDecision
{
    /// <summary>
    /// <c>true</c> 表示拦下这次关闭（窗口隐藏、进程继续跑、播放不断）。
    /// </summary>
    /// <param name="exitRequested">
    /// 用户已经明确要求退出 —— 目前只有托盘菜单的「退出」会置位。
    /// 这是整个应用唯一真正结束进程的入口，所以它一旦为真就绝不能再拦。
    /// </param>
    /// <param name="sessionEnding">
    /// 系统正在注销或关机。这时<b>必须放行</b>：拦下来会变成「这个应用阻止了关机」，
    /// 那比多留一个窗口严重得多。
    /// </param>
    /// <remarks>
    /// 两个条件都是「放行」，所以结果是它们的或非。写成或非而不是嵌套 <c>if</c>，
    /// 是为了让真值表一眼可见 —— 这个函数的全部内容就是那张真值表。
    /// </remarks>
    internal static bool ShouldCancelClose(bool exitRequested, bool sessionEnding)
        => !exitRequested && !sessionEnding;
}
