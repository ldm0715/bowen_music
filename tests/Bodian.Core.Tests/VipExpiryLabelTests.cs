using Bodian.Core.Models.Account;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 会员到期时间的文案。
/// </summary>
/// <remarks>
/// <b>只要日期</b>：会员到期是按天的概念，带上时分反而让人以为要精确到点。
/// </remarks>
public sealed class VipExpiryLabelTests
{
    [Fact]
    public void NoExpiry_IsEmpty() => Assert.Equal("", VipExpiryLabel.Format(null));

    [Fact]
    public void FormatsDateOnly()
    {
        // 用本地偏移构造，ToLocalTime 是恒等变换 —— 断言与机器时区无关。
        var localNoon = new DateTimeOffset(new DateTime(2028, 9, 21, 12, 0, 0, DateTimeKind.Local));

        Assert.Equal("2028-09-21 到期", VipExpiryLabel.Format(localNoon));
    }

    /// <summary>服务端给的是 UTC 毫秒时间戳，显示的是**本地**那一天。</summary>
    [Fact]
    public void UsesLocalDate()
    {
        var utcNoon = new DateTimeOffset(2028, 9, 21, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal($"{utcNoon.ToLocalTime():yyyy-MM-dd} 到期", VipExpiryLabel.Format(utcNoon));
    }
}
