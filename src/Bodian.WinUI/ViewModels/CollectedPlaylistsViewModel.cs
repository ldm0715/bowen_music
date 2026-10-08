using Bodian.Core.Api;
using Bodian.Core.Models;
using Bodian.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 「收藏的歌单」页。
/// </summary>
/// <remarks>
/// <para>
/// <b>歌单 ≠ 专辑</b>：这一页与 <see cref="CollectedAlbumsViewModel"/> 是两回事，
/// 只是两者都读 <c>service/collect/4/list</c> —— 那条端点是混合列表，
/// 这里是按 <c>sourceType == 4</c> 过滤出来的那一半（专辑那一半走 <c>sourceType == 6</c>）。
/// </para>
/// <para>
/// 见 <c>reverse/findings/13-collect-playlist-follow-artist.md</c> 与 <c>10-collect-source.md</c>。
/// </para>
/// </remarks>
public sealed class CollectedPlaylistsViewModel
{
    public CollectedPlaylistsViewModel(IBodianApi api, ViewModeService viewMode,
        ILogger<CollectedPlaylistsViewModel>? logger = null, ICurrentAccount? account = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(viewMode);
        ViewMode = viewMode;

        Playlists = new PagedList<Playlist>(
            api.GetCollectedPlaylistsAsync,
            logger ?? NullLogger<CollectedPlaylistsViewModel>.Instance,
            "收藏的歌单",
            "还没有收藏的歌单。",
            // 「个歌单」当单位用：模板是「共 {n} {单位}」，得到的是「共 30 个歌单」。
            countUnit: "个歌单",
            account: account);
    }

    public PagedList<Playlist> Playlists { get; }

    /// <summary>
    /// 行列表还是封面卡片。状态在单例里 —— 与搜索结果那三个页签、收藏的专辑**共用同一个开关**。
    /// </summary>
    public ViewModeService ViewMode { get; }

    public Task EnsureLoadedAsync(CancellationToken cancellationToken = default) =>
        Playlists.EnsureLoadedAsync(cancellationToken);

    /// <summary>按当前账号重拉第一页。换账号后由页面调，见 <c>IAccountScopedView</c>。</summary>
    public Task ReloadAsync(CancellationToken cancellationToken = default) =>
        Playlists.ReloadAsync(cancellationToken);
}
