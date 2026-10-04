using Bodian.Core.Models;

namespace Bodian.WinUI.Services;

/// <summary>
/// 曲目行「更多」菜单里的两个跳转。
/// </summary>
/// <remarks>
/// <b>抽成接口是为了让 <c>TrackActionsViewModel</c> 能进离线测试工程</b>：那边只认 Core 的模型，
/// 一旦 VM 直接依赖 <c>INavigationService</c> 与页面工厂，就绑上了 WinUI 类型、编不进测试。
/// 真正的实现（压栈、页面工厂）在 <c>TrackActionsService</c>。
/// </remarks>
public interface ITrackNavigator
{
    /// <summary>打开歌手详情。<b>压栈，不换根</b> —— 侧栏该继续高亮原来的那一页。</summary>
    void OpenArtist(Artist artist);

    /// <summary>打开专辑详情。同样是压栈。</summary>
    void OpenAlbum(Album album);

    /// <summary>
    /// 打开这首歌的 MV 页。同样是压栈。
    /// </summary>
    /// <remarks>
    /// 目标页自己负责去取 MV 地址（<c>GetMvInfoAsync</c>）—— 这里只传曲目，
    /// 因为取地址要发请求，不该在点菜单时阻塞。
    /// </remarks>
    void OpenMv(Track track);
}
