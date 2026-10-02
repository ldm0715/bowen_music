namespace Bodian.Core.Performance;

/// <summary>把连续尺寸请求合并到下一次布局；布局开销越大，留给合成预览的时间越多。</summary>
public sealed class ResizeLayoutBudget(TimeProvider time)
{
    private readonly TimeProvider _time = time ?? throw new ArgumentNullException(nameof(time));
    private long _lastLayout;
    private bool _hasLayout;
    private double _costMilliseconds;
    public static TimeSpan FrameBudget => TimeSpan.FromMilliseconds(1000.0 / 120);
    public bool CanReflowInteractively => _hasLayout && _costMilliseconds <= FrameBudget.TotalMilliseconds;
    public TimeSpan Interval => TimeSpan.FromMilliseconds(Math.Clamp(_costMilliseconds * 3, FrameBudget.TotalMilliseconds, 50));
    public TimeSpan Remaining => !_hasLayout ? TimeSpan.Zero
        : TimeSpan.FromTicks(Math.Max(0, (Interval - _time.GetElapsedTime(_lastLayout)).Ticks));

    public void RecordLayout(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
        _costMilliseconds = Math.Max(duration.TotalMilliseconds, _costMilliseconds * 0.75);
        _lastLayout = _time.GetTimestamp();
        _hasLayout = true;
    }

    public void Reset() => _hasLayout = false;
}
