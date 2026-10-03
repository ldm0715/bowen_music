using Bodian.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Bodian.WinUI.Controls;

/// <summary>编辑歌单时的可选标签。一个 chip 对应 <c>service/category/list</c> 里的一个子分类。</summary>
/// <remarks>
/// <see cref="ObservableObject"/> 而不是裸属性：选满 3 个之后再点第 4 个会被回退，
/// 界面得跟着弹回去，所以 <see cref="IsSelected"/> 必须发通知。
/// </remarks>
public sealed partial class TagChip : ObservableObject
{
    public TagChip(MusicCategory category)
    {
        ArgumentNullException.ThrowIfNull(category);

        Id = category.Id;
        Name = category.Name;
    }

    public long Id { get; }

    public string Name { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>还原成一个领域模型，交给 <c>SaveEditAsync</c>。</summary>
    public MusicCategory ToCategory() => new(Id, Name);
}
