namespace Bodian.Core.Models.Lyrics;

/// <summary>
/// 歌词动效的纯数学。
/// </summary>
/// <remarks>
/// <para>
/// 参数与算法对齐自 <c>jayfunc/BetterLyrics</c>（GPL-3.0）的
/// <c>BetterLyrics.Core/Helpers/Lyrics/LyricsAnimator.cs</c> 与
/// <c>BetterLyrics.WinUI3/BetterLyrics.WinUI3/Renderer/LyricsRenderer/</c>。
/// 本项目是 GPL-3.0，可直接移植；这里是<b>按公式自行实现</b>，没有逐行抄写。
/// </para>
/// <para>
/// 放在 Core 而不是 UI 项目里，是为了能单测 —— <c>Bodian.WinUI</c> 没有测试工程，
/// 也不该为它建一个 WinUI 测试宿主。这些函数全是 <see cref="double"/> 运算，没有 UI 依赖。
/// </para>
/// </remarks>
public static class LyricEffectMath
{
    /// <summary>
    /// 一行离当前行的归一化距离：<c>0</c> 是当前行，<c>1</c> 是最远的可见行。
    /// </summary>
    /// <param name="lineY">本行在视口里的 Y。</param>
    /// <param name="playingLineY">当前行在视口里的 Y。</param>
    /// <param name="spaceBefore">当前行<b>之上</b>的可用空间。</param>
    /// <param name="spaceAfter">当前行<b>之下</b>的可用空间。</param>
    /// <remarks>
    /// <b>按像素距离算，不按下标算。</b> 当前行会被放大，行高因此与其他行不同，
    /// 按下标会让「视觉上的远近」与算出来的距离对不上。分母上下不同，
    /// 是因为当前行在视口里的位置并不居中。
    /// </remarks>
    public static double DistanceFactor(double lineY, double playingLineY, double spaceBefore, double spaceAfter)
    {
        var space = lineY < playingLineY ? spaceBefore : spaceAfter;

        if (space <= 0)
        {
            return 0;
        }

        return Math.Clamp(Math.Abs(lineY - playingLineY) / space, 0, 1);
    }

    /// <summary>Quad + Out 缓动。</summary>
    public static double EaseOutQuad(double t)
    {
        var clamped = Math.Clamp(t, 0, 1);
        var inverse = 1 - clamped;

        return 1 - (inverse * inverse);
    }

    /// <summary>
    /// Apple Music 式指数错峰延迟（秒）。以首个可见行为波源，越远的行延迟越大，
    /// 滚动因此是波浪式推进而不是整块平移。
    /// </summary>
    /// <param name="visibleIndex">距首个可见行的行数，波源为 <c>0</c>。</param>
    /// <param name="scrollDuration">这一行的滚动时长（秒）。</param>
    /// <param name="budgetRatio">预算占滚动时长的比例。</param>
    /// <param name="budgetMax">预算上限（秒）。</param>
    /// <param name="perLineFactor">每行的错峰系数（秒）。</param>
    /// <remarks>
    /// <b>上游默认关闭这个效果</b>（<c>LyricsScrollBottomDelay</c> 默认 <c>0</c>，
    /// 源码里 <c>if (delay &gt; 0)</c> 才启用）。本项目<b>默认开启</b> —— 观感更好，
    /// 而实际用户不会去改这个开关。<paramref name="perLineFactor"/> 是主动偏离的取值。
    /// </remarks>
    public static double StaggerDelay(
        int visibleIndex,
        double scrollDuration,
        double budgetRatio,
        double budgetMax,
        double perLineFactor)
    {
        if (visibleIndex <= 0 || perLineFactor <= 0)
        {
            return 0;
        }

        var budget = Math.Min(budgetMax, Math.Max(0, scrollDuration) * budgetRatio);

        if (budget <= 0)
        {
            return 0;
        }

        return budget * (1 - Math.Exp(-visibleIndex * perLineFactor / budget));
    }

    /// <summary>
    /// 一行实际的滚动时长：基准加上按距离加权的上下段增量。
    /// </summary>
    /// <param name="canvasTransDuration">基准时长（秒）。</param>
    /// <param name="distanceFactor">本行离当前行的归一化距离。</param>
    /// <param name="visibleIndex">距首个可见行的行数。</param>
    /// <param name="totalVisible">可见行总数。</param>
    /// <param name="topDuration">当前行之上的那一段时长（秒）。</param>
    /// <param name="bottomDuration">当前行之下的那一段时长（秒）。</param>
    /// <remarks>
    /// 三段时长不是「各管一段」，而是<b>两个增量</b>：距离决定强度，在可见窗口里的
    /// 位置决定用上段的还是下段的。这样才有「上进下出」的不对称手感。
    /// </remarks>
    public static double ScrollDuration(
        double canvasTransDuration,
        double distanceFactor,
        int visibleIndex,
        int totalVisible,
        double topDuration,
        double bottomDuration)
    {
        var total = Math.Max(1, totalVisible);
        var factor = Math.Clamp(visibleIndex / (double)total, 0, 1);
        var topExtra = distanceFactor * (topDuration - canvasTransDuration);
        var bottomExtra = distanceFactor * (bottomDuration - canvasTransDuration);

        return canvasTransDuration + ((1 - factor) * topExtra) + (factor * bottomExtra);
    }

    /// <summary>
    /// 一个视觉行内的扫光进度：<c>Σ(字宽 × 该字进度) / 视觉行宽</c>。
    /// </summary>
    /// <param name="charWidths">该视觉行内每个字的排版宽度，按顺序。</param>
    /// <param name="charProgress">每个字的播放进度，与上者一一对应。</param>
    /// <param name="regionWidth">视觉行的总宽度。</param>
    /// <remarks>
    /// <b>扫光是渐变画刷画出来的，不是裁剪几何。</b> 这个值直接喂给
    /// <c>CanvasLinearGradientBrush</c> 的渐变停靠点位置。
    /// 累加在遇到第一个未唱完的字时停下 —— 它后面的字一定还没开口。
    /// </remarks>
    public static double RegionPlayProgress(
        ReadOnlySpan<double> charWidths,
        ReadOnlySpan<double> charProgress,
        double regionWidth)
    {
        if (regionWidth <= 0)
        {
            return 0;
        }

        var count = Math.Min(charWidths.Length, charProgress.Length);
        var played = 0.0;

        for (var i = 0; i < count; i++)
        {
            var progress = Math.Clamp(charProgress[i], 0, 1);

            if (progress <= 0)
            {
                break;
            }

            played += charWidths[i] * progress;

            if (progress < 1)
            {
                break;
            }
        }

        return Math.Clamp(played / regionWidth, 0, 1);
    }
}
