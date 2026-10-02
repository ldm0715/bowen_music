using Bodian.Core.Models;
using Xunit;

namespace Bodian.Core.Tests;

public sealed class CommentCountLabelTests
{
    [Theory]
    [InlineData(-1, "0")]
    [InlineData(0, "0")]
    [InlineData(8, "8")]
    [InlineData(9999, "9999")]
    [InlineData(10000, "10000")]
    [InlineData(10001, "1w+")]
    [InlineData(10999, "1w+")]
    [InlineData(12000, "1w2+")]
    [InlineData(12345, "1w2+")]
    [InlineData(19999, "1w9+")]
    [InlineData(20000, "2w+")]
    [InlineData(27890, "2w7+")]
    [InlineData(101999, "10w1+")]
    [InlineData(1000000, "100w+")]
    public void Format_IsExactThroughTenThousandAndTruncatesLargerCounts(long count, string label)
        => Assert.Equal(label, CommentCountLabel.Format(count));
}
