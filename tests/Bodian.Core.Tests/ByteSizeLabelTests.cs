using Bodian.Core.Models;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 字节数的显示文案。设置页那一栏全靠它，写错就是「1024.0 KB」这种一眼假的数字。
/// </summary>
public sealed class ByteSizeLabelTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1, "1 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1024 * 1024, "1 MB")]
    [InlineData(12 * 1024 * 1024 + 300 * 1024, "12.3 MB")]
    [InlineData(1024L * 1024 * 1024, "1 GB")]
    [InlineData(2L * 1024 * 1024 * 1024 + 760L * 1024 * 1024, "2.7 GB")]
    public void Format_ProducesTheExpectedText(long bytes, string expected) =>
        Assert.Equal(expected, ByteSizeLabel.Format(bytes));

    [Fact]
    public void Format_NeverEmitsAKilobyteValueWithFourDigits()
    {
        // 1023.9 KB 这种是典型的进位漏掉，进一位才是 1 MB。
        Assert.Equal("1 MB", ByteSizeLabel.Format(1024L * 1024 - 1));
    }

    [Fact]
    public void Format_TreatsNegativeAsZero()
    {
        // 目录枚举失败时可能算出负数，别显示成「-1.0 GB」。
        Assert.Equal("0 B", ByteSizeLabel.Format(-1));
    }
}
