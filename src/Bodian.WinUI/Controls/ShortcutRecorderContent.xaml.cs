using Bodian.Core.Models;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace Bodian.WinUI.Controls;

/// <summary>
/// 录制一个快捷键手势。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么是内容体而不是一个 <see cref="ContentDialog"/> 子类</b>：本仓库把「要不要弹、
/// 按钮怎么摆」留在页面层（与编辑歌单、清空最近播放同一条规矩）。这里只负责「把按键收下来并校验」。
/// </para>
/// <para>
/// <b>对话框天然解决了「录制时别把动作也触发了」</b>：ContentDialog 挂在窗口的 popup root 下，
/// 不在 <c>ShellRoot</c> 这棵子树里，所以 shell 上那批 <c>KeyboardAccelerator</c> 解析不到它。
/// </para>
/// </remarks>
public sealed partial class ShortcutRecorderContent : UserControl
{
    private ShortcutAction _action;
    private Func<ShortcutAction, ShortcutKey, ShortcutModifiers, ShortcutAction?> _conflictOf = (_, _, _) => null;

    public ShortcutRecorderContent()
    {
        InitializeComponent();

        // handledEventsToo: true 是必须的 —— 空格与 Tab 会被对话框的焦点导航先吃掉，
        // 不带这一项就永远收不到它们，而空格恰恰是默认键位之一。
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDown), handledEventsToo: true);
    }

    /// <summary>已经收下一个合法手势。设置页据此把「确定」按钮点亮。</summary>
    public event EventHandler? Completed;

    /// <summary>收下的主键。还没收到就是 <c>null</c>。</summary>
    public ShortcutKey? Key { get; private set; }

    /// <summary>收下主键时按着的修饰键。</summary>
    public ShortcutModifiers Modifiers { get; private set; }

    /// <summary>
    /// 交给它要改的动作，以及一个「这个手势被谁占了」的查询。
    /// </summary>
    /// <remarks>
    /// 冲突查询由调用方注入而不是在这里注入服务：内容是控件，控件的构造函数拿不到容器，
    /// 传一个委托比传一个服务更贴合本仓库的接线方式。
    /// </remarks>
    public void Configure(
        ShortcutAction action,
        Func<ShortcutAction, ShortcutKey, ShortcutModifiers, ShortcutAction?> conflictOf)
    {
        _action = action;
        _conflictOf = conflictOf;

        HintText.Text = $"为「{action.DisplayName()}」按下新的组合键。按 Esc 取消。";
        GestureText.Text = "等待按键…";
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        // 焦点必须在录制区上，否则按键全被对话框的按钮收走。
        Focus(FocusState.Programmatic);
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        // 一律吃掉：不放行的话 Tab 会把焦点移走、空格会去按默认按钮。
        args.Handled = true;

        var key = args.Key;

        // 只按修饰键时先等主键，别急着报错。
        if (key is VirtualKey.Control or VirtualKey.Menu or VirtualKey.Shift
            or VirtualKey.LeftWindows or VirtualKey.RightWindows)
        {
            return;
        }

        var modifiers = CurrentModifiers();

        if (key == VirtualKey.Escape)
        {
            Reject("Esc 不能绑定：它是收起播放队列、歌单浮层与搜索面板的共用键。");
            return;
        }

        if (!ShortcutKeyRules.IsBindable((ShortcutKey)(int)key))
        {
            Reject("这个键不能绑定，换一个试试。");
            return;
        }

        var shortcutKey = (ShortcutKey)(int)key;

        if (modifiers == ShortcutModifiers.None && !ShortcutKeyRules.AllowWithoutModifier(shortcutKey))
        {
            Reject($"「{ShortcutGestureLabel.KeyName(shortcutKey)}」要配合 Ctrl、Alt 或 Shift 使用，否则打字时会误触发。");
            return;
        }

        if (_conflictOf(_action, shortcutKey, modifiers) is { } occupied)
        {
            Reject($"已被「{occupied.DisplayName()}」占用，先把它改到别的键上。");
            return;
        }

        Key = shortcutKey;
        Modifiers = modifiers;
        MessageText.Visibility = Visibility.Collapsed;
        GestureText.Text = ShortcutGestureLabel.Format(shortcutKey, modifiers);
        Completed?.Invoke(this, EventArgs.Empty);
    }

    /// <remarks>
    /// 用键盘状态查而不是自己记账：用户可能在别处先按住 Ctrl 再切过来，
    /// 那时没有经过本控件的 KeyDown，自己攒的状态就是错的。
    /// </remarks>
    private static ShortcutModifiers CurrentModifiers()
    {
        var modifiers = ShortcutModifiers.None;
        if (IsDown(VirtualKey.Control)) modifiers |= ShortcutModifiers.Control;
        if (IsDown(VirtualKey.Menu)) modifiers |= ShortcutModifiers.Alt;
        if (IsDown(VirtualKey.Shift)) modifiers |= ShortcutModifiers.Shift;
        return modifiers;
    }

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private void Reject(string message)
    {
        MessageText.Text = message;
        MessageText.Visibility = Visibility.Visible;
    }
}
