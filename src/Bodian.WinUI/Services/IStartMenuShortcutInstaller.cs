namespace Bodian.WinUI.Services;

/// <summary>
/// 保证开始菜单里有指向本应用的快捷方式。
/// </summary>
/// <remarks>
/// <b>为什么需要它</b>：unpackaged 应用没有 identity，系统媒体面板（SMTC）默认显示 exe 文件名，
/// 而且拿不到应用图标。名字与图标<b>取自开始菜单里的快捷方式</b>，所以这条是「面板上显示什么名字」
/// 的前提，不是可有可无的润色。
/// </remarks>
public interface IStartMenuShortcutInstaller
{
    /// <summary>确保快捷方式存在且指向当前 exe。幂等，失败只记日志（不影响播放）。</summary>
    void EnsureInstalled();
}
