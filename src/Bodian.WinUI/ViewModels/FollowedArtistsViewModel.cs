using Bodian.Core.Api;
using Bodian.Core.Api.Paging;
using Bodian.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 「关注的歌手」页。
/// </summary>
/// <remarks>
/// <para>
/// 数据来自 <c>service/collect/7/list</c>（<c>source = 7</c>），与收藏歌单那条是同一族端点，
/// 见 <c>docs/collect-follow.md</c>。
/// </para>
/// <para>
/// <b>这条端点没有分页</b>（官方固定 <c>rn = 400</c>，一次全量），所以下面那个 fetch 委托
/// 拿到结果后直接把游标标到底 —— 详见方法上的注释。
/// </para>
/// </remarks>
public sealed class FollowedArtistsViewModel
{
    private readonly IBodianApi _api;

    public FollowedArtistsViewModel(IBodianApi api, ViewModeService viewMode,
        ILogger<FollowedArtistsViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(viewMode);

        _api = api;
        ViewMode = viewMode;

        Artists = new PagedList<Artist>(
            FetchAsync,
            logger ?? NullLogger<FollowedArtistsViewModel>.Instance,
            "关注的歌手",
            "还没有关注的歌手。",
            // 「位歌手」当单位用：模板是「共 {n} {单位}」，得到的是「共 12 位歌手」。
            countUnit: "位歌手");
    }

    public PagedList<Artist> Artists { get; }

    /// <summary>
    /// 行列表还是封面卡片。状态在单例里 —— 与搜索结果那三个页签、收藏的专辑/歌单**共用同一个开关**。
    /// </summary>
    public ViewModeService ViewMode { get; }

    /// <summary>首次进入时调，已经加载过就什么都不做。</summary>
    public Task EnsureLoadedAsync(CancellationToken cancellationToken = default) =>
        Artists.EnsureLoadedAsync(cancellationToken);

    /// <summary>
    /// 重新拉一次。给「从歌手详情返回」用 —— 详情页里可以取消关注，回来列表得跟着变。
    /// </summary>
    public Task RefreshAsync(CancellationToken cancellationToken = default) =>
        Artists.ReloadAsync(cancellationToken);

    /// <summary>
    /// 一次取全部关注歌手，并声明这就是最后一页。
    /// </summary>
    /// <remarks>
    /// <b><c>Advance(0)</c> 是游标约定里「到底」的写法</b>（收到 0 条即 <c>Exhausted</c>）：
    /// 推进游标是 fetch 委托的责任，<see cref="PagedList{T}"/> 自己从不调它。
    /// 不标到底的话 <c>HasMore</c> 会停在 true，列表末尾的自动翻页就会再拉一次并追加一份重复的。
    /// </remarks>
    private async Task<PagedResult<Artist>> FetchAsync(PagedCursor cursor, CancellationToken cancellationToken)
    {
        var artists = await _api.GetFollowedArtistsAsync(cancellationToken).ConfigureAwait(true);

        cursor.Advance(0);

        return new PagedResult<Artist>(artists, cursor.Offset, cursor.PageSize, artists.Count);
    }
}
