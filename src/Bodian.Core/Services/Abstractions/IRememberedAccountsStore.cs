namespace Bodian.Core.Services.Abstractions;

/// <summary>
/// 清单里的一条：一份可用的凭据 + 上次使用它的时刻。
/// </summary>
/// <remarks>
/// <b>直接复用 <see cref="BodianCredential"/>，不照抄一遍字段。</b> 上次加
/// <c>VipBadge</c> 时吃过亏：同样的字段在两处各写一份，加字段就得记得改两处，
/// 漏一处的表现是「恢复出来的头像/会员标悄悄没了」。
/// </remarks>
/// <param name="Credential">可用的凭据（含 token）。</param>
/// <param name="LastUsedAt">上次使用时刻，用来把最可能想切回去的那个排在最前面。</param>
public sealed record RememberedAccount(BodianCredential Credential, DateTimeOffset LastUsedAt);

/// <summary>
/// 「记住过哪些账号」的读写。供账号快捷切换用。
/// </summary>
/// <remarks>
/// <para>
/// <b>这里是可用的凭据</b>（含 token），与 <see cref="ICredentialStore"/> 同级敏感，实现必须加密落盘。
/// 区别只在语义：那个是「当前是谁」，这里是「记住过谁」。
/// </para>
/// <para>
/// <b>读失败一律当空清单，绝不抛。</b> 清单读不出来只让「切换账号」列表空着，
/// 当前会话（<c>session.dat</c>）照旧 —— 不该因此让应用起不来，更不该替用户做删除决定。
/// </para>
/// </remarks>
public interface IRememberedAccountsStore
{
    /// <summary>按 <see cref="RememberedAccount.LastUsedAt"/> 倒序。</summary>
    IReadOnlyList<RememberedAccount> Load();

    /// <summary>记下或更新一份凭据，并把它标成「刚用过」。同一个 uid 只留一条。</summary>
    void Remember(BodianCredential credential, DateTimeOffset usedAt);

    /// <summary>忘掉某个 uid。<b>只忘登录凭据，不动该账号的本地数据。</b>不在清单里时是空操作。</summary>
    void Forget(string uid);
}
