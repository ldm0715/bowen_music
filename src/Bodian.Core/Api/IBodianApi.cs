using Bodian.Core.Api.Paging;
using Bodian.Core.Models;
using Bodian.Core.Models.Lyrics;

namespace Bodian.Core.Api;

/// <summary>
/// 业务门面。UI 只跟它打交道，不直接碰 DTO，也不自己拼请求。
/// </summary>
/// <remarks>
/// <para>
/// 返回的都是 <c>Bodian.Core.Models</c> 里的公开领域模型 —— 底层 DTO 全是 <c>internal</c>，
/// 这是有意的一道墙（见 <c>Dto/README.md</c>）。
/// </para>
/// <para>
/// 分页游标由<b>调用方持有</b>，门面不自己记状态：这样同一个界面上的两组搜索结果互不干扰，
/// 也便于对同一游标做确定性测试。
/// </para>
/// </remarks>
public interface IBodianApi
{
    /// <summary>
    /// 搜索曲目。
    /// </summary>
    /// <param name="keyword">关键词。空白串会抛 <see cref="ArgumentException"/>。</param>
    /// <param name="cursor">调用方持有的游标，由本方法推进。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <remarks>
    /// 返回值里的 <c>Total</c> <b>不可信</b>（服务端会漏算不可用条目），判断「还有没有下一页」
    /// 只能看 <see cref="PagedCursor.Exhausted"/>。
    /// </remarks>
    Task<PagedResult<Track>> SearchAsync(
        string keyword,
        PagedCursor cursor,
        CancellationToken cancellationToken = default);

    /// <summary>曲目详情。服务端没有这首歌时返回 <c>null</c>。</summary>
    Task<Track?> GetTrackAsync(long musicId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 解析一首歌能不能播、能播多少。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 一次调用完成「授权检查 → 选档 → 取地址 → 核对降级」，因为界面永远需要这三步连起来的结论，
    /// 没有只做前两步的场景。
    /// </para>
    /// <para>
    /// <b>每次点播现取地址，不要缓存结果</b>：CDN 地址带签名且有时效。
    /// </para>
    /// </remarks>
    Task<PlaybackResolution> ResolvePlaybackAsync(
        Track track,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 原始取词：直接要指定版式，返回 Base64 解码后的歌词文本。
    /// </summary>
    /// <param name="musicId">波点的 musicId，不是酷我 rid。</param>
    /// <param name="lrcx"><c>1</c> 逐字 / <c>0</c> 逐行（见 <c>BodianLyricPayload</c>）。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>
    /// 歌词文本。**空串是正常结果**：这首歌没有该版式的轨时服务端就返回空串，业务码仍是 200。
    /// </returns>
    /// <remarks>
    /// 解析交给 <c>BodianLyricParser</c>，本方法只负责取回文本。
    /// 多数调用方要的是 <see cref="GetLyricsAsync"/>。
    /// </remarks>
    Task<string> GetLyricAsync(long musicId, int lrcx, CancellationToken cancellationToken = default);

    /// <summary>
    /// 取这首歌的歌词并解析成统一模型。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 版式按 <see cref="Track.Lyrics"/> 决定：已知有逐字轨就只要逐字版，已知没有就直接要逐行版，
    /// **未知时（搜索结果没有歌词轨信息）先试逐字版，拿到空串再退逐行版**。最多两次请求。
    /// </para>
    /// <para>
    /// 这首歌没有歌词时返回 <see cref="LyricDocument.Empty"/> —— 那是正常结果，不是异常。
    /// 网络与服务端异常**照常抛出**，不要在这里吞掉。
    /// </para>
    /// </remarks>
    Task<LyricDocument> GetLyricsAsync(Track track, CancellationToken cancellationToken = default);
}
