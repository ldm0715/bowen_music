using Bodian.Core.Services.Abstractions;

namespace Bodian.Core.Tests.Support;

/// <summary>
/// 内存版「记住的账号」清单。
/// </summary>
/// <remarks>
/// 刻意**不实现上限淘汰**：那是真实实现的行为，有它自己的用例（<c>RememberedAccountsStoreTests</c>）。
/// 这个假实现只提供「记了什么、忘了什么」这两笔账。
/// </remarks>
internal sealed class FakeRememberedAccountsStore : IRememberedAccountsStore
{
    private readonly List<RememberedAccount> _accounts = [];

    /// <summary><see cref="Remember"/> 被调了几次。用来验「登录/恢复/切换都会记一笔」。</summary>
    public int RememberCount { get; private set; }

    public int ForgetCount { get; private set; }

    public IReadOnlyList<RememberedAccount> Load() =>
        [.. _accounts.OrderByDescending(entry => entry.LastUsedAt)];

    public void Remember(BodianCredential credential, DateTimeOffset usedAt)
    {
        RememberCount++;

        _accounts.RemoveAll(entry => entry.Credential.Uid == credential.Uid);
        _accounts.Add(new RememberedAccount(credential, usedAt));
    }

    public void Forget(string uid)
    {
        ForgetCount++;

        _accounts.RemoveAll(entry => entry.Credential.Uid == uid);
    }
}
