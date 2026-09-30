using Bodian.Core.Api;
using Bodian.Core.Api.Dto;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 会员状态判读。纯值输入。
/// </summary>
/// <remarks>
/// 判据来自实测：登录样本里 <c>isVip=1</c> 而 <c>expireDate=0</c>，
/// 真正生效的是 <c>actVipType=1</c> + <c>actExpireDate</c>（活动赠送的会员）。
/// 挑单一字段读一定会判错，所以这里逐条把各种组合钉住。
/// </remarks>
public sealed class VipStatusTests
{
    private const long Jan2027 = 1798761600000;   // 2027-01-01 的毫秒时间戳
    private const long Jul2027 = 1814400000000;   // 2027-07-01

    [Fact]
    public void NoPayInfo_IsNotVip()
    {
        var (isVip, expires) = VipStatus.Resolve(null);

        Assert.False(isVip);
        Assert.Null(expires);
    }

    [Fact]
    public void EmptyPayInfo_IsNotVip()
    {
        var (isVip, expires) = VipStatus.Resolve(new AccountPayInfoDto());

        Assert.False(isVip);
        Assert.Null(expires);
    }

    /// <summary>实测形态：普通到期字段是 0，靠活动会员字段才判得出来。</summary>
    [Fact]
    public void ActivityVipOnly_IsStillVip()
    {
        var (isVip, expires) = VipStatus.Resolve(new AccountPayInfoDto
        {
            IsVip = 1,
            ExpireDate = 0,
            ActVipType = 1,
            ActExpireDate = Jan2027,
        });

        Assert.True(isVip);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(Jan2027), expires);
    }

    [Theory]
    [InlineData(true, 0, 0, 0)]
    [InlineData(false, 1, 0, 0)]
    [InlineData(false, 0, 0, 1)]
    [InlineData(false, 0, 1, 0)]
    public void AnyVipSignal_Counts(bool isVipBoolean, int isVipField, int vipType, int actVipType)
    {
        var (isVip, _) = VipStatus.Resolve(new AccountPayInfoDto
        {
            IsVipBoolean = isVipBoolean,
            IsVip = isVipField,
            VipType = vipType,
            ActVipType = actVipType,
        });

        Assert.True(isVip);
    }

    /// <summary>同时持有几种会员时取最晚的那个 —— 取第一个非零会低估。</summary>
    [Fact]
    public void PicksTheLatestExpiry()
    {
        var (_, expires) = VipStatus.Resolve(new AccountPayInfoDto
        {
            IsVipBoolean = true,
            ExpireDate = Jan2027,
            BigExpireDate = Jul2027,
            ActExpireDate = Jan2027,
        });

        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(Jul2027), expires);
    }

    /// <summary>0 是「没有这种会员」的表达，不能被当成 1970 年。</summary>
    [Fact]
    public void ZeroExpiryMeansAbsent()
    {
        var (isVip, expires) = VipStatus.Resolve(new AccountPayInfoDto { IsVipBoolean = true });

        Assert.True(isVip);
        Assert.Null(expires);
    }

    /// <summary>明显越界的时间戳按不可信丢弃，不要让界面显示出「公元 500 年到期」。</summary>
    [Fact]
    public void ImplausibleTimestampIsIgnored()
    {
        var (isVip, expires) = VipStatus.Resolve(new AccountPayInfoDto
        {
            IsVipBoolean = true,
            ExpireDate = long.MaxValue,
        });

        Assert.True(isVip);
        Assert.Null(expires);
    }

    /// <summary>负数是异常值，同样丢弃。</summary>
    [Fact]
    public void NegativeTimestampIsIgnored()
    {
        var (_, expires) = VipStatus.Resolve(new AccountPayInfoDto
        {
            IsVipBoolean = true,
            ExpireDate = -1,
        });

        Assert.Null(expires);
    }
}
