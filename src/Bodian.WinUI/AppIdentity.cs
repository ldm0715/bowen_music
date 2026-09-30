namespace Bodian.WinUI;

/// <summary>
/// 应用身份的单一来源：AUMID、开始菜单里的名字、快捷方式文件名。
/// </summary>
/// <remarks>
/// <para>
/// 这些值必须一致：<see cref="AppUserModelId"/> 既要在进程启动时设（<c>App</c> 组合根），
/// 又要写进开始菜单快捷方式的属性里（<c>StartMenuShortcutInstaller</c>）——
/// 两处不一致，shell 就关联不到进程，SMTC 面板上的名字与图标依然不对。
/// </para>
/// <para>
/// <b>不要用官方客户端的 AUMID（<c>Tencent.BodianMusic.PC</c>）。</b> 两个应用共用一个
/// AppMediaId 会让 SMTC 会话互相顶掉（表现为「关掉一个另一个才出现」），而且那等于冒充官方客户端。
/// </para>
/// </remarks>
internal static class AppIdentity
{
    /// <summary>进程级 AppUserModelID。</summary>
    public const string AppUserModelId = "Bodian.WinUI";

    /// <summary>显示名。系统媒体面板与开始菜单里显示的就是它（取自快捷方式的名字）。</summary>
    public const string DisplayName = "Bodian";

    /// <summary>
    /// 开始菜单里的快捷方式文件名。
    /// </summary>
    /// <remarks>
    /// 刻意不叫「波点音乐」：本机装过官方 PC 客户端的话，开始菜单里已经有同名的项
    /// （官方客户端有两条：一条无 AUMID 的普通快捷方式、一条 <c>Tencent.BodianMusic.PC</c>），
    /// 重名会让「正在播的是哪个波点」无法分辨。
    /// </remarks>
    public const string ShortcutFileName = DisplayName + ".lnk";
}
