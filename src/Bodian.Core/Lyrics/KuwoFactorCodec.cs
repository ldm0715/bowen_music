namespace Bodian.Core.Lyrics;

/// <summary>
/// 酷我逐字歌词的时间系数。文本开头的 <c>[kuwo:N]</c> 决定两个除数。
/// </summary>
/// <remarks>
/// <para>
/// <b><c>N</c> 是八进制。</b> 这一条错了整轨时间全废，而且**"看着有值"不像报错**，
/// 是最容易蒙混过关的一处。
/// </para>
/// <para>
/// 以《晴天》的 <c>[kuwo:127]</c> 为例：
/// </para>
/// <list type="table">
/// <item>
///   <term>八进制（正确）</term>
///   <description><c>N = 87</c> → startFactor <c>8</c>、durationFactor <c>7</c>，
///   首行算出 <c>0 / 160 / 320 / 480 / 640 …</c> 的干净 160ms 网格</description>
/// </item>
/// <item>
///   <term>十进制（错误）</term>
///   <description><c>N = 127</c> → startFactor <c>12</c>，得到
///   <c>0 / 106.67 / 213.3 / 320 / 426.7 …</c> 这种非整数、忽快忽慢的时间轴</description>
/// </item>
/// </list>
/// <para>
/// 另外：解出的词时间是**相对行首的偏移**，不是绝对时间。实测样本里 63 行的首词
/// <c>start</c> 全为 0，绝对时间不可能每行都从 0 开始。
/// </para>
/// </remarks>
public static class KuwoFactorCodec
{
    private const string TagPrefix = "[kuwo:";

    /// <summary>
    /// 从歌词文本开头读 <c>[kuwo:N]</c>。
    /// </summary>
    /// <returns>
    /// 该文件有有效逐字轨时为 <c>true</c>。任一系数为 0 时返回 <c>false</c>——
    /// 那是「无有效逐字轨」的信号，应当回退行歌词。
    /// </returns>
    /// <remarks>
    /// <b>无标签不是异常边界。</b> 逐行版（<c>lrcx=0</c>）本来就不带这个标签，
    /// 参见 <c>bodian-api-reference.md</c> 2.6 节的说明：按 <c>lrcx</c> 分流，
    /// 而不是在解析器里猜默认值。
    /// </remarks>
    public static bool TryParseTag(string? lyricText, out int startFactor, out int durationFactor)
    {
        startFactor = 0;
        durationFactor = 0;

        if (string.IsNullOrEmpty(lyricText))
        {
            return false;
        }

        var start = lyricText.IndexOf(TagPrefix, StringComparison.Ordinal);
        if (start < 0)
        {
            return false;
        }

        var end = lyricText.IndexOf(']', start + TagPrefix.Length);
        if (end < 0)
        {
            return false;
        }

        var digits = lyricText[(start + TagPrefix.Length)..end].Trim();

        // ★ 八进制。按十进制解会得到非整数的时间轴，而且不报错。
        if (!TryParseOctal(digits, out var tag))
        {
            return false;
        }

        startFactor = tag / 10;
        durationFactor = tag % 10;

        return startFactor != 0 && durationFactor != 0;
    }

    /// <summary>把 <c>[kuwo:N]</c> 标签从文本里去掉。</summary>
    public static string StripTag(string lyricText)
    {
        if (string.IsNullOrEmpty(lyricText))
        {
            return lyricText ?? string.Empty;
        }

        var start = lyricText.IndexOf(TagPrefix, StringComparison.Ordinal);
        if (start < 0)
        {
            return lyricText;
        }

        var end = lyricText.IndexOf(']', start + TagPrefix.Length);
        return end < 0 ? lyricText : lyricText.Remove(start, end - start + 1);
    }

    /// <summary>
    /// 把一对 <c>&lt;a,b&gt;</c> 还原成毫秒。
    /// </summary>
    /// <remarks>
    /// <code>
    /// start    = trunc(abs((a + b) / (2 * startFactor)))
    /// duration = trunc(abs((a - b) / (2 * durationFactor)))
    /// </code>
    /// 实测样本里恒有 <c>a &gt; b</c>，所以取不取绝对值结果相同；这里取绝对值，
    /// 免得遇到 <c>&lt;3150,-3150&gt;</c> 这类形态时算出负数。
    /// </remarks>
    public static (long StartMs, long DurationMs) DecodeWord(
        long a,
        long b,
        int startFactor,
        int durationFactor)
    {
        if (startFactor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startFactor), startFactor, "系数必须为正");
        }

        if (durationFactor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(durationFactor), durationFactor, "系数必须为正");
        }

        var start = Math.Abs(a + b) / (2 * (long)startFactor);
        var duration = Math.Abs(a - b) / (2 * (long)durationFactor);

        return (start, duration);
    }

    private static bool TryParseOctal(string digits, out int value)
    {
        value = 0;

        if (digits.Length == 0)
        {
            return false;
        }

        foreach (var c in digits)
        {
            if (c is < '0' or > '7')
            {
                value = 0;
                return false;
            }

            value = (value * 8) + (c - '0');
        }

        return true;
    }
}
