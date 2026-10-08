using Bodian.Core.Api;
using Bodian.Core.Models;
using Bodian.Core.Services.Abstractions;
using Bodian.WinUI.Playback;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 「我喜欢的」页。
/// </summary>
/// <remarks>
/// <b>侧栏六个入口里唯一全链路实测验证过的</b>：P0 已对
/// <c>service/playlist/fond</c> → <c>service/playlist/{id}/musicList</c> 做过完整往返
/// （取歌单 → 加歌 → 读回 → 删歌 → 读回）。所以这一页也是唯一不依赖静态推断的页面。
/// </remarks>
public sealed partial class FavoritesViewModel : PlaylistTracksViewModel
{
    private readonly IBodianApi _api;

    public FavoritesViewModel(
        IBodianApi api,
        PlaybackCoordinator coordinator,
        ILogger<FavoritesViewModel>? logger = null,
        ICurrentAccount? account = null)
        : base(api, coordinator, logger ?? NullLogger<FavoritesViewModel>.Instance, account)
    {
        ArgumentNullException.ThrowIfNull(api);

        _api = api;
        Title = "我喜欢的";
    }

    /// <summary>
    /// 没有红心歌单时的说明。
    /// </summary>
    /// <remarks>
    /// 这是**正常状态**：账号从来没有点过红心时，服务端返回的对象就没有 <c>id</c>
    /// （文档 2.3）。所以文案说的是「没有这个歌单」而不是「加载失败」。
    /// </remarks>
    protected override string MissingText => "这个账号还没有「我喜欢的」歌单。";

    /// <summary>匿名时这个页面取不到东西（`playlist/fond` 要会话），要说清是「没登录」而不是「空的」。</summary>
    protected override string RequiresSignInText => "登录后可查看「我喜欢的」。";

    protected override string EmptyText => "「我喜欢的」里还没有歌。";

    protected override Task<Playlist?> ResolvePlaylistAsync(CancellationToken cancellationToken) =>
        _api.GetLikedPlaylistAsync(cancellationToken);
}
