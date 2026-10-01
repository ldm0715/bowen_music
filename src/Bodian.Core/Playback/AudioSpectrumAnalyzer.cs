namespace Bodian.Core.Playback;

public sealed class AudioSpectrumAnalyzer
{
    public const int SampleCount = 2048;
    public const int BandCount = 48;

    private readonly float[] _samples = new float[SampleCount];
    private readonly double[] _real = new double[SampleCount];
    private readonly double[] _imaginary = new double[SampleCount];
    private readonly double[] _window = new double[SampleCount];
    private readonly float[] _levels = new float[BandCount];
    private readonly (int First, int Last)[] _bins = new (int, int)[BandCount];
    private int _cursor;
    private long _generation;
    private long _analyzedGeneration = -1;

    public AudioSpectrumAnalyzer(int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        var maximumFrequency = Math.Min(16000, sampleRate / 2.0);
        for (var index = 0; index < SampleCount; index++)
            _window[index] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * index / (SampleCount - 1));
        for (var band = 0; band < BandCount; band++)
        {
            var low = 40 * Math.Pow(maximumFrequency / 40, band / (double)BandCount);
            var high = 40 * Math.Pow(maximumFrequency / 40, (band + 1) / (double)BandCount);
            var first = Math.Clamp((int)Math.Floor(low * SampleCount / sampleRate), 1, SampleCount / 2);
            var last = Math.Clamp((int)Math.Ceiling(high * SampleCount / sampleRate), first, SampleCount / 2);
            _bins[band] = (first, last);
        }
    }

    public void Append(ReadOnlySpan<float> interleaved, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        var frameCount = interleaved.Length / channels;
        for (var frame = Math.Max(0, frameCount - SampleCount); frame < frameCount; frame++)
        {
            var sum = 0.0;
            for (var channel = 0; channel < channels; channel++)
                sum += interleaved[frame * channels + channel];
            var sample = sum / channels;
            _samples[_cursor] = double.IsFinite(sample) ? (float)sample : 0;
            _cursor = (_cursor + 1) & (SampleCount - 1);
        }
        if (frameCount > 0) _generation++;
    }

    public void CopyLevels(Span<float> destination)
    {
        if (destination.Length < BandCount)
            throw new ArgumentException("Spectrum destination must contain 48 bands.", nameof(destination));
        if (_analyzedGeneration != _generation)
        {
            Analyze();
            _analyzedGeneration = _generation;
        }
        _levels.CopyTo(destination);
    }

    private void Analyze()
    {
        for (var index = 0; index < SampleCount; index++)
        {
            _real[index] = _samples[(_cursor + index) & (SampleCount - 1)] * _window[index];
            _imaginary[index] = 0;
        }
        var reversed = 0;
        for (var index = 1; index < SampleCount; index++)
        {
            var bit = SampleCount / 2;
            while ((reversed & bit) != 0) { reversed ^= bit; bit >>= 1; }
            reversed ^= bit;
            if (index >= reversed) continue;
            (_real[index], _real[reversed]) = (_real[reversed], _real[index]);
            (_imaginary[index], _imaginary[reversed]) = (_imaginary[reversed], _imaginary[index]);
        }
        for (var length = 2; length <= SampleCount; length *= 2)
        {
            var angle = -2 * Math.PI / length;
            var stepReal = Math.Cos(angle);
            var stepImaginary = Math.Sin(angle);
            for (var start = 0; start < SampleCount; start += length)
            {
                var phaseReal = 1.0;
                var phaseImaginary = 0.0;
                for (var offset = 0; offset < length / 2; offset++)
                {
                    var left = start + offset;
                    var right = left + length / 2;
                    var rightReal = _real[right] * phaseReal - _imaginary[right] * phaseImaginary;
                    var rightImaginary = _real[right] * phaseImaginary + _imaginary[right] * phaseReal;
                    _real[right] = _real[left] - rightReal;
                    _imaginary[right] = _imaginary[left] - rightImaginary;
                    _real[left] += rightReal;
                    _imaginary[left] += rightImaginary;
                    (phaseReal, phaseImaginary) = (phaseReal * stepReal - phaseImaginary * stepImaginary,
                        phaseReal * stepImaginary + phaseImaginary * stepReal);
                }
            }
        }
        for (var band = 0; band < BandCount; band++)
        {
            var power = 0.0;
            for (var bin = _bins[band].First; bin <= _bins[band].Last; bin++)
                power = Math.Max(power, _real[bin] * _real[bin] + _imaginary[bin] * _imaginary[bin]);
            var amplitude = Math.Sqrt(power) * 4 / SampleCount;
            _levels[band] = amplitude <= 0.0001 ? 0 : (float)Math.Clamp((20 * Math.Log10(amplitude) + 80) / 80, 0, 1);
        }
    }
}
