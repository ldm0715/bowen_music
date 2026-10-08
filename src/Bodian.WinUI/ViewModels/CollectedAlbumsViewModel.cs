using Bodian.Core.Api;
using Bodian.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 「收藏的专辑」页。
/// </summary>
/// <remarks>
/// 数据源是移动端的<b>收藏歌单</b>端点（<c>service/collect/4/list</c>）——
/// 按用户实测，它的内容与官方桌面端「收藏专辑」的效果一致。
/// 官方桌面端自己那条路（<c>service/collect/6/list</c>）已弃用：实测它对本项目在测的账号
/// 返回 200 但 <c>data</c> 是空对象。
/// </remarks>
public sealed class CollectedAlbumsViewModel
{
    public CollectedAlbumsViewModel(IBodianApi api, ViewModeService viewMode,
        ILogger<CollectedAlbumsViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(viewMode);
        ViewMode = viewMode;

        Albums = new PagedList<Album>(
            api.GetCollectedAlbumsAsync,
            logger ?? NullLogger<CollectedAlbumsViewModel>.Instance,
            "收藏的专辑",
            "还没有收藏的专辑。",
            // 单位与 Formats.AlbumTotal 一致：「共 N 张」，不再是泛指的「N 项」。
            countUnit: "张");
    }

    public PagedList<Album> Albums { get; }

    /// <summary>
    /// 行列表还是封面卡片。状态在单例里 —— 与搜索结果那三个页签、收藏的歌单**共用同一个开关**。
    /// </summary>
    public ViewModeService ViewMode { get; }

    public Task EnsureLoadedAsync(CancellationToken cancellationToken = default) =>
        Albums.EnsureLoadedAsync(cancellationToken);

    /// <summary>按当前账号重拉第一页。换账号后由页面调，见 <c>IAccountScopedView</c>。</summary>
    public Task ReloadAsync(CancellationToken cancellationToken = default) =>
        Albums.ReloadAsync(cancellationToken);
}
