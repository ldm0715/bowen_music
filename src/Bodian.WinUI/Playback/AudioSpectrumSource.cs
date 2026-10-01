using System.Diagnostics;
using System.Runtime.InteropServices;
using Bodian.Core.Playback;
using Microsoft.Extensions.Logging;
using NAudio.Wave;

namespace Bodian.WinUI.Playback;

public sealed class AudioSpectrumSource(ILogger<AudioSpectrumSource> logger) : IDisposable
{
    private readonly object _gate = new();
    private WasapiLoopbackCapture? _capture;
    private AudioSpectrumAnalyzer? _analyzer;
    private long _lastSamples;
    private int _channels;

    public void Start()
    {
        if (_capture is not null) return;
        try
        {
            var capture = new WasapiLoopbackCapture();
            var format = capture.WaveFormat;
            capture.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(format.SampleRate, format.Channels);
            _channels = format.Channels;
            lock (_gate)
            {
                _analyzer = new AudioSpectrumAnalyzer(format.SampleRate);
                _lastSamples = 0;
                _capture = capture;
            }
            capture.DataAvailable += OnDataAvailable;
            capture.RecordingStopped += OnRecordingStopped;
            capture.StartRecording();
            logger.LogInformation("歌词频谱已启用：输出回环 {SampleRate} Hz，{Channels} 声道", format.SampleRate, format.Channels);
        }
        catch (Exception exception)
        {
            Stop();
            logger.LogWarning(exception, "歌词频谱采样不可用");
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs args)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(sender, _capture) || _analyzer is null) return;
            var samples = MemoryMarshal.Cast<byte, float>(args.Buffer.AsSpan(0, args.BytesRecorded));
            _analyzer.Append(samples, _channels);
            _lastSamples = Stopwatch.GetTimestamp();
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs args)
    {
        if (args.Exception is not null) logger.LogWarning(args.Exception, "歌词频谱采样已停止");
    }

    public void CopyLevels(Span<float> destination)
    {
        lock (_gate)
        {
            if (_analyzer is null || _lastSamples == 0 || Stopwatch.GetElapsedTime(_lastSamples).TotalMilliseconds > 250)
                destination.Clear();
            else
                _analyzer.CopyLevels(destination);
        }
    }

    public void Stop()
    {
        WasapiLoopbackCapture? capture;
        lock (_gate)
        {
            capture = _capture;
            _capture = null;
            _analyzer = null;
            _lastSamples = 0;
        }
        if (capture is null) return;
        capture.DataAvailable -= OnDataAvailable;
        capture.RecordingStopped -= OnRecordingStopped;
        capture.Dispose();
    }

    public void Dispose() => Stop();
}
