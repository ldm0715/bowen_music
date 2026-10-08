using Bodian.Core.Api;
using Bodian.Core.Services;
using Bodian.Core.Services.Abstractions;

namespace Bodian.Core.Tests.Support;

/// <summary>
/// 可切换的「当前账号」。
/// </summary>
/// <remarks>
/// 设 <see cref="Uid"/> 会像真实现一样抛 <see cref="Changed"/>；需要「只改状态、不通知」的边界
/// 用 <see cref="SetQuietly"/>。
/// </remarks>
internal sealed class FakeCurrentAccount : ICurrentAccount
{
    private string _uid = BodianSession.AnonymousUid;

    public string Uid
    {
        get => _uid;
        set
        {
            _uid = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public string Scope => _uid == BodianSession.AnonymousUid || _uid.Length == 0
        ? AppPaths.AnonymousScope
        : _uid;

    public event EventHandler? Changed;

    /// <summary>登录成某个账号，并通知订阅方。</summary>
    public void SignIn(string uid) => Uid = uid;

    /// <summary>登出，并通知订阅方。</summary>
    public void SignOut() => Uid = BodianSession.AnonymousUid;

    /// <summary>不通知地改状态。用来构造「读到一半账号自己变了」这类边界。</summary>
    public void SetQuietly(string uid) => _uid = uid;
}
