using Bodian.Core.Models.Lyrics;

namespace Bodian.WinUI.LyricRenderer;

/// <summary>每行独立保留位置和速度，换行时从当前运动状态接续弹簧。</summary>
internal sealed class LyricsScrollAnimator
{
    private double[] _positions = [];
    private double[] _velocities = [];
    private double[] _targets = [];
    private TimeSpan _started;
    private int _anchor;
    private bool _stagger = true;
    public int Count => _positions.Length;

    public void Reset(int count, double target)
    {
        _positions = new double[count];
        _velocities = new double[count];
        _targets = new double[count];
        Array.Fill(_positions, target);
        Array.Fill(_targets, target);
        Target = target;
    }

    public double Target { get; private set; }

    public void Retarget(double target, int anchor, TimeSpan now, bool stagger = true)
    {
        Target = target;
        _started = now;
        _anchor = anchor;
        _stagger = stagger;
    }

    public void Update(TimeSpan now, double seconds)
    {
        for (var i = 0; i < _positions.Length; i++)
        {
            var delay = _stagger ? Math.Min(0.21, Math.Abs(i - _anchor) * 0.035) : 0;
            // 只积分延迟结束后的时间，避免低帧率下各行同时起步。
            var activeSeconds = Math.Min(seconds, Math.Max(0, (now - _started).TotalSeconds - delay));
            var waitingSeconds = Math.Max(0, seconds - activeSeconds);
            if (waitingSeconds > 0)
                (_positions[i], _velocities[i]) = LyricMotionMath.AdvanceSpring(
                    _positions[i], _velocities[i], _targets[i], waitingSeconds);
            if (activeSeconds <= 0) continue;
            _targets[i] = Target;
            (_positions[i], _velocities[i]) = LyricMotionMath.AdvanceSpring(
                _positions[i], _velocities[i], _targets[i], activeSeconds);
        }
    }

    public double OffsetAt(int index) => _positions[index];
}
