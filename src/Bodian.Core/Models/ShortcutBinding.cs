namespace Bodian.Core.Models;

/// <summary>
/// 一个动作绑到哪个手势上。
/// </summary>
/// <param name="Action">被触发的动作。</param>
/// <param name="Key">主键。</param>
/// <param name="Modifiers">修饰键组合。</param>
public sealed record ShortcutBinding(ShortcutAction Action, ShortcutKey Key, ShortcutModifiers Modifiers)
{
    /// <summary>这个手势本身（不含它触发什么），用于查重。</summary>
    public (ShortcutKey Key, ShortcutModifiers Modifiers) Gesture => (Key, Modifiers);

    /// <summary>给界面显示的手势文案，如 <c>Ctrl+←</c>。</summary>
    public string GestureText => ShortcutGestureLabel.Format(Key, Modifiers);
}
