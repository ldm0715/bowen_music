namespace Bodian.Core.Models.Lyrics;

/// <summary>
/// 某一时刻在歌词里的定位结果。渲染一帧只需要这一个结构。
/// </summary>
/// <param name="LineIndex">落在第几行；曲头空白段与空文档是 <c>-1</c>。</param>
/// <param name="SyllableIndex">落在行内第几个音节；没有音节时是 <c>-1</c>。</param>
/// <param name="SyllableProgress">当前音节的播放进度，钳在 <c>[0,1]</c>。</param>
/// <param name="LineProgress">整行的播放进度，钳在 <c>[0,1]</c>。</param>
/// <remarks>
/// <b>纯数据、无状态。</b> 同一位置反复查询必须给出完全一致的结果 —— 渲染层靠这一点
/// 在 seek 之后不残留上一帧的高亮。
/// </remarks>
public readonly record struct LyricCursor(
    int LineIndex,
    int SyllableIndex,
    double SyllableProgress,
    double LineProgress)
{
    /// <summary>没有落在任何行里。</summary>
    public static LyricCursor None { get; } = new(-1, -1, 0, 0);

    /// <summary>落在一行里。</summary>
    public bool HasLine => LineIndex >= 0;

    /// <summary>落在一个音节里。</summary>
    public bool HasSyllable => SyllableIndex >= 0;
}

/// <summary>
/// 歌词时间轴的查询。全部是 <see cref="TimeSpan"/> 的纯函数，没有状态。
/// </summary>
/// <remarks>
/// <para>
/// 行级定位早就在 <see cref="LyricDocument.IndexOfLineAt"/> 里了；这里补的是<b>音节级</b>。
/// 两者的二分语义<b>刻意保持一致</b>：第一项之前返回 <c>-1</c>，之后返回最后一项。
/// 读代码的人只需要理解一次这套边界。
/// </para>
/// <para>
/// 写成扩展方法而不是往 <see cref="LyricLine"/> / <see cref="LyricSyllable"/> 里加成员，
/// 是为了让那三个 record 保持纯数据。
/// </para>
/// <para>
/// <b>行间隙在模型里不存在。</b> 行窗口是「下一行行首 − 本行行首」推出来的，所以
/// <c>Lines[i].End == Lines[i+1].Start</c> 恒成立（末行除外）。唯一真实存在的间隙是
/// <b>行内音节之间的</b>（长音唱完到下一个字之前）—— 那时高亮停在原地不继续爬，
/// 这是卡拉OK 该有的观感。
/// </para>
/// </remarks>
public static class LyricTimeline
{
    /// <summary>
    /// 行内定位：最后一个 <c>Start &lt;= position</c> 的音节。
    /// </summary>
    /// <returns>全部音节的起始都晚于 <paramref name="position"/> 时返回 <c>-1</c>。</returns>
    public static int IndexOfSyllableAt(this LyricLine line, TimeSpan position)
    {
        ArgumentNullException.ThrowIfNull(line);

        var low = 0;
        var high = line.Syllables.Count - 1;
        var found = -1;

        while (low <= high)
        {
            var mid = low + ((high - low) / 2);

            if (line.Syllables[mid].Start <= position)
            {
                found = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return found;
    }

    /// <summary>音节的播放进度，钳在 <c>[0,1]</c>。</summary>
    /// <remarks>时长为零的音节（异常数据）不产生 <c>NaN</c>：起始之前是 0，到达起始就是 1。</remarks>
    public static double ProgressAt(this LyricSyllable syllable, TimeSpan position)
    {
        ArgumentNullException.ThrowIfNull(syllable);

        return Progress(position - syllable.Start, syllable.Duration);
    }

    /// <summary>整行的播放进度，钳在 <c>[0,1]</c>。</summary>
    public static double ProgressAt(this LyricLine line, TimeSpan position)
    {
        ArgumentNullException.ThrowIfNull(line);

        return Progress(position - line.Start, line.Duration);
    }

    /// <summary>
    /// 一次拿到渲染一帧所需的全部定位信息。
    /// </summary>
    /// <remarks>
    /// 位置在第一行之前、或文档为空时不返回行（<see cref="LyricCursor.None"/>），
    /// 界面据此不高亮任何一行。位置在曲末之后<b>保持末行满高亮</b>，与
    /// <see cref="LyricDocument.IndexOfLineAt"/> 的既有取舍一致。
    /// </remarks>
    public static LyricCursor Locate(this LyricDocument document, TimeSpan position)
    {
        ArgumentNullException.ThrowIfNull(document);

        var lineIndex = document.IndexOfLineAt(position);
        if (lineIndex < 0)
        {
            return LyricCursor.None;
        }

        var line = document.Lines[lineIndex];

        // 解析器保证每行至少一个音节（逐行版整行合成一个），空的情况只可能来自手工构造。
        if (line.Syllables.Count == 0)
        {
            return new LyricCursor(lineIndex, -1, 0, line.ProgressAt(position));
        }

        var syllableIndex = line.IndexOfSyllableAt(position);

        // 行首到第一个音节之间：落在第一个音节上，但还没开口。
        if (syllableIndex < 0)
        {
            return new LyricCursor(lineIndex, 0, 0, line.ProgressAt(position));
        }

        return new LyricCursor(
            lineIndex,
            syllableIndex,
            line.Syllables[syllableIndex].ProgressAt(position),
            line.ProgressAt(position));
    }

    /// <summary>
    /// 本行唱完的时刻：最后一个音节的结束，钳进行的时间窗口。
    /// </summary>
    /// <remarks>
    /// <b>不能用 <see cref="LyricLine.End"/></b> —— 那是下一行的行首。长音的发光的衰减、
    /// 「本行是否唱完」的判断都要用这个值。
    /// </remarks>
    public static TimeSpan SpeechEnd(this LyricLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (line.Syllables.Count == 0)
        {
            return line.End;
        }

        var end = line.Syllables[^1].End;

        if (end < line.Start)
        {
            return line.Start;
        }

        return end > line.End ? line.End : end;
    }

    /// <summary>共用的进度算法：时长为零时不除零。</summary>
    private static double Progress(TimeSpan elapsed, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return elapsed < TimeSpan.Zero ? 0 : 1;
        }

        var value = elapsed.Ticks / (double)duration.Ticks;

        return value <= 0 ? 0 : value >= 1 ? 1 : value;
    }
}
