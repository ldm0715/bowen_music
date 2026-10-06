namespace Bodian.Core.Models;

/// <summary>
/// 手势的显示文案。
/// </summary>
/// <remarks>
/// 放在 Core 而不是界面层：设置页的键位列与录制对话框里的提示要用同一份，
/// 分两份就会出现「列表里写着 Ctrl+←、对话框里写着 Control+Left」这种不一致。
/// </remarks>
public static class ShortcutGestureLabel
{
    /// <summary>把键与修饰键拼成 <c>Ctrl+←</c> 这样的文案。</summary>
    public static string Format(ShortcutKey key, ShortcutModifiers modifiers)
    {
        var parts = new List<string>(4);
        // 顺序固定为 Ctrl / Alt / Shift：与 Windows 上其它软件的写法一致，
        // 而且不会因为用户按下的先后而变。
        if (modifiers.HasFlag(ShortcutModifiers.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ShortcutModifiers.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ShortcutModifiers.Shift)) parts.Add("Shift");

        parts.Add(KeyName(key));
        return string.Join("+", parts);
    }

    /// <summary>单个键的显示名。方向键等用符号，与应用的图标语言一致。</summary>
    public static string KeyName(ShortcutKey key) => key switch
    {
        ShortcutKey.Space => "空格",
        ShortcutKey.Left => "←",
        ShortcutKey.Up => "↑",
        ShortcutKey.Right => "→",
        ShortcutKey.Down => "↓",
        ShortcutKey.Enter => "回车",
        ShortcutKey.Tab => "Tab",
        ShortcutKey.PageUp => "PageUp",
        ShortcutKey.PageDown => "PageDown",
        ShortcutKey.Home => "Home",
        ShortcutKey.End => "End",
        ShortcutKey.Insert => "Insert",
        ShortcutKey.Delete => "Delete",
        ShortcutKey.Add => "小键盘 +",
        ShortcutKey.Subtract => "小键盘 -",
        ShortcutKey.OemPlus => "=",
        ShortcutKey.OemMinus => "-",
        >= ShortcutKey.D0 and <= ShortcutKey.D9 => ((char)('0' + (key - ShortcutKey.D0))).ToString(),
        >= ShortcutKey.A and <= ShortcutKey.Z => ((char)('A' + (key - ShortcutKey.A))).ToString(),
        >= ShortcutKey.F1 and <= ShortcutKey.F12 => $"F{key - ShortcutKey.F1 + 1}",
        _ => key.ToString(),
    };
}
