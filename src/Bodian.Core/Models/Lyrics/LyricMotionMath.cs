namespace Bodian.Core.Models.Lyrics;

/// <summary>与帧率无关的歌词弹簧与连续扫光计算。</summary>
public static class LyricMotionMath
{
    public static TimeSpan AdvanceHighlight(TimeSpan previous, TimeSpan observed, bool discontinuity)
        => discontinuity || observed >= previous ? observed : previous;

    public static int NearestLine(ReadOnlySpan<double> centers, double anchor)
    {
        var nearest = -1;
        var distance = double.PositiveInfinity;
        for (var index = 0; index < centers.Length; index++)
        {
            var candidate = Math.Abs(centers[index] - anchor);
            if (candidate >= distance) continue;
            nearest = index;
            distance = candidate;
        }
        return nearest;
    }

    public static (double Position, double Velocity) AdvanceSpring(
        double position, double velocity, double target, double seconds,
        double frequency = 14, double damping = 0.82)
    {
        if (seconds <= 0) return (position, velocity);
        if (frequency <= 0) return (target, 0);
        damping = Math.Clamp(damping, 0.01, 1);
        var displacement = position - target;
        var decay = Math.Exp(-damping * frequency * seconds);
        double next, speed;
        if (damping >= 0.999)
        {
            var coefficient = velocity + frequency * displacement;
            next = (displacement + coefficient * seconds) * decay;
            speed = (velocity - frequency * coefficient * seconds) * decay;
        }
        else
        {
            var oscillation = frequency * Math.Sqrt(1 - damping * damping);
            var coefficient = (velocity + damping * frequency * displacement) / oscillation;
            var cosine = Math.Cos(oscillation * seconds);
            var sine = Math.Sin(oscillation * seconds);
            next = decay * (displacement * cosine + coefficient * sine);
            speed = -damping * frequency * next
                + decay * oscillation * (-displacement * sine + coefficient * cosine);
        }
        return Math.Abs(next) < 0.001 && Math.Abs(speed) < 0.001
            ? (target, 0) : (target + next, speed);
    }

    /// <summary>将一个音节的进度按排版宽度分配到它包含的字形。</summary>
    public static double GlyphProgress(double syllableProgress, double offset, double width, double totalWidth)
        => width <= 0 || totalWidth <= 0 ? Math.Clamp(syllableProgress, 0, 1)
            : Math.Clamp((Math.Clamp(syllableProgress, 0, 1) * totalWidth - offset) / width, 0, 1);

}
