namespace Bodian.Core.Services.Abstractions;

/// <summary>
/// 「当前是谁」——账号级数据（播放队列、最近播放、搜索历史）据此决定读写哪个目录。
/// </summary>
/// <remarks>
/// <para>
/// <b>刻意只暴露 uid 与作用域名</b>，不暴露 token 之类：需要凭据的地方本来就该直接依赖
/// <c>BodianSession</c>，而数据存储只需要知道「分到哪个目录」。
/// </para>
/// <para>
/// <b>未登录时 <see cref="Uid"/> 是 <c>"-1"</c>、<see cref="Scope"/> 是 <c>"anonymous"</c></b>。
/// 未登录期间播放历史仍会被写（播放不分登录状态），所以必须有一个匿名桶接住它，
/// 不能假设「一定有账号」。
/// </para>
/// </remarks>
public interface ICurrentAccount
{
    /// <summary>当前账号的 uid；未登录时为 <c>"-1"</c>。</summary>
    string Uid { get; }

    /// <summary>
    /// 数据目录名：匿名时是 <c>"anonymous"</c>，否则就是 uid。
    /// </summary>
    /// <remarks>
    /// 之所以不让每个调用点各写一次 <c>Uid == "-1" ? "anonymous" : Uid</c>：
    /// 那种特判散开后，漏掉一处就会让未登录的数据落进以 <c>-1</c> 命名的目录，
    /// 而那个目录看起来像某个账号。
    /// </remarks>
    string Scope { get; }

    /// <summary>
    /// 登录、登出、或服务端把会话清掉（业务码 <c>11012</c>）之后触发。
    /// </summary>
    /// <remarks>
    /// <b>回调里不做阻塞 IO，也不要直接碰界面对象。</b> 事件可能在传输层线程上触发
    /// （<c>NotifyUnauthorized</c> 走的是解析响应那条路），订阅方要自己 marshal 回 UI 线程。
    /// </remarks>
    event EventHandler? Changed;
}
