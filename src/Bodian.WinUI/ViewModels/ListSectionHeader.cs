using Bodian.Core.Models;

namespace Bodian.WinUI.ViewModels;

/// <summary>单个虚拟化列表中的分组标题；不在标题下面嵌套另一张列表。</summary>
public sealed class ListSectionHeader
{
    public string Title { get; set; } = "";
    public SearchResultCategory Category { get; set; }
}
