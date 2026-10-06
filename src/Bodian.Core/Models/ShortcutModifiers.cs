namespace Bodian.Core.Models;

/// <summary>
/// 快捷键的修饰键组合。
/// </summary>
/// <remarks>
/// <b>刻意不含 Windows 键。</b> Win 组合被系统大范围占用（Win+方向、Win+数字、Win+D…），
/// 绑上去多半会被系统先吃掉，表现为「设了没反应」。与其让用户去踩，不如不提供。
/// </remarks>
[Flags]
public enum ShortcutModifiers
{
    None = 0,

    Control = 1,

    Alt = 2,

    Shift = 4,
}
