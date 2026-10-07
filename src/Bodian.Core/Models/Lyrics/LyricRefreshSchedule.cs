namespace Bodian.Core.Models.Lyrics;

/// <summary>稳定画面等到下一时间点；只有扫色或长句滚动时需要连续刷新。</summary>
public static class LyricRefreshSchedule
{
    public static readonly TimeSpan AnimationInterval = TimeSpan.FromSeconds(1.0 / 60);

    public static TimeSpan? NextUpdateDelay(LyricDocument document, TimeSpan position, bool playing,
        bool animateHighlight, bool overflowing)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!playing || document.IsEmpty) return null;
        var index = document.IndexOfLineAt(position);
        TimeSpan? next = index + 1 < document.Lines.Count ? document.Lines[index + 1].Start : null;
        if (index < 0) return Until(next, position);
        var line = document.Lines[index];
        if (document.Kind == LyricKind.WordByWord)
        {
            var animated = false;
            foreach (var syllable in line.Syllables)
            {
                if (syllable.Start > position && (!next.HasValue || syllable.Start < next.Value)) next = syllable.Start;
                if (syllable.Text.Length > 0 && syllable.Duration > TimeSpan.Zero
                    && position >= syllable.Start && position < syllable.End) animated = true;
            }
            if (animated && (animateHighlight || overflowing)) return AnimationInterval;
            // 缺少有效逐字时序时，长句与 LyricHorizontalScroll 一样回退到行时间。
            if (overflowing && (line.Syllables.Count == 0 || line.SpeechEnd() <= line.Start)
                && position < line.End) return AnimationInterval;
        }
        else if (overflowing && position < line.End) return AnimationInterval;
        return Until(next, position);
    }

    private static TimeSpan? Until(TimeSpan? next, TimeSpan position)
        => next.HasValue ? TimeSpan.FromTicks(Math.Max(TimeSpan.TicksPerMillisecond, (next.Value - position).Ticks)) : null;
}
