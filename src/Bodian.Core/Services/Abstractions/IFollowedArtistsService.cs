namespace Bodian.Core.Services.Abstractions;

/// <summary>关注 / 取消关注的结果。</summary>
public enum FollowedArtistOutcome
{
    /// <summary>写入成功，本地状态已同步。</summary>
    Succeeded,

    /// <summary>未登录。</summary>
    NotAuthenticated,

    /// <summary>同一个歌手的写操作正在进行，这次点击被忽略。</summary>
    AlreadyPending,

    /// <summary>请求失败。</summary>
    Failed,
}

/// <summary>
/// 歌手的「关注」状态与增删。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么需要这一层</b>：服务端对「当前账号是否已关注这个歌手」<b>没有字段</b> ——
/// 歌手详情 <c>service/artist/{id}</c> 的响应里没有任何 follow 标志
/// （<c>followStatus</c> 是**用户**对象和语音房的字段，不是歌手的）。
/// 所以个人状态只能由客户端拿 <c>service/collect/7/list</c> 的歌手集合自己判定，
/// 这份状态与它的失效规则需要一个唯一持有者。
/// </para>
/// <para>
/// <b>拉取策略</b>与 <see cref="ILikedSongsService"/> 同：判定时按需拉一次，之后本地判断；
/// <b>任何一次写成功都把缓存标脏，推迟到下一次判定才重拉</b>，所以点击当下不发额外请求。
/// </para>
/// <para>
/// <b>不落盘</b>：缓存只活在会话内。落盘会让「在别的设备关注过」的状态长期不同步，
/// 而重新拉一次的代价只有一个请求。见 <c>docs/like-share.md</c>。
/// </para>
/// </remarks>
public interface IFollowedArtistsService
{
    /// <summary>
    /// 判定歌手是否已关注。
    /// </summary>
    /// <returns>
    /// <c>true</c> / <c>false</c> 为确定答案；<b><c>null</c> 表示无法判定</b> ——
    /// 未登录或读取失败。调用方不要把它当成「未关注」。
    /// </returns>
    Task<bool?> IsFollowedAsync(long artistId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 关注或取消关注一个歌手。写成功后本地集合立即更新，并标记缓存需要重拉。
    /// </summary>
    Task<FollowedArtistOutcome> SetFollowedAsync(long artistId, bool followed, CancellationToken cancellationToken = default);
}
