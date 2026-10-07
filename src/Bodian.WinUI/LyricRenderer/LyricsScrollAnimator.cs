using Bodian.Core.Models.Lyrics;

namespace Bodian.WinUI.LyricRenderer;

/// <summary>所有行单向收敛到锚点，保留独立错峰起步。</summary>
internal sealed class LyricsScrollAnimator
{
    private double[] _positions = [];
    private double[] _velocities = [];
    private double[] _targets = [];
    private TimeSpan _started;
    private int _anchor;
    private bool _stagger = true;
    public int Count => _positions.Length;
    public bool IsAnimating { get; private set; }
    public bool HasMoved { get; private set; }

    public void Reset(int count, double target)
    {
        _positions = new double[count];
        _velocities = new double[count];
        _targets = new double[count];
        Array.Fill(_positions, target);
        Array.Fill(_targets, target);
        Target = target;
        IsAnimating = HasMoved = false;
    }

    public double Target { get; private set; }

    public void Retarget(double target, int anchor, TimeSpan now, bool stagger = true)
    {
        Target = target;
        _started = now;
        _anchor = anchor;
        _stagger = stagger;
        IsAnimating = _positions.Any(position => position != target);
    }

    public void Update(TimeSpan now, double seconds)
    {
        IsAnimating = HasMoved = false;
        for (var i = 0; i < _positions.Length; i++)
        {
            var previous = _positions[i];
            if (!_stagger || i == _anchor)
            {
                _targets[i] = Target;
                (_positions[i], _velocities[i]) = LyricMotionMath.AdvanceSettlingSpring(
                    _positions[i], _velocities[i], Target, seconds);
                RecordMotion(i, previous);
                continue;
            }
            var delay = _stagger ? Math.Min(0.21, Math.Abs(i - _anchor) * 0.035) : 0;
            // 只积分延迟结束后的时间，避免低帧率下各行同时起步。
            var activeSeconds = Math.Min(seconds, Math.Max(0, (now - _started).TotalSeconds - delay));
            var waitingSeconds = Math.Max(0, seconds - activeSeconds);
            if (waitingSeconds > 0)
                (_positions[i], _velocities[i]) = LyricMotionMath.AdvanceSettlingSpring(
                    _positions[i], _velocities[i], _targets[i], waitingSeconds);
            if (activeSeconds <= 0) { RecordMotion(i, previous); continue; }
            _targets[i] = Target;
            (_positions[i], _velocities[i]) = LyricMotionMath.AdvanceSettlingSpring(
                _positions[i], _velocities[i], _targets[i], activeSeconds);
            RecordMotion(i, previous);
        }
    }

    private void RecordMotion(int index, double previous)
    {
        HasMoved |= previous != _positions[index];
        IsAnimating |= _positions[index] != Target || _velocities[index] != 0;
    }

    public double OffsetAt(int index) => _positions[index];
}
