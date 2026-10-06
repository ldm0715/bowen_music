using CommunityToolkit.Mvvm.ComponentModel;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 设置页左侧分类栏的一项。
/// </summary>
/// <remarks>
/// <para>
/// <b>刻意是普通类而不是 <c>record</c></b>：这个类型要当 <c>x:DataType</c>，
/// 而 XAML 类型信息生成器会为它生成无参构造与属性 setter，位置参数构造的 record
/// 在那条路上出过问题（见 <c>docs/ui-refresh.md</c> 里 CS8852 那两条报错记录）。
/// </para>
/// <para>
/// <see cref="ShowLabel"/> 是要通知的属性：窗口变窄时分类栏收成图标轨，
/// 页面的代码里逐项改它，图标下面的文字因此整批隐藏 —— 这样不必让模板去够页面级的状态。
/// </para>
/// </remarks>
public sealed partial class SettingsCategoryItem : ObservableObject
{
    public SettingsCategoryItem(string label, string iconKey)
    {
        Label = label;
        IconKey = iconKey;
    }

    /// <summary>分类名。</summary>
    public string Label { get; set; }

    /// <summary>图标资源键（<c>Themes/Icons.xaml</c> 的 <c>x:Key</c>），由模板交给 <c>Formats.IconPaths</c>。</summary>
    public string IconKey { get; set; }

    /// <summary>是否显示分类名。图标轨态下为 false，只留图标与 ToolTip。</summary>
    [ObservableProperty]
    public partial bool ShowLabel { get; set; } = true;
}
