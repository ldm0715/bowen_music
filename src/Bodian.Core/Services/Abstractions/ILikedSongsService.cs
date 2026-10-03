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
}
