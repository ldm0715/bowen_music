using Bodian.Core.Models.Lyrics;

namespace Bodian.WinUI.LyricRenderer;

/// <summary>
/// 滚动的波浪式推进。
/// </summary>
/// <remarks>
/// <para>
/// <b>波浪在「每行自己的时长与延迟」里。</b> 一次滚动只有一个起点与终点，但每一行用
/// <c>duration</c> 与 <c>delay</c> 各自插值 —— 延迟随离当前行的距离指数增长，
/// 于是越远的行起步越晚，整体看起来是波浪推进而不是整块平移。
/// </para>
/// <para>
/// 公式与参数来源见 <see cref="LyricEffectMath.ScrollDuration"/> 与
/// <see cref="LyricEffectMath.StaggerDelay"/>（取自 BetterLyrics，GPL-3.0）。
/// </para>
/// <para>
/// <b>只保存起点、终点与起始时刻</b>；每行的插值进度由调用方按自己的 duration/delay 算，
/// 所以这里没有「每行一个过渡对象」的负担。
/// </para>
/// </remarks>
internal sealed class LyricsScrollAnimator
{
    private double _from;
    private double _to;
    private TimeSpan _start;
    private bool _active;

    /// <summary>本次滚动的目标偏移。</summary>
    public double Target => _to;

    /// <summary>当前是否还在动。</summary>
    public bool IsAnimating => _active;

    /// <summary>
    /// 直接落到目标位置，不做动画。
    /// </summary>
    /// <remarks>拖动进度条、切歌、以及第一次布局时用 —— 那些场合做波浪反而像卡顿。</remarks>
    public void JumpTo(double offset)
    {
        _from = offset;
        _to = offset;
        _active = false;
    }

    /// <summary>
    /// 滚到新位置。
    /// </summary>
    /// <param name="from">本次动画的起点（当前实际偏移）。</param>
    /// <param name="target">目标偏移。</param>
    /// <param name="now">当前时刻。</param>
    public void ScrollTo(double from, double target, TimeSpan now)
    {
        if (_active && Math.Abs(target - _to) < 0.5)
        {
            return;   // 目标没变，别把进行中的动画重启
        }

        _from = from;
        _to = target;
        _start = now;
        _active = true;
    }

    /// <summary>
    /// 某一行在这一刻的偏移。
    /// </summary>
    /// <param name="durationSeconds">该行的滚动时长（秒）。</param>
    /// <param name="delaySeconds">该行的错峰延迟（秒）。</param>
    /// <param name="now">当前时刻。</param>
    public double OffsetAt(double durationSeconds, double delaySeconds, TimeSpan now)
    {
        if (!_active || durationSeconds <= 0)
        {
            return _to;
        }

        var elapsed = (now - _start).TotalSeconds;
        var progress = (elapsed - delaySeconds) / durationSeconds;

        if (progress <= 0)
        {
            return _from;   // 还没轮到这一行
        }

        if (progress >= 1)
        {
            return _to;
        }

        return _from + ((_to - _from) * LyricEffectMath.EaseOutQuad(progress));
    }

    /// <summary>
    /// 全部行是否都已经落定。
    /// </summary>
    /// <remarks>落定之后 <see cref="OffsetAt"/> 恒返回终点，调用方可以省掉逐行的插值计算。</remarks>
    public bool IsSettled(TimeSpan now, double maxDurationSeconds, double maxDelaySeconds)
        => !_active || (now - _start).TotalSeconds >= maxDurationSeconds + maxDelaySeconds;
}
