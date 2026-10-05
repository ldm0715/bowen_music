namespace Bodian.Core.Models;

/// <summary>从当前歌词视口计算完整的行高；字号与窗口尺寸暂时不同步时也容纳两行。</summary>
public readonly record struct DesktopLyricsLayoutMetrics(double FontSize, double LineHeight, double Gap, double Top)
{
    public static DesktopLyricsLayoutMetrics Calculate(double requestedFontSize, double viewportHeight, bool dualLine)
    {
        var rows = dualLine ? 2 : 1;
        var height = Math.Max(0.01, viewportHeight);
        var gap = dualLine ? Math.Min(4, height / 3) : 0;
        var lineHeight = Math.Min(Math.Max(0.01, requestedFontSize) * 1.4, (height - gap) / rows);
        var fontSize = lineHeight / 1.4;
        var top = Math.Max(0, (height - lineHeight * rows - gap) / 2);
        return new DesktopLyricsLayoutMetrics(fontSize, lineHeight, gap, top);
    }
}
