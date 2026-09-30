using Bodian.Core.Services;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 身份一致性校验。**纯值输入**，不碰 fixture —— 登录响应样本里的身份字段被脱敏成了
/// <c>"&lt;redacted&gt;"</c>，拿它测不出任何东西。
/// </summary>
public sealed class SessionIdentityTests
{
    /// <summary>实测响应形态：只有 id / bid / userInfo.id，三者相同。</summary>
    [Fact]
    public void RealWorldShape_ThreeMatchingFields_Resolves()
    {
        var uid = SessionIdentity.Resolve(50303440, 50303440, null, 50303440, null);

        Assert.Equal(50303440, uid);
    }

    /// <summary>只有两个字段也能成立——判据是「至少凑出两个」。</summary>
    [Fact]
    public void TwoMatchingFields_Resolves()
    {
        Assert.Equal(50303440, SessionIdentity.Resolve(50303440, 50303440, null, null, null));
    }

    /// <summary>只有一个字段没有证据力：可能是别的字段缺失导致的巧合。</summary>
    [Fact]
    public void SingleField_IsNotEnough()
    {
        Assert.Null(SessionIdentity.Resolve(50303440, null, null, null, null));
    }

    [Fact]
    public void NoFields_ReturnsNull()
    {
        Assert.Null(SessionIdentity.Resolve(null, null, null, null, null));
    }

    /// <summary>不等说明拿到的不是扫码人的会话，必须丢弃——这是 authType 写错时的症状。</summary>
    [Fact]
    public void MismatchedFields_ReturnsNull()
    {
        Assert.Null(SessionIdentity.Resolve(50303440, 99999999, null, 50303440, null));
    }

    /// <summary>文档描述过但实测不存在的 uid / userInfo.uid 也参与比较，不能忽略。</summary>
    [Fact]
    public void DocumentedFields_AreAlsoCompared()
    {
        Assert.Null(SessionIdentity.Resolve(50303440, 50303440, 11111111, null, null));
        Assert.Equal(50303440, SessionIdentity.Resolve(50303440, null, 50303440, null, 50303440));
    }

    /// <summary>五个字段全在且全同 → 证据最充分。</summary>
    [Fact]
    public void FiveMatchingFields_Resolves()
    {
        var uid = SessionIdentity.Resolve(7, 7, 7, 7, 7);

        Assert.Equal(7, uid);
    }

    /// <summary>0 是「存在且相等」的值，不能被当成「未出现」。边界：uid 理论上不会为 0，但判据不该因此漏掉。</summary>
    [Fact]
    public void ZeroIsAValueNotAbsence()
    {
        Assert.Equal(0, SessionIdentity.Resolve(0, 0, null, null, null));
        Assert.Null(SessionIdentity.Resolve(0, 1, null, null, null));
    }
}
