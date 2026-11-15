using Bowen.Core.Models.AudioMemory;
using Xunit;

namespace Bowen.Core.Tests;

/// <summary>
/// 月报月份的解析。协议里 <c>monthDesc</c> 只给月份、<c>monthDate</c> 给完整日期，
/// 两者都要能落到同一个 <see cref="MonthPeriod"/>。
/// </summary>
public sealed class MonthPeriodTests
{
    [Fact]
    public void Value_IsCompactFormat()
    {
        var period = new MonthPeriod(2026, 10);

        Assert.Equal("202610", period.Value);
        Assert.Equal("2026年10月", period.Label);
    }

    /// <summary>实测的 <c>monthDesc</c> 形状：只有月份，年份要推断。</summary>
    [Fact]
    public void ParseMonthDesc_InfersCurrentYear()
    {
        Assert.True(MonthPeriod.TryParseMonthDesc("10月听歌", new DateOnly(2026, 11, 15), out var period));
        Assert.Equal(new MonthPeriod(2026, 10), period);
    }

    /// <summary>一月份看到「12月」只能是去年的报告。</summary>
    [Fact]
    public void ParseMonthDesc_InfersPreviousYear_WhenMonthIsAhead()
    {
        Assert.True(MonthPeriod.TryParseMonthDesc("12月听歌", new DateOnly(2026, 1, 5), out var period));
        Assert.Equal(new MonthPeriod(2025, 12), period);
    }

    [Theory]
    [InlineData("2026年10月听歌")]
    [InlineData("202610")]
    public void ParseMonthDesc_AcceptsExplicitYear(string text)
    {
        Assert.True(MonthPeriod.TryParseMonthDesc(text, new DateOnly(2026, 11, 15), out var period));
        Assert.Equal(new MonthPeriod(2026, 10), period);
    }

    [Theory]
    [InlineData("")]
    [InlineData("近期暂无听歌月报")]
    [InlineData("13月听歌")]
    public void ParseMonthDesc_RejectsUnparseableText(string text)
    {
        Assert.False(MonthPeriod.TryParseMonthDesc(text, new DateOnly(2026, 11, 15), out _));
    }

    [Fact]
    public void ParseMonthDate_ReadsYearAndMonth()
    {
        Assert.True(MonthPeriod.TryParseMonthDate("2026-10-01", out var period));
        Assert.Equal(new MonthPeriod(2026, 10), period);
    }

    [Fact]
    public void AddMonths_CrossesYearBoundary()
    {
        Assert.Equal(new MonthPeriod(2025, 12), new MonthPeriod(2026, 1).AddMonths(-1));
        Assert.Equal(new MonthPeriod(2027, 1), new MonthPeriod(2026, 12).AddMonths(1));
        Assert.Equal(new MonthPeriod(2025, 11), new MonthPeriod(2026, 11).AddMonths(-12));
    }
}
