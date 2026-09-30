using Bodian.Core.Api.Paging;
using Bodian.Core.Models;

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
}
