using System.Diagnostics;
using System.Runtime.InteropServices;
using Bodian.Core.Playback;
using Microsoft.Extensions.Logging;
using NAudio.Wave;

namespace Bodian.WinUI.Playback;

public sealed class AudioSpectrumSource(ILogger<AudioSpectrumSource> logger) : IDisposable
{
    private readonly object _gate = new();
    private bool _requested;
    private bool _workerRunning;
    private bool _disposed;
    private CaptureSession? _capture;
    private SpectrumFrame? _frame;

    // WASAPI 初始化和 Dispose（内部 Join 采集线程）只在同一个后台工作循环里执行。
    // UI 只修改期望状态，快速最小化/恢复也不会并发启动和释放同一采集器。
    public void Start() => SetRequested(true);
    public void Stop() => SetRequested(false);

    private void SetRequested(bool requested)
    {
        lock (_gate)
        {
            if (_disposed && requested) return;
            _requested = requested;
            if (!requested) Volatile.Write(ref _frame, null);
            if (_workerRunning || requested == (Volatile.Read(ref _capture) is not null)) return;
            _workerRunning = true;
            _ = Task.Run(ReconcileCapture);
        }
    }

    private void ReconcileCapture()
    {
        while (true)
        {
            bool requested;
            lock (_gate)
            {
                requested = _requested;
                if (requested == (_capture is not null))
                {
                    _workerRunning = false;
                    return;
                }
            }
            if (requested) StartCapture();
            else StopCapture();
        }
    }

    private void StartCapture()
    {
        WasapiLoopbackCapture? capture = null;
        try
        {
            capture = new WasapiLoopbackCapture();
            var format = capture.WaveFormat;
            capture.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(format.SampleRate, format.Channels);
            var session = new CaptureSession(capture, new AudioSpectrumAnalyzer(format.SampleRate), format.Channels);
            Volatile.Write(ref _capture, session);
            capture.DataAvailable += OnDataAvailable;
            capture.RecordingStopped += OnRecordingStopped;
            capture.StartRecording();
            logger.LogInformation("歌词频谱已启用：输出回环 {SampleRate} Hz，{Channels} 声道", format.SampleRate, format.Channels);
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _capture, null);
            if (capture is not null) ReleaseCapture(capture);
            lock (_gate) _requested = false;
            logger.LogWarning(exception, "歌词频谱采样不可用");
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs args)
    {
        var session = Volatile.Read(ref _capture);
        if (session is null || !ReferenceEquals(sender, session.Capture)) return;
        var samples = MemoryMarshal.Cast<byte, float>(args.Buffer.AsSpan(0, args.BytesRecorded));
        session.Analyzer.Append(samples, session.Channels);
        var levels = new float[AudioSpectrumAnalyzer.BandCount];
        session.Analyzer.CopyLevels(levels);
        Volatile.Write(ref _frame, new SpectrumFrame(session, levels, Stopwatch.GetTimestamp()));
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs args)
    {
        if (args.Exception is not null) logger.LogWarning(args.Exception, "歌词频谱采样已停止");
    }

    public void CopyLevels(Span<float> destination)
    {
        var frame = Volatile.Read(ref _frame);
        if (frame is null || !ReferenceEquals(frame.Session, Volatile.Read(ref _capture))
            || Stopwatch.GetElapsedTime(frame.Timestamp).TotalMilliseconds > 250)
            destination.Clear();
        else
            frame.Levels.AsSpan().CopyTo(destination);
    }

    private void StopCapture()
    {
        var session = Interlocked.Exchange(ref _capture, null);
        Volatile.Write(ref _frame, null);
        if (session is not null) ReleaseCapture(session.Capture);
    }

    private void ReleaseCapture(WasapiLoopbackCapture capture)
    {
        capture.DataAvailable -= OnDataAvailable;
        capture.RecordingStopped -= OnRecordingStopped;
        try { capture.Dispose(); }
        catch (Exception exception) { logger.LogWarning(exception, "释放歌词频谱采集器失败"); }
    }

    public void Dispose()
    {
        lock (_gate) _disposed = true;
        Stop();
    }

    private sealed record CaptureSession(WasapiLoopbackCapture Capture, AudioSpectrumAnalyzer Analyzer, int Channels);
    private sealed record SpectrumFrame(CaptureSession Session, float[] Levels, long Timestamp);
}
