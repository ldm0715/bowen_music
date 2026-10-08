using Bodian.Core.Services;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 手机号规范化。纯函数，零网络。
/// </summary>
/// <remarks>
/// <b>本文件的号码全部是合成值</b>（<c>13800138000</c> 一族），
/// <b>绝不要把你自己的号码抄进来</b> —— 这是要进公开仓库的文件。
/// </remarks>
public sealed class MobileNumberTests
{
    [Theory]
    [InlineData("13800138000", "13800138000")]
    [InlineData("13912345678", "13912345678")]
    // 界面上常见的分隔符：服务端要的是裸 11 位，所以这些都要被抹平。
    [InlineData("138 0013 8000", "13800138000")]
    [InlineData("138-0013-8000", "13800138000")]
    [InlineData("(138)00138000", "13800138000")]
    // 国际前缀：带与不带 + 都要认。
    [InlineData("+8613800138000", "13800138000")]
    [InlineData("8613800138000", "13800138000")]
    [InlineData("+86 138 0013 8000", "13800138000")]
    public void Normalize_StripsToElevenDigits(string raw, string expected)
        => Assert.Equal(expected, MobileNumber.Normalize(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12800000000")]     // 第二位 2 不是手机号段
    [InlineData("10800000000")]     // 10x 是特服号段，不是手机号
    [InlineData("1380013800")]      // 10 位
    [InlineData("138001380000")]    // 12 位
    [InlineData("1380013800a")]     // 混了字母
    [InlineData("abcdefghijk")]
    public void Normalize_RejectsInvalid(string? raw)
        => Assert.Null(MobileNumber.Normalize(raw));

    [Fact]
    public void IsValid_AgreesWithNormalize()
    {
        Assert.True(MobileNumber.IsValid("13800138000"));
        Assert.False(MobileNumber.IsValid("12800000000"));
        Assert.False(MobileNumber.IsValid(null));
    }
}
