namespace Bodian.Core.Models.Lyrics;

/// <summary>长句保持字号，让唱到的位置留在视口中间；句首、句尾分别停在两端。</summary>
public static class LyricHorizontalScroll
{
    public static double Offset(double textWidth, double viewportWidth, double progress)
    {
        if (!double.IsFinite(textWidth) || !double.IsFinite(viewportWidth)
            || viewportWidth <= 0 || textWidth <= viewportWidth) return 0;
        progress = double.IsFinite(progress) ? Math.Clamp(progress, 0, 1) : 0;
        return Math.Clamp(textWidth * progress - viewportWidth / 2, 0, textWidth - viewportWidth);
    }

    /// <summary>小窗没有字形扫色，按音节文本长度估算唱到的位置；逐行歌词按行时间推进。</summary>
    public static double ProgressAt(LyricLine line, LyricKind kind, TimeSpan position)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (position < line.Start) return 0;
        if (kind == LyricKind.WordByWord && line.SpeechEnd() > line.Start)
        {
            var characters = 0.0;
            var sung = 0.0;
            foreach (var syllable in line.Syllables)
            {
                characters += syllable.Text.Length;
                sung += syllable.Text.Length * syllable.ProgressAt(position);
            }
            if (characters > 0) return Math.Clamp(sung / characters, 0, 1);
        }
        return line.ProgressAt(position);
    }
}
