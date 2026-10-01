using System.Collections.ObjectModel;
using Bodian.Core.Api;
using Bodian.Core.Api.Paging;
using Bodian.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 乐库的一个大类：子类 + 专辑列表。
/// </summary>
/// <remarks>
/// <para>
/// <b>大类本身不请求</b> —— 乐库首页那次 <c>navigation</c> 已经把子类一起带回来了。
/// 但**每个子类的专辑列表要单独请求**（<c>play/music/library/albums</c>），
/// 所以这一页有加载状态、也会失败。
/// </para>
/// <para>
/// 切子类或切排序都要**重置分页游标**，所以用 <see cref="PagedList{T}.ReloadAsync"/>
/// 而不是 <c>EnsureLoadedAsync</c>（后者只在第一次生效）。
/// </para>
/// </remarks>
public sealed partial class LibraryCategoryViewModel : ObservableObject
{
    private readonly IBodianApi _api;

    public LibraryCategoryViewModel(IBodianApi api, MusicCategoryGroup group, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(group);

        _api = api;
        Group = group;

        foreach (var child in group.Children)
        {
            Children.Add(child);
        }

        Albums = new PagedList<Album>(
            FetchAsync,
            logger ?? NullLogger<LibraryCategoryViewModel>.Instance,
            $"乐库「{group.Name}」",
            "这个子类下暂时没有专辑。");

        // 默认选中第一个子类 —— 进页面就该有内容，不该是一片空。
        SelectedChild = Children.FirstOrDefault();
    }

    /// <summary>这一页要展示的大类。</summary>
    public MusicCategoryGroup Group { get; }

    /// <summary>大类的子类。</summary>
    public ObservableCollection<MusicCategoryChild> Children { get; } = [];

    /// <summary>专辑列表，按当前选中的子类与排序加载。</summary>
    public PagedList<Album> Albums { get; }

    [ObservableProperty]
    public partial MusicCategoryChild? SelectedChild { get; set; }

    /// <summary>
    /// 排序。界面上是「精品 / 最新」两个 tab。
    /// </summary>
    /// <remarks>
    /// <b>这个值直接影响请求</b>：服务端要的是字符串 <c>"1"</c>/<c>"2"</c>，
    /// 传错的话它回 <c>200</c> + 空数据、不报错 —— 界面上看起来就是「这个子类没有专辑」。
    /// 见 <see cref="MusicLibSort"/>。
    /// </remarks>
    [ObservableProperty]
    public partial MusicLibSort SelectedSort { get; set; } = MusicLibSort.Curated;

    partial void OnSelectedChildChanged(MusicCategoryChild? value)
    {
        if (value is not null)
        {
            _ = Albums.ReloadAsync();
        }
    }

    partial void OnSelectedSortChanged(MusicLibSort value) => _ = Albums.ReloadAsync();

    /// <summary>取一页专辑。<b>用一个读当前选中项的闭包</b>，才会跟着选择走。</summary>
    private Task<PagedResult<Album>> FetchAsync(PagedCursor cursor, CancellationToken cancellationToken)
    {
        if (SelectedChild is not { } child)
        {
            return Task.FromResult(new PagedResult<Album>([], cursor.Offset, cursor.PageSize, null));
        }

        // pTypeId 是大类的 id、cTypeId 是子类的 id —— 都不是 ptypeId 那个字段。
        return _api.GetMusicLibraryAlbumsAsync(
            Group.Id,
            child.Id,
            SelectedSort,
            cursor,
            cancellationToken);
    }
}
