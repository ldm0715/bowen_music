using Bodian.Core.Api;
using Bodian.Core.Api.Dto;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 付费属性判读。
/// </summary>
/// <remarks>
/// <b>这一层的判据还没闭环</b>：现有样本全是付费曲（<c>{vip:"1", song:"1"}</c>），
/// 没有免费曲作对照。这些测试钉住的是「按现有理解实现得对不对」，
/// 不是「判据本身对不对」—— 采到免费曲样本后要回来复核。
/// </remarks>
public sealed class PayTypeReaderTests
{
    [Fact]
    public void NoPayInfo_IsNeither()
    {
        var (requiresVip, requiresPurchase) = PayTypeReader.Resolve(null);

        Assert.False(requiresVip);
        Assert.False(requiresPurchase);
    }

    [Fact]
    public void EmptyFeeType_IsNeither()
    {
        var (requiresVip, requiresPurchase) = PayTypeReader.Resolve(new PayInfoDto());

        Assert.False(requiresVip);
        Assert.False(requiresPurchase);
    }

    /// <summary>实测样本形态：搜索列表里的付费曲就是这个组合。</summary>
    [Fact]
    public void RealSampleShape()
    {
        var (requiresVip, requiresPurchase) = PayTypeReader.Resolve(new PayInfoDto
        {
            FeeType = new Dictionary<string, string>
            {
                ["bodianAlbum"] = "0",
                ["song"] = "1",
                ["vip"] = "1",
            },
        });

        Assert.True(requiresVip);
        Assert.True(requiresPurchase);
    }

    /// <summary>全 0 就是免费。</summary>
    [Fact]
    public void AllZero_IsFree()
    {
        var (requiresVip, requiresPurchase) = PayTypeReader.Resolve(new PayInfoDto
        {
            FeeType = new Dictionary<string, string>
            {
                ["vip"] = "0",
                ["song"] = "0",
                ["album"] = "0",
                ["bodianAlbum"] = "0",
            },
        });

        Assert.False(requiresVip);
        Assert.False(requiresPurchase);
    }

    /// <summary>只要 VIP、不要单曲购买是可能的，两个标志各判各的。</summary>
    [Fact]
    public void VipOnly_DoesNotSetPurchase()
    {
        var (requiresVip, requiresPurchase) = PayTypeReader.Resolve(new PayInfoDto
        {
            FeeType = new Dictionary<string, string> { ["vip"] = "1", ["song"] = "0" },
        });

        Assert.True(requiresVip);
        Assert.False(requiresPurchase);
    }

    [Fact]
    public void SongOnly_DoesNotSetVip()
    {
        var (requiresVip, requiresPurchase) = PayTypeReader.Resolve(new PayInfoDto
        {
            FeeType = new Dictionary<string, string> { ["vip"] = "0", ["song"] = "1" },
        });

        Assert.False(requiresVip);
        Assert.True(requiresPurchase);
    }

    /// <summary>键集合随接口变，所以要按键查：这些键只在 <c>music/info</c> 出现。</summary>
    [Theory]
    [InlineData("album")]
    [InlineData("bookvip")]
    [InlineData("bodianAlbum")]
    public void AlternatePurchaseKeys_AreRecognized(string key)
    {
        var (_, requiresPurchase) = PayTypeReader.Resolve(new PayInfoDto
        {
            FeeType = new Dictionary<string, string> { [key] = "1" },
        });

        Assert.True(requiresPurchase);
    }

    /// <summary>值只有 <c>"1"</c> 才算数，别把 <c>"0"</c> 或空串当真的。</summary>
    [Theory]
    [InlineData("0")]
    [InlineData("")]
    [InlineData("true")]
    public void OnlyLiteralOneCounts(string value)
    {
        var (requiresVip, requiresPurchase) = PayTypeReader.Resolve(new PayInfoDto
        {
            FeeType = new Dictionary<string, string> { ["vip"] = value, ["song"] = value },
        });

        Assert.False(requiresVip);
        Assert.False(requiresPurchase);
    }
}
