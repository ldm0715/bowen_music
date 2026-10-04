using Bodian.Core.Models.Account;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 听歌时长文案。
/// </summary>
/// <remarks>
/// <b>单位是推断的</b>（见 <c>ListenTimeLabel.SecondsPerUnit</c>）—— 服务端只说
/// <c>playTime</c> 是个整数。这些用例钉的是「拿到秒数之后的文案」，与单位无关。
/// </remarks>
public sealed class ListenTimeLabelTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void NoValue_IsEmpty(long? raw) => Assert.Equal("", ListenTimeLabel.Format(raw));

    /// <summary>不足一分钟也要显示「1 分钟」，不能显示「0 分钟」。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(59)]
    public void UnderOneMinute_ShowsOneMinute(long raw) =>
        Assert.Equal("1 分钟", ListenTimeLabel.Format(raw));

    [Fact]
    public void UnderOneHour_ShowsMinutes() =>
        Assert.Equal("59 分钟", ListenTimeLabel.Format(3599));

    /// <summary>满一小时就换成小时，不足的部分截断（不做四舍五入）。</summary>
    [Theory]
    [InlineData(3600)]
    [InlineData(3661)]
    public void FromOneHour_ShowsHours(long raw) =>
        Assert.Equal("1 小时", ListenTimeLabel.Format(raw));

    [Fact]
    public void LargeValue_ShowsHours() =>
        Assert.Equal("100 小时", ListenTimeLabel.Format(360000));
}
