using Bowen.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Bowen.WinUI.ViewModels;

/// <summary>
/// 设置页「存储」分区里的一行：一项占用 + 一个清空按钮。
/// </summary>
public sealed partial class StorageRowViewModel : ObservableObject
{
    public StorageRowViewModel(StorageItemKind kind, string title, string description, bool isLast = false)
    {
        Kind = kind;
        Title = title;
        Description = description;
        IsLast = isLast;
    }

    public StorageItemKind Kind { get; }

    public string Title { get; }

    public string Description { get; }

    /// <summary>末行不画分割线。</summary>
    public bool IsLast { get; }

    /// <summary>占用文案，如 <c>12.3 MB</c>。还没量出来时是一段占位。</summary>
    [ObservableProperty]
    public partial string SizeText { get; set; } = "正在统计…";

    /// <summary>正在清理。清理期间按钮禁用，避免连点。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanClear))]
    public partial bool IsBusy { get; set; }

    /// <summary>上一次清理的结果文案。空串表示不显示。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    public partial string ResultText { get; set; } = "";

    /// <summary>这一项当前有没有可清理的内容。占用为 0 B 时为假。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanClear))]
    public partial bool HasContent { get; set; }

    /// <summary>
    /// 「清理」按钮能不能点。给绑定用 —— <c>x:Bind</c> 不接受取反。
    /// </summary>
    /// <remarks>
    /// 0 B 时禁用：此时再点只会用「已清理 0 B」覆盖上一次的结果文案，
    /// 用户看到的是「刚清的提示没了」，会以为清理失败。
    /// </remarks>
    public bool CanClear => !IsBusy && HasContent;

    /// <summary>有没有结果文案可显示。同上，避免在 XAML 里写比较。</summary>
    public bool HasResult => ResultText.Length > 0;
}
