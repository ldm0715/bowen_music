using Bodian.Core.Models;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;

namespace Bodian.WinUI.ViewModels;

/// <summary>单个虚拟化列表中的分组标题；不在标题下面嵌套另一张列表。</summary>
public sealed class ListSectionHeader
{
    public string Title { get; set; } = "";
    public SearchResultCategory Category { get; set; }

    /// <summary>标题右侧的刷新按钮。<b>为 null 的标题不显示按钮。</b></summary>
    /// <remarks>
    /// 目前只有发现页那两个单曲推荐模块会填它（见 <see cref="DiscoverViewModel"/>）——
    /// 命令由数据源构造，标题只是载体。
    /// </remarks>
    public IAsyncRelayCommand? RefreshCommand { get; set; }

    /// <summary>刷新按钮显不显示。</summary>
    /// <remarks>
    /// 必须是 <see cref="Visibility"/> 而不是 bool：x:Bind 不做 bool 到 Visibility 的隐式转换，
    /// 绑 bool 会报 WMC1121（同 <c>Formats.PayLabelVisibility</c> 那处）。
    /// </remarks>
    public Visibility RefreshVisibility =>
        RefreshCommand is null ? Visibility.Collapsed : Visibility.Visible;
}
