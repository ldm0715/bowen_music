using System.Globalization;

namespace Bodian.Core.Models;

/// <summary>评论角标：不超过一万显示整数，超过后按千位截断显示 w 后缀。</summary>
public static class CommentCountLabel
{
    public static string Format(long count)
    {
        count = Math.Max(0, count);
        if (count <= 10000) return count.ToString(CultureInfo.InvariantCulture);
        var tenThousands = count / 10000;
        var thousands = count / 1000 % 10;
        return thousands == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{tenThousands}w+")
            : string.Create(CultureInfo.InvariantCulture, $"{tenThousands}w{thousands}+");
    }
}
