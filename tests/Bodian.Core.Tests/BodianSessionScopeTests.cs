using Bodian.Core.Api;
using Bodian.Core.Services;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 会话作为「当前账号」的角色：作用域名，以及变更通知。
/// </summary>
public sealed class BodianSessionScopeTests
{
    [Fact]
    public void Anonymous_IsTheAnonymousScope()
    {
        var session = BodianSession.CreateAnonymous();

        Assert.False(session.IsAuthenticated);
        Assert.Equal(BodianSession.AnonymousUid, session.Uid);
        Assert.Equal(AppPaths.AnonymousScope, session.Scope);
    }

    [Fact]
    public void SignedIn_ScopeIsTheUid()
    {
        var session = BodianSession.CreateAnonymous();
        session.Set("12345", "token");

        Assert.True(session.IsAuthenticated);
        Assert.Equal("12345", session.Scope);
    }

    [Fact]
    public void Changed_FiresOnSignInAndSignOut()
    {
        var session = BodianSession.CreateAnonymous();
        var changes = 0;
        session.Changed += (_, _) => changes++;

        session.Set("12345", "token");
        Assert.Equal(1, changes);

        session.Clear();
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Changed_DoesNotFireOnSigningOutTwice()
    {
        var session = BodianSession.CreateAnonymous();
        var changes = 0;
        session.Changed += (_, _) => changes++;

        session.Clear();

        // 本来就是匿名，Clear 是空操作 —— 不该推高 Revision，也不该通知。
        Assert.Equal(0, changes);
        Assert.Equal(0, session.Revision);
    }

    [Fact]
    public void Cleared_StillCarriesItsReason()
    {
        // Changed 是后加的；Cleared 的语义（带原因）不能因此丢掉。
        var session = BodianSession.CreateAnonymous();
        session.Set("12345", "token");

        string? reason = null;
        session.Cleared += (_, e) => reason = e.Reason;

        session.Clear();

        Assert.NotNull(reason);
    }
}
