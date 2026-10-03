using Bodian.Core.Api;
using Bodian.Core.Models;
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
    public CollectedPlaylistsViewModel(IBodianApi api, ILogger<CollectedPlaylistsViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(api);

        Playlists = new PagedList<Playlist>(
            api.GetCollectedPlaylistsAsync,
            logger ?? NullLogger<CollectedPlaylistsViewModel>.Instance,
            "收藏的歌单",
            "还没有收藏的歌单。");
    }

    public PagedList<Playlist> Playlists { get; }

    public Task EnsureLoadedAsync(CancellationToken cancellationToken = default) =>
        Playlists.EnsureLoadedAsync(cancellationToken);
}
