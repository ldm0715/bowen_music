using Bodian.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 设置页「快捷键」分区里的一行。
/// </summary>
public sealed partial class ShortcutRowViewModel : ObservableObject
{
    public ShortcutRowViewModel(ShortcutAction action, bool isLast)
    {
        Action = action;
        Title = action.DisplayName();
        IsLast = isLast;
    }

    public ShortcutAction Action { get; }

    /// <summary>行标题，如「下一首」。</summary>
    public string Title { get; }

    /// <summary>末行不画分割线。</summary>
    public bool IsLast { get; }

    /// <summary>当前手势文案，如 <c>Ctrl+→</c>。改键后由 <see cref="ShortcutSettingsViewModel"/> 刷新。</summary>
    [ObservableProperty]
    public partial string GestureText { get; set; } = "";
}
