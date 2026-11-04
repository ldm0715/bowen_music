namespace Bowen.Core.Models.Lyrics;

/// <summary>海报里的一行歌词。译文行也是独立的一项。</summary>
/// <param name="Text">这一行的文字。</param>
/// <param name="IsTranslation">这一行是译文。</param>
/// <param name="Start">行首时间，用来定位「当前播放行」。</param>
public sealed record LyricPosterLine(string Text, bool IsTranslation, TimeSpan Start);

/// <summary>
/// 把歌词文档整理成海报可选的行。
/// </summary>
/// <remarks>
/// <b>原文与译文都是独立的选择项。</b> 用户可以在海报里只放译文、或原文译文混排，
/// 所以这里不把译文并进上一句，只剔除空行。选多少行不在这里限制。
/// </remarks>
public static class LyricPosterComposer
{
    /// <summary>按文档顺序返回所有非空行。</summary>
    public static IReadOnlyList<LyricPosterLine> Compose(LyricDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var lines = new List<LyricPosterLine>(document.Lines.Count);

        foreach (var line in document.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.Text))
            {
                continue;
            }

            lines.Add(new LyricPosterLine(line.Text, line.IsTranslation, line.Start));
        }

        return lines;
    }

    /// <summary>
    /// 给定播放位置，返回应默认勾选的那一行的下标。
    /// </summary>
    /// <returns>下标；位置在第一行之前时返回 <c>-1</c>。</returns>
    public static int IndexAt(IReadOnlyList<LyricPosterLine> lines, TimeSpan position)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var found = -1;
        for (var index = 0; index < lines.Count; index++)
        {
            if (lines[index].Start > position)
            {
                break;
            }

            found = index;
        }

        return found;
    }
}
