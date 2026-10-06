using Bodian.WinUI.Services;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 关闭到托盘的判定。
/// </summary>
/// <remarks>
/// <para>
/// 托盘那一套里只有这一段是纯逻辑 —— 不含 Win32、不含窗口、不含 UI 线程，所以能离屏验。
/// 其余部分（<c>AppWindow.Closing</c>、命名互斥量、窗口消息）都要真窗口真进程，只能手动验。
/// </para>
/// <para>
/// 这里验的就是那张真值表本身。<b>两个「放行」的条件都是安全出口</b>，漏掉任何一个
/// 都会变成用户看得见的事故：漏了 <c>exitRequested</c> 是「点了退出关不掉」，
/// 漏了 <c>sessionEnding</c> 是「这个应用阻止了关机」。
/// </para>
/// </remarks>
public sealed class TrayCloseDecisionTests
{
    [Theory]
    // 用户点 ✕，正常情况：拦下来，窗口隐藏、进程继续跑、播放不断。
    [InlineData(false, false, true)]
    // 用户点了托盘菜单的「退出」：必须放行，否则退出永远无效。
    [InlineData(true, false, false)]
    // 系统正在注销/关机：必须放行，否则会阻止关机。
    [InlineData(false, true, false)]
    // 两种情况同时成立：同样是放行。
    [InlineData(true, true, false)]
    public void 只有既非退出请求又非注销时才拦下关闭(bool exitRequested, bool sessionEnding, bool expected)
        => Assert.Equal(expected, TrayCloseDecision.ShouldCancelClose(exitRequested, sessionEnding));

    /// <remarks>
    /// 「退出」被点两次（或菜单关得不干净又触发一次）时，第二次的判定必须与第一次一致 ——
    /// 若第二次又拦下来，窗口就再也关不掉了。<see cref="TrayCloseDecision"/> 是纯函数，
    /// 这条断言的是调用方可以放心重复问它。
    /// </remarks>
    [Fact]
    public void 重复判定结果不变()
    {
        Assert.False(TrayCloseDecision.ShouldCancelClose(exitRequested: true, sessionEnding: false));
        Assert.False(TrayCloseDecision.ShouldCancelClose(exitRequested: true, sessionEnding: false));
    }
}
