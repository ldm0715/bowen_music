using Bodian.Core.Models;
using Bodian.Core.Services;
using Bodian.Core.Services.Abstractions;

namespace Bodian.WinUI.Services;

/// <summary>
/// 列表级工具栏的批量动作出口。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="ITrackNavigator"/> / <see cref="IQueueSink"/> 同一套手法：工具栏是 XAML 实例化的控件，
/// 构造函数拿不到 DI 容器，所以服务走 App 资源（键 <c>BodianTrackActions</c>），
/// 与 <c>TrackMoreButton</c> 共用同一个实例。
/// </para>
/// <para>
/// <b>进度与取消都从这里透传下去</b>：一次批量可能是几十上百个串行请求，
/// 界面必须能显示「正在加入 37/150」并中途叫停。
/// </para>
/// </remarks>
public interface ITrackBatchActions
{
    /// <summary>把一批曲目加到播放队列末尾，队列内按 Id 去重，不打断正在播的那首。</summary>
    /// <returns>实际追加的条数；全都已在队列里时返回 <c>0</c>。</returns>
    Task<int> AddToQueueAsync(IReadOnlyList<Track> tracks, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量喜欢 / 取消喜欢。
    /// </summary>
    /// <param name="liked">
    /// <c>true</c> 是喜欢（已经在「我喜欢的」里的不重复发请求）；<c>false</c> 是移出喜欢。
    /// </param>
    Task<LikedSongsBatchOutcome> SetLikedManyAsync(
        IReadOnlyList<Track> tracks,
        bool liked = true,
        IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>拉一次自建歌单，供「批量加入歌单」的选择弹窗用。</summary>
    /// <exception cref="InvalidOperationException">未登录。</exception>
    Task<IReadOnlyList<Playlist>> GetCreatedPlaylistsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 把一批曲目加进指定歌单。
    /// </summary>
    /// <exception cref="InvalidOperationException">未登录，或写到一半登录状态变了。</exception>
    Task<BatchWriteResult> AddToPlaylistAsync(
        long playlistId,
        IReadOnlyList<Track> tracks,
        IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 把一批曲目从指定歌单里移出。
    /// </summary>
    /// <remarks>
    /// <b>只对自建歌单用这条</b>：「我喜欢的」要走 <see cref="SetLikedManyAsync"/>，
    /// 那条路会同步喜欢状态的缓存，直接调这里会让缓存和歌单对不上。
    /// </remarks>
    /// <exception cref="InvalidOperationException">未登录，或写到一半登录状态变了。</exception>
    Task<BatchWriteResult> RemoveFromPlaylistAsync(
        long playlistId,
        IReadOnlyList<Track> tracks,
        IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>发一条短提示。</summary>
    void ShowNotice(string message, NoticeSeverity severity = NoticeSeverity.Informational);
}
