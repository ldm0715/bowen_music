using System.Collections.ObjectModel;
using Bodian.Core.Models;
using Bodian.WinUI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 设置页「快捷键」分区。
/// </summary>
/// <remarks>
/// <para>
/// 只做展示与转发：真正的键位表在单例 <see cref="IShortcutService"/> 里，改键也走它。
/// 这里另存一份就会多出一处要同步的东西。
/// </para>
/// <para>
/// <b>界面不在这里弹。</b> 「改键」要开对话框，而「要不要弹、按钮怎么摆」是本仓库明确
/// 留在页面层的一类决策（与「我喜欢的」页那个清空确认同一条理由）。
/// </para>
/// </remarks>
public sealed partial class ShortcutSettingsViewModel : ObservableObject
{
    private readonly IShortcutService _shortcuts;

    public ShortcutSettingsViewModel(IShortcutService shortcuts)
    {
        ArgumentNullException.ThrowIfNull(shortcuts);

        _shortcuts = shortcuts;

        for (var index = 0; index < ShortcutActions.All.Length; index++)
        {
            Rows.Add(new ShortcutRowViewModel(
                ShortcutActions.All[index],
                isLast: index == ShortcutActions.All.Length - 1));
        }

        Refresh();
    }

    public ObservableCollection<ShortcutRowViewModel> Rows { get; } = [];

    /// <summary>把每一行的手势文案对齐到当前键位表。改键之后、以及页面每次进入时调。</summary>
    public void Refresh()
    {
        foreach (var row in Rows)
        {
            row.GestureText = _shortcuts.Bindings
                .FirstOrDefault(binding => binding.Action == row.Action)?.GestureText ?? "未设置";
        }
    }

    /// <summary>这个手势是不是已经被别的动作占了。改键对话框用它做即时校验。</summary>
    public ShortcutAction? ConflictOf(ShortcutAction action, ShortcutKey key, ShortcutModifiers modifiers) =>
        _shortcuts.Bindings
            .FirstOrDefault(binding => binding.Action != action && binding.Gesture == (key, modifiers))
            ?.Action;

    /// <summary>改键。撞车时返回 false，并把占用者写进 <paramref name="conflict"/>。</summary>
    public bool TryRebind(ShortcutAction action, ShortcutKey key, ShortcutModifiers modifiers, out ShortcutAction? conflict)
    {
        var ok = _shortcuts.TryRebind(action, key, modifiers, out conflict);
        if (ok)
        {
            Refresh();
        }

        return ok;
    }

    [RelayCommand]
    private void ResetAll()
    {
        _shortcuts.ResetToDefault();
        Refresh();
    }
}
