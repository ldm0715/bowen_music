namespace Bodian.Core.Services.Abstractions;

/// <summary>喜欢 / 取消喜欢的结果。</summary>
public enum LikedSongsOutcome
{
    /// <summary>写入成功，本地状态已同步。</summary>
    Succeeded,

    /// <summary>未登录。</summary>
    NotAuthenticated,

    /// <summary>账号还没有「我喜欢的」歌单，无法增删。</summary>
    NoLikedPlaylist,

    /// <summary>同一首歌的写操作正在进行，这次点击被忽略。</summary>
    AlreadyPending,

    /// <summary>请求失败。</summary>
    Failed,
}

/// <summary>批量喜欢 / 取消喜欢的结果。</summary>
/// <param name="Outcome">
/// 整体结论。<see cref="LikedSongsOutcome.Succeeded"/> 表示这一批处理完了
/// （<b>不代表每一首都成功</b>，逐首的失败数看 <paramref name="Failed"/>）；
/// 其余取值表示整批没做，此时两个计数都是 0。
/// </param>
/// <param name="Succeeded">成功的曲目数。</param>
/// <param name="Failed">失败的曲目数。</param>
/// <param name="Canceled">是否被取消。</param>
public readonly record struct LikedSongsBatchOutcome(
    LikedSongsOutcome Outcome, int Succeeded, int Failed, bool Canceled);

/// <summary>
/// 曲目的「喜欢」状态与增删。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么需要这一层</b>：服务端对「当前账号是否已喜欢这首歌」<b>没有字段</b> ——
/// <c>service/music/info</c> 只给全站 <c>favorite</c> 计数。所以个人状态只能由客户端
/// 拿「我喜欢的」歌单的曲目集合自己判定，这份状态和它的失效规则需要一个唯一的持有者。
/// </para>
/// <para>
/// <b>拉取策略</b>：判定时按需拉一次，之后本地判断；<b>任何一次写成功（喜欢或取消）
/// 都把缓存标脏，推迟到下一次判定才重拉</b>。所以点击当下不发额外请求。
/// </para>
/// <para>
/// <b>不落盘</b>：缓存只活在会话内。落盘会让「在别的设备喜欢过」的状态长期不同步，
/// 而重新拉一次的代价只有几个请求。见 <c>docs/like-share.md</c>。
/// </para>
/// </remarks>
public interface ILikedSongsService
{
    /// <summary>
    /// 判定曲目是否已喜欢。
    /// </summary>
    /// <returns>
    /// <c>true</c> / <c>false</c> 为确定答案；<b><c>null</c> 表示无法判定</b> ——
    /// 未登录、账号没有「我喜欢的」歌单，或读取失败。调用方不要把它当成「未喜欢」。
    /// </returns>
    Task<bool?> IsLikedAsync(long musicId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 喜欢或取消喜欢一首歌。写成功后本地集合立即更新，并标记缓存需要重拉。
    /// </summary>
    Task<LikedSongsOutcome> SetLikedAsync(long musicId, bool liked, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量喜欢。多选栏的「批量加入喜欢」走这条。
    /// </summary>
    /// <param name="musicIds">目标曲目。非正 id 会被剔掉，重复的只算一次。</param>
    /// <param name="liked">默认喜欢；传 <c>false</c> 是批量取消喜欢。</param>
    /// <param name="progress">每写完一批报一次进度，可空。</param>
    /// <remarks>
    /// <para>
    /// <b>已经在目标状态里的曲目不发请求</b>：全选一个已收藏的歌单时能省掉整批。
    /// </para>
    /// <para>
    /// <b>单首失败不中断</b>，最后按 <see cref="LikedSongsBatchOutcome.Failed"/> 汇总；
    /// 但只要有失败（或账号中途换了），本地集合就<b>不更新</b>、只标脏，
    /// 交给下一次判定整体重拉 —— 半对半错的集合比稍微滞后的集合危险得多。
    /// </para>
    /// </remarks>
    Task<LikedSongsBatchOutcome> SetLikedManyAsync(
        IReadOnlyList<long> musicIds, bool liked = true,
        IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
