namespace Bodian.Core.Services;

/// <summary>
/// 从登录响应里挑出「扫码人自己的 uid」—— 身份一致性校验。
/// </summary>
/// <remarks>
/// <para>
/// 为什么需要它：<b>官方响应里的身份字段不止一个，而且历史上出现过身份错配。</b>
/// 换取会话时若把 <c>authType</c> 写成 <c>9</c>（正确值是 <c>10</c>），服务端会返回
/// **别人的**账号会话。所以拿到响应后必须确认那几个 id 指的是同一个人，否则只能丢弃。
/// </para>
/// <para>
/// 判据字段不是固定的三四个：文档曾写「<c>id</c>/<c>uid</c>/<c>userInfo.id</c>/<c>userInfo.uid</c>
/// 四者一致」，但实测响应里<b>根本没有</b> <c>uid</c>，真正存在的是
/// <c>id</c> / <c>bid</c> / <c>userInfo.id</c>。所以采取「出现几个就比几个」：
/// 取所有非空值，要求<b>两两相等且至少凑出两个</b>。
/// </para>
/// <para>
/// <b>纯函数</b>，刻意不接受 DTO —— 登录响应 fixture 被脱敏过（身份字段全是
/// <c>"&lt;redacted&gt;"</c>），拿它测不出东西，只能用纯值输入测。
/// </para>
/// </remarks>
public static class SessionIdentity
{
    /// <summary>
    /// 校验并取出 uid。
    /// </summary>
    /// <param name="id">响应的 <c>data.id</c>。</param>
    /// <param name="bid">响应的 <c>data.bid</c>。</param>
    /// <param name="uid">响应的 <c>data.uid</c>（实测不存在，保留以防将来出现）。</param>
    /// <param name="userInfoId">响应的 <c>data.userInfo.id</c>。</param>
    /// <param name="userInfoUid">响应的 <c>data.userInfo.uid</c>（实测不存在）。</param>
    /// <returns>
    /// 所有非空身份字段两两相等且至少有两个时返回该 uid；凑不出两个、或存在不相等时返回
    /// <c>null</c> —— <b>调用方必须丢弃该会话</b>，不能只记一条警告。
    /// </returns>
    public static long? Resolve(long? id, long? bid, long? uid, long? userInfoId, long? userInfoUid)
    {
        ReadOnlySpan<long?> candidates = [id, bid, uid, userInfoId, userInfoUid];

        long? resolved = null;
        var evidence = 0;

        foreach (var candidate in candidates)
        {
            if (candidate is not { } value)
            {
                continue;
            }

            if (resolved is null)
            {
                resolved = value;
            }
            else if (resolved != value)
            {
                // 不等说明拿到的不是扫码人的会话。只能丢弃，不能挑一个用。
                return null;
            }

            evidence++;
        }

        return evidence >= 2 ? resolved : null;
    }
}
