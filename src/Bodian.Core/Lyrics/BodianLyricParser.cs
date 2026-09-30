using System.Globalization;
using System.Text.RegularExpressions;
using Bodian.Core.Models.Lyrics;

namespace Bodian.Core.Lyrics;

/// <summary>
/// 把歌词文本解析成 <see cref="LyricDocument"/>。
/// </summary>
/// <remarks>
/// <para>
/// 输入是 <see cref="BodianLyricPayload.DecodeContent"/> 的产物（已经过一次 Base64）。
/// 同时吃两种版式：带 <c>[kuwo:N]</c> 与 <c>&lt;a,b&gt;</c> 的逐字版（<c>lrcx=1</c>）、
/// 标准 LRC 的逐行版（<c>lrcx=0</c>）。版式由标签自动判定，**不在解析器里猜测默认值** ——
/// 无标签不是异常边界，逐行版本来就不带标签（见 <c>bodian-api-reference.md</c> 2.6 节）。
/// </para>
/// <para>
/// 时间一律折算成绝对值：行首来自 <c>[mm:ss.fff]</c>，行内音节来自
/// <see cref="KuwoFactorCodec.DecodeWord"/> 的相对偏移再加上行首。消费方永远不必碰系数。
/// </para>
/// <para>
/// <b>刻意不实现的规则</b>：参考实现里有一条「长度 &lt; 6 的行跳过」。
/// 拿本项目两份真实样本核对过，它会把 <c>词：周杰伦</c>、<c>曲：周杰伦</c>、<c>鼓：陈柏州</c>
/// 这三行真实歌词误删（63 行变 60 行）。所以只跳过<b>没有时间戳</b>的行
/// （<c>[ti:]</c>/<c>[ar:]</c>/<c>[by:]</c>/<c>[ver:]</c> 这些元数据行天然落进这一类）。
/// </para>
/// </remarks>
public static partial class BodianLyricParser
{
    /// <summary>最后一行没有「下一行」可参照时的窗口长度。</summary>
    private static readonly TimeSpan FallbackLineDuration = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 行首时间戳。<c>[mm:ss]</c> / <c>[mm:ss.ff]</c> / <c>[mm:ss.fff]</c> / <c>[mm:ss:ff]</c> 都认。
    /// </summary>
    /// <remarks>
    /// 不锚定 <c>^</c>：读取时用 <see cref="Regex.Match(string, int)"/> 指定起点，
    /// 再靠 <c>Index</c> 是否等于起点来判断「就在开头」。<c>[ti:晴天]</c> 这类元数据
    /// 因为冒号前不是数字，天然不匹配。
    /// </remarks>
    [GeneratedRegex(@"\[(\d{1,3}):(\d{2})(?:[.:](\d{1,3}))?\]")]
    private static partial Regex TimestampPattern();

    /// <summary>逐字标记 <c>&lt;a,b&gt;</c>。两个数都可为负（行首那个标记的第二项就是负的）。</summary>
    [GeneratedRegex(@"<(-?\d+),(-?\d+)>")]
    private static partial Regex WordTagPattern();

    /// <summary>
    /// 解析歌词文本。
    /// </summary>
    /// <param name="lyricText">Base64 解码后的歌词。空白串返回 <see cref="LyricDocument.Empty"/>。</param>
    /// <param name="trackDuration">
    /// 曲目时长，只用来给最后一行收尾。拿不到就传 <c>null</c>，最后一行取 5 秒兜底。
    /// </param>
    public static LyricDocument Parse(string? lyricText, TimeSpan? trackDuration = null)
    {
        if (string.IsNullOrWhiteSpace(lyricText))
        {
            return LyricDocument.Empty;
        }

        var hasWordTrack = KuwoFactorCodec.TryParseTag(lyricText, out var startFactor, out var durationFactor);
        var text = hasWordTrack ? KuwoFactorCodec.StripTag(lyricText) : lyricText;

        var raw = new List<RawLine>();

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var stamps = new List<TimeSpan>();
            var offset = 0;

            while (TryReadTimestamp(line, offset, out var stamp, out var next))
            {
                stamps.Add(stamp);
                offset = next;
            }

            // 没有时间戳的行：元数据（[ti:] / [ar:] / [by:] / [ver:]）与空行。**不算错误。**
            if (stamps.Count == 0)
            {
                continue;
            }

            var content = line[offset..];
            string lineText;
            IReadOnlyList<RawSyllable> syllables;

            if (hasWordTrack)
            {
                (lineText, syllables) = ParseWordByWord(content, startFactor, durationFactor);
            }
            else
            {
                lineText = content;
                syllables = [];
            }

            // 一行挂多个时间戳是老 LRC 的常见写法（用来让同一句在副歌重复出现）。
            foreach (var stamp in stamps)
            {
                raw.Add(new RawLine(stamp, lineText, syllables));
            }
        }

        if (raw.Count == 0)
        {
            return LyricDocument.Empty;
        }

        var lines = new List<LyricLine>(raw.Count);

        for (var i = 0; i < raw.Count; i++)
        {
            var current = raw[i];

            var duration = i + 1 < raw.Count
                ? raw[i + 1].Start - current.Start
                : LastLineDuration(current.Start, trackDuration);

            // 时间戳乱序或重复时差值会不是正数，退回兜底值，别产出负窗口。
            if (duration <= TimeSpan.Zero)
            {
                duration = FallbackLineDuration;
            }

            lines.Add(new LyricLine(
                current.Start,
                duration,
                current.Text,
                Materialize(current, duration)));
        }

        return new LyricDocument(lines, hasWordTrack ? LyricKind.WordByWord : LyricKind.LineByLine);
    }

    /// <summary>逐行版整行就是一个音节；逐字版按解析结果折算成绝对时间。</summary>
    private static IReadOnlyList<LyricSyllable> Materialize(RawLine line, TimeSpan lineDuration)
    {
        if (line.Syllables.Count == 0)
        {
            return [new LyricSyllable(line.Text, line.Start, lineDuration)];
        }

        var result = new LyricSyllable[line.Syllables.Count];

        for (var i = 0; i < result.Length; i++)
        {
            var syllable = line.Syllables[i];
            result[i] = new LyricSyllable(syllable.Text, line.Start + syllable.Offset, syllable.Duration);
        }

        return result;
    }

    /// <summary>剥掉全部前导时间戳，取出正文。</summary>
    private static (string Text, IReadOnlyList<RawSyllable> Syllables) ParseWordByWord(
        string content,
        int startFactor,
        int durationFactor)
    {
        var text = WordTagPattern().Replace(content, "");
        var matches = WordTagPattern().Matches(content);
        var syllables = new List<RawSyllable>(matches.Count);

        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];

            // 标记在它所修饰的那段文本**之前**：<a,b>字。
            var from = match.Index + match.Length;
            var to = i + 1 < matches.Count ? matches[i + 1].Index : content.Length;
            var segment = from <= to ? content[from..to] : string.Empty;

            // 标记后面没有文本（行尾空标记）时不产出音节，免得渲染层拿到一堆空片段。
            if (segment.Length == 0)
            {
                continue;
            }

            var a = long.Parse(match.Groups[1].ValueSpan, CultureInfo.InvariantCulture);
            var b = long.Parse(match.Groups[2].ValueSpan, CultureInfo.InvariantCulture);
            var (startMs, durationMs) = KuwoFactorCodec.DecodeWord(a, b, startFactor, durationFactor);

            syllables.Add(new RawSyllable(
                segment,
                TimeSpan.FromMilliseconds(startMs),
                TimeSpan.FromMilliseconds(durationMs)));
        }

        return (text, syllables);
    }

    private static TimeSpan LastLineDuration(TimeSpan start, TimeSpan? trackDuration)
        => trackDuration is { } total && total > start ? total - start : FallbackLineDuration;

    /// <summary>从 <paramref name="start"/> 处读一个 <c>[mm:ss.fff]</c>。</summary>
    private static bool TryReadTimestamp(string line, int start, out TimeSpan value, out int next)
    {
        value = default;
        next = start;

        if (start >= line.Length || line[start] != '[')
        {
            return false;
        }

        var match = TimestampPattern().Match(line, start);
        if (!match.Success || match.Index != start)
        {
            return false;
        }

        var minutes = int.Parse(match.Groups[1].ValueSpan, CultureInfo.InvariantCulture);
        var seconds = int.Parse(match.Groups[2].ValueSpan, CultureInfo.InvariantCulture);
        var fraction = match.Groups[3].Success
            ? int.Parse(match.Groups[3].ValueSpan, CultureInfo.InvariantCulture)
            : 0;

        // 小数位按位数定标：.5 = 500ms、.50 = 500ms、.500 = 500ms。
        var milliseconds = match.Groups[3].ValueSpan.Length switch
        {
            1 => fraction * 100,
            2 => fraction * 10,
            _ => fraction,
        };

        value = new TimeSpan(0, 0, minutes, seconds).Add(TimeSpan.FromMilliseconds(milliseconds));
        next = match.Index + match.Length;
        return true;
    }

    /// <summary>解析中途的行：时间戳与音节还是「行内相对」的形态。</summary>
    private sealed record RawLine(TimeSpan Start, string Text, IReadOnlyList<RawSyllable> Syllables);

    /// <summary>解析中途的音节，时间是相对行首的偏移。</summary>
    private sealed record RawSyllable(string Text, TimeSpan Offset, TimeSpan Duration);
}
