using Bodian.Core.Api;
using Bodian.Core.Api.Dto;
using Bodian.Core.Models.Account;
using Bodian.Core.Tests.Support;
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

    // ── 徽标档位（vipLogo 的映射）────────────────────────────────────────────

    /// <summary>
    /// 拿真实登录响应走一遍，确认映射在**实测数据**上自洽。
    /// </summary>
    /// <remarks>
    /// 这份 fixture 的 <c>vipType=2</c>、<c>payVipType=0</c>，而 <c>actExpireDate</c> 是唯一
    /// 有效的到期字段 —— 是个活动赠送的会员，映射成「福利会员」才对得上。
    /// <b>映射若被实测推翻（比如发现 x3 其实不是 vipType），这条会先红。</b>
    /// </remarks>
    [Fact]
    public void LoginFixture_MapsToWelfare()
    {
        var login = Fixtures.Load("login-users-login.json", BodianJsonContext.Default.LoginResultDto);

        Assert.Equal(VipBadgeKind.Welfare, VipStatus.ResolveBadge(login.PayInfo));
    }

    /// <summary>
    /// 大会员账号的实测形态（<c>ucenter/users/pub/36828743</c>，2026-10-04）。
    /// </summary>
    /// <remarks>
    /// 这条是**档位名的锚点**：该账号在官方客户端显示为「大会员」，而它的
    /// <c>vipType=1 / payVipType=1</c> 走的正是 crown 分支 —— 所以 crown 分支就是大会员。
    /// 之前把这一档当成畅听会员，是拿**另一个账号**（福利会员）的字段推的，前提就错了。
    /// </remarks>
    [Fact]
    public void BigVipAccount_MapsToBig()
    {
        var badge = VipStatus.ResolveBadge(new AccountPayInfoDto
        {
            IsVip = 1,
            IsVipBoolean = true,
            VipType = 1,
            PayVipType = 1,
            IsPayVipBoolean = true,
            IsBigVipBoolean = true,
            IsBigPayVipBoolean = true,
            IsCtVipBoolean = false,
            IsActVipBoolean = true,
            ActVipType = 1,
        });

        Assert.Equal(VipBadgeKind.Big, badge);
    }

    [Theory]
    [InlineData(1, 2, VipBadgeKind.Standard)]   // payVipType=2 → 畅听会员
    [InlineData(1, 1, VipBadgeKind.Big)]        // 本机 大会员 账号的形态：vipType=1 / payVipType=1
    [InlineData(1, 0, VipBadgeKind.Big)]
    [InlineData(1, 3, VipBadgeKind.Big)]        // 认不出的 payVipType → 退回大会员
    [InlineData(2, 2, VipBadgeKind.Welfare)]    // 活动档优先于 payVipType（与安卓同序）
    [InlineData(2, 0, VipBadgeKind.Welfare)]
    [InlineData(9, 0, VipBadgeKind.Big)]        // 是会员但档位认不出 → 退回大会员，**不画灰标**
    public void VipTypeMapsToBadge(int vipType, int payVipType, VipBadgeKind expected)
    {
        var badge = VipStatus.ResolveBadge(new AccountPayInfoDto
        {
            IsVipBoolean = true,
            VipType = vipType,
            PayVipType = payVipType,
        });

        Assert.Equal(expected, badge);
    }

    [Fact]
    public void NotVip_MapsToNoBadge()
    {
        Assert.Equal(VipBadgeKind.None, VipStatus.ResolveBadge(null));
        Assert.Equal(VipBadgeKind.None, VipStatus.ResolveBadge(new AccountPayInfoDto()));
    }
}
