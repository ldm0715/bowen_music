using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Bodian.WinUI.Services;

internal sealed class FrameStatistics(ILogger logger, string name)
{
    private readonly bool _enabled = Environment.GetEnvironmentVariable("BODIAN_LYRICS_DIAGNOSTICS") == "1";
    private long _started, _previous;
    private int _frames;
    private double _drawTotal, _maximumInterval;
    private readonly List<double> _drawTimes = new(650);

    public void Record(long start)
    {
        if (!_enabled) return;
        if (_started == 0) _started = start;
        var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        _drawTimes.Add(elapsed);
        _drawTotal += elapsed;
        if (_previous != 0) _maximumInterval = Math.Max(_maximumInterval, Stopwatch.GetElapsedTime(_previous, start).TotalMilliseconds);
        _previous = start;
        _frames++;
        var seconds = Stopwatch.GetElapsedTime(_started).TotalSeconds;
        if (seconds < 5) return;
        _drawTimes.Sort();
        logger.LogInformation("{Name}后台绘制：{Fps:F1} fps，平均 {Average:F2} ms，P99 {P99:F2} ms，最大帧间隔 {Interval:F2} ms",
            name, _frames / seconds, _drawTotal / _frames, _drawTimes[(int)Math.Ceiling(_frames * 0.99) - 1], _maximumInterval);
        Reset();
    }

    public void Reset()
    {
        _started = _previous = 0;
        _frames = 0;
        _drawTotal = _maximumInterval = 0;
        _drawTimes.Clear();
    }
}
