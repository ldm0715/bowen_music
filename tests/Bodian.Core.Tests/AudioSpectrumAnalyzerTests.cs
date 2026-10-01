using Bodian.Core.Playback;
using Xunit;

namespace Bodian.Core.Tests;

public sealed class AudioSpectrumAnalyzerTests
{
    [Fact]
    public void SilenceProducesNoSpectrum()
    {
        var analyzer = new AudioSpectrumAnalyzer(48000);
        analyzer.Append(new float[AudioSpectrumAnalyzer.SampleCount], 1);
        var levels = new float[AudioSpectrumAnalyzer.BandCount];
        analyzer.CopyLevels(levels);
        Assert.All(levels, level => Assert.Equal(0, level));
    }

    [Theory]
    [InlineData(48000, 1000)]
    [InlineData(44100, 4000)]
    public void TonePeaksInTheCorrespondingLogarithmicBand(int sampleRate, int frequency)
    {
        var analyzer = new AudioSpectrumAnalyzer(sampleRate);
        var samples = Tone(sampleRate, frequency);
        analyzer.Append(samples, 1);
        var levels = new float[AudioSpectrumAnalyzer.BandCount];
        analyzer.CopyLevels(levels);
        var peak = Array.IndexOf(levels, levels.Max());
        var expected = Math.Log(frequency / 40.0) / Math.Log(16000 / 40.0) * AudioSpectrumAnalyzer.BandCount;
        Assert.InRange(peak, (int)expected - 1, (int)expected + 1);
        Assert.InRange(levels[peak], 0.85f, 1);
        Assert.All(levels, level => Assert.True(float.IsFinite(level)));
    }

    [Fact]
    public void ChunkedSamplesProduceTheSameSpectrum()
    {
        var complete = new AudioSpectrumAnalyzer(48000);
        var chunked = new AudioSpectrumAnalyzer(48000);
        var samples = Tone(48000, 1500);
        complete.Append(samples, 1);
        chunked.Append(samples.AsSpan(0, 400), 1);
        chunked.Append(samples.AsSpan(400, 500), 1);
        chunked.Append(samples.AsSpan(900), 1);
        var expected = new float[AudioSpectrumAnalyzer.BandCount];
        var actual = new float[AudioSpectrumAnalyzer.BandCount];
        complete.CopyLevels(expected);
        chunked.CopyLevels(actual);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void StereoToneDoesNotChangeItsFrequency()
    {
        var analyzer = new AudioSpectrumAnalyzer(48000);
        var mono = Tone(48000, 2000);
        var stereo = new float[mono.Length * 2];
        for (var index = 0; index < mono.Length; index++)
            stereo[index * 2] = stereo[index * 2 + 1] = mono[index];
        analyzer.Append(stereo, 2);
        var levels = new float[AudioSpectrumAnalyzer.BandCount];
        analyzer.CopyLevels(levels);
        Assert.InRange(Array.IndexOf(levels, levels.Max()), 30, 32);
    }

    private static float[] Tone(int sampleRate, int frequency)
    {
        var samples = new float[AudioSpectrumAnalyzer.SampleCount];
        for (var index = 0; index < samples.Length; index++)
            samples[index] = (float)(0.5 * Math.Sin(2 * Math.PI * frequency * index / sampleRate));
        return samples;
    }
}
