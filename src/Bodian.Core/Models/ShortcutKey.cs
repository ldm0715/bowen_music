namespace Bodian.Core.Models;

/// <summary>
/// 可以绑到快捷键上的键。
/// </summary>
/// <remarks>
/// <para>
/// <b>枚举值刻意等于 Win32 的虚拟键码（VK）。</b> <c>Windows.System.VirtualKey</c> 用的是同一套码，
/// 所以界面层「按下的是哪个键」与这里「绑的是哪个键」之间是一次 <c>checked</c> 转换，
/// 不需要维护一张映射表 —— 那种表最容易在新增按键时漏一项，而漏了就表现为某个键绑不上。
/// </para>
/// <para>
/// <b>Escape 不在这里。</b> 它是 shell 与沉浸页共用的「收起 / 退出」键
/// （见 <c>MainWindow.OnShellKeyDown</c> 那条优先级链），收进枚举就等于允许用户把它改掉，
/// 那会同时废掉歌词浮层、播放队列抽屉与搜索面板的关闭路径。不进枚举，就没有这条绑定路径。
/// </para>
/// </remarks>
public enum ShortcutKey
{
    Tab = 0x09,
    Enter = 0x0D,
    Space = 0x20,
    PageUp = 0x21,
    PageDown = 0x22,
    End = 0x23,
    Home = 0x24,
    Left = 0x25,
    Up = 0x26,
    Right = 0x27,
    Down = 0x28,
    Insert = 0x2D,
    Delete = 0x2E,

    D0 = 0x30,
    D1 = 0x31,
    D2 = 0x32,
    D3 = 0x33,
    D4 = 0x34,
    D5 = 0x35,
    D6 = 0x36,
    D7 = 0x37,
    D8 = 0x38,
    D9 = 0x39,

    A = 0x41,
    B = 0x42,
    C = 0x43,
    D = 0x44,
    E = 0x45,
    F = 0x46,
    G = 0x47,
    H = 0x48,
    I = 0x49,
    J = 0x4A,
    K = 0x4B,
    L = 0x4C,
    M = 0x4D,
    N = 0x4E,
    O = 0x4F,
    P = 0x50,
    Q = 0x51,
    R = 0x52,
    S = 0x53,
    T = 0x54,
    U = 0x55,
    V = 0x56,
    W = 0x57,
    X = 0x58,
    Y = 0x59,
    Z = 0x5A,

    Add = 0x6B,
    Subtract = 0x6D,

    F1 = 0x70,
    F2 = 0x71,
    F3 = 0x72,
    F4 = 0x73,
    F5 = 0x74,
    F6 = 0x75,
    F7 = 0x76,
    F8 = 0x77,
    F9 = 0x78,
    F10 = 0x79,
    F11 = 0x7A,
    F12 = 0x7B,

    OemPlus = 0xBB,
    OemMinus = 0xBD,
}

/// <summary>
/// 哪些键能绑、哪些键必须带修饰键。
/// </summary>
/// <remarks>
/// 规则放在 Core 而不是界面层：录制对话框与文件校验都要用同一套判断，
/// 分成两份就会出现「录的时候放行、读回来时又剔掉」这种自相矛盾。
/// </remarks>
public static class ShortcutKeyRules
{
    /// <summary>是不是枚举里认识的键。文件被手工改坏时用得上。</summary>
    public static bool IsBindable(ShortcutKey key) => Enum.IsDefined(key);

    /// <summary>
    /// 不按修饰键也能绑的键。
    /// </summary>
    /// <remarks>
    /// <b>只放行空格与 F1–F12。</b> 其余键（字母、数字、标点）单独按下都会产生字符，
    /// 绑上去等于把打字废掉 —— 用户在搜索框里敲一个「L」就切了歌。
    /// 空格与功能键不产生字符，裸绑是常规做法。
    /// </remarks>
    public static bool AllowWithoutModifier(ShortcutKey key) =>
        key == ShortcutKey.Space || (key >= ShortcutKey.F1 && key <= ShortcutKey.F12);

    /// <summary>修饰键里有没有越界的位。文件被手工改坏时用得上。</summary>
    public static bool IsValidModifiers(ShortcutModifiers modifiers) =>
        (modifiers & ~(ShortcutModifiers.Control | ShortcutModifiers.Alt | ShortcutModifiers.Shift)) == 0;
}
