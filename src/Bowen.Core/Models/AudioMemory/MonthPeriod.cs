namespace Bowen.Core.Models.AudioMemory;

/// <summary>
/// 月份。音波回忆的月份参数、缓存键与显示文案都围绕它。
/// </summary>
/// <remarks>
/// 协议里的 <c>date</c> 是 <c>YYYYMM</c>（如 <c>202610</c>），不是 <c>yyyy-MM</c>；
/// 月报响应里的 <c>monthDate</c> 则是 <c>yyyy-MM-dd</c>。两者都由本类型统一。
/// </remarks>
public readonly record struct MonthPeriod
{
    public MonthPeriod(int year, int month)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(month, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(month, 12);

        Year = year;
        Month = month;
    }

    public int Year { get; }

    public int Month { get; }

    /// <summary><c>date</c> 参数用的 <c>YYYYMM</c>。</summary>
    public string Value => $"{Year}{Month:00}";

    /// <summary>界面展示用的 <c>yyyy年M月</c>。</summary>
    public string Label => $"{Year}年{Month}月";

    /// <summary>相对当前月份偏移。用于月份下拉的最近 12 个月。</summary>
    public MonthPeriod AddMonths(int delta)
    {
        var index = (Year * 12) + (Month - 1) + delta;
        return new MonthPeriod(index / 12, (index % 12) + 1);
    }

    /// <summary>
    /// 从 <c>summary</c> 的 <c>monthDesc</c> 解析月份。
    /// </summary>
    /// <remarks>
    /// 实测格式是 <c>"10月听歌"</c>，<b>没有年份</b>。年份按「月份大于当前月份就是去年」推断；
    /// 如果文案里明确带了四位年份（如 <c>"2026年10月"</c>），以它为准。
    /// </remarks>
    public static bool TryParseMonthDesc(string? text, DateOnly today, out MonthPeriod period)
    {
        period = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        for (var i = 0; i < text.Length;)
        {
            if (!char.IsAsciiDigit(text[i]))
            {
                i++;
                continue;
            }

            var start = i;
            while (i < text.Length && char.IsAsciiDigit(text[i]))
            {
                i++;
            }

            var token = text[start..i];

            // 紧凑写法 YYYYMM（如 "202610"）。
            if (token.Length == 6
                && int.TryParse(token, out var compact)
                && compact % 100 is >= 1 and <= 12)
            {
                period = new MonthPeriod(compact / 100, compact % 100);
                return true;
            }

            // 四位数先当年份，后面的那个数字才是月份。
            if (token.Length == 4
                && int.TryParse(token, out var year)
                && TryReadNextNumber(text, i) is { } month
                && month is >= 1 and <= 12)
            {
                period = new MonthPeriod(year, month);
                return true;
            }

            // 一到两位直接当月份；年份按「大于当前月即去年」推断。
            if (token.Length is 1 or 2
                && int.TryParse(token, out var monthOnly)
                && monthOnly is >= 1 and <= 12)
            {
                var inferredYear = today.Year - (monthOnly > today.Month ? 1 : 0);
                period = new MonthPeriod(inferredYear, monthOnly);
                return true;
            }
        }

        return false;
    }

    /// <summary>从月报响应的 <c>monthDate</c>（<c>yyyy-MM-dd</c>）解析月份。</summary>
    public static bool TryParseMonthDate(string? text, out MonthPeriod period)
    {
        period = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('-');
        if (parts.Length >= 2
            && int.TryParse(parts[0], out var year)
            && int.TryParse(parts[1], out var month)
            && month is >= 1 and <= 12)
        {
            period = new MonthPeriod(year, month);
            return true;
        }

        return false;
    }

    private static int? TryReadNextNumber(string text, int index)
    {
        while (index < text.Length && !char.IsAsciiDigit(text[index]))
        {
            index++;
        }

        var start = index;
        while (index < text.Length && char.IsAsciiDigit(text[index]))
        {
            index++;
        }

        return start < index && int.TryParse(text[start..index], out var value) ? value : null;
    }
}
