using Bodian.Core.Models;

namespace Bodian.WinUI.Services;

/// <summary>
/// 应用内快捷键的唯一持有者：装在哪、按了什么、算哪个动作。
/// </summary>
/// <remarks>
/// <para>
/// <b>只做应用内快捷键，不注册全局热键。</b> 应用失去焦点时不响应 —— 这正是应用内快捷键
/// 该有的语义，也避免了与其它软件抢占组合键（<c>RegisterHotKey</c> 还要新开消息窗口或
/// 再子类化一次主窗口，本仓库已经在单实例、渲染活动、桌面歌词光标上用了三个不同的子类 id）。
/// </para>
/// <para>
/// 单例。<see cref="MainWindow"/> 按它装 <c>KeyboardAccelerator</c>，设置页按它改键；
/// 两边绑同一份，改完一处另一处立刻跟着变。
/// </para>
/// </remarks>
public interface IShortcutService
{
    /// <summary>当前键位表，顺序与 <see cref="ShortcutActions.All"/> 一致。</summary>
    IReadOnlyList<ShortcutBinding> Bindings { get; }

    /// <summary>键位变了（改键、恢复默认）。<see cref="MainWindow"/> 据此重建加速器。</summary>
    event EventHandler? Changed;

    /// <summary>这个手势触发哪个动作。没有就是 <c>null</c>。</summary>
    ShortcutAction? Match(ShortcutKey key, ShortcutModifiers modifiers);

    /// <summary>
    /// 改键。<b>与别的动作撞车时拒绝</b>，把占用者写到 <paramref name="conflict"/> 里。
    /// </summary>
    /// <remarks>
    /// 由调用方决定撞车时怎么办（当前是提示用户先改那个动作），所以这里不自动顶掉别人。
    /// </remarks>
    bool TryRebind(ShortcutAction action, ShortcutKey key, ShortcutModifiers modifiers, out ShortcutAction? conflict);

    /// <summary>全部恢复出厂键位。</summary>
    void ResetToDefault();

    /// <summary>执行一个动作。这是唯一的执行点，动作全部落在播放器上，shell 因此保持薄。</summary>
    void Invoke(ShortcutAction action);
}
