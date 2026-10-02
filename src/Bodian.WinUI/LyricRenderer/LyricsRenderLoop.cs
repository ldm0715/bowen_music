using System.Collections.Concurrent;
using System.Diagnostics;
using Bodian.Core.Models.Lyrics;
using Bodian.Core.Playback;
using Bodian.WinUI.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Composition;
using Microsoft.UI.Composition;
using Windows.Foundation;

namespace Bodian.WinUI.LyricRenderer;

/// <summary>歌词表面、排版和绘制由一个后台循环独占；UI 只发布最新输入，不等待绘制。</summary>
internal sealed class LyricsRenderLoop
{
    private static readonly TimeSpan ResizeDelay = TimeSpan.FromMilliseconds(120);
    private readonly CanvasDevice _device;
    private readonly CompositionGraphicsDevice _graphicsDevice;
    private readonly CompositionDrawingSurface _surface;
    private readonly LyricsRenderer _renderer;
    private readonly LyricsPlaybackClock _clock;
    private readonly ILogger _logger;
    private readonly Action<bool> _browsingChanged;
    private readonly Action _failed;
    private readonly Action _releaseGraphics;
    private readonly ConcurrentQueue<Action<LyricsRenderer>> _commands = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly AutoResetEvent _resume = new(false);
    private readonly Stopwatch _animationClock;
    private readonly bool _diagnostics = Environment.GetEnvironmentVariable("BODIAN_LYRICS_DIAGNOSTICS") == "1";
    private RenderInput _input = new(LyricDocument.Empty, 0, 0, 1, 40, 0);
    private int _paused = 1;
    private int _forceResize;
    private int _stopped;
    private bool _browsing;
    private double _width, _height, _scale, _fontSize = 40;
    private long _statsStarted, _previousDraw;
    private int _statsFrames;
    private double _statsDrawMilliseconds, _statsMaxMilliseconds, _statsMaxFrameInterval;
    private readonly List<double> _frameIntervals = new(650);
    private readonly List<double> _drawTimes = new(650);

    public LyricsRenderLoop(CanvasDevice device, CompositionGraphicsDevice graphicsDevice,
        CompositionDrawingSurface surface, LyricsPlaybackClock clock, Stopwatch animationClock, ILoggerFactory factory,
        Action<bool> browsingChanged, Action failed, Action releaseGraphics)
    {
        _device = device;
        _graphicsDevice = graphicsDevice;
        _surface = surface;
        _clock = clock;
        _animationClock = animationClock;
        _logger = factory.CreateLogger<LyricsRenderLoop>();
        _renderer = new LyricsRenderer(LyricsRenderSettings.Default, factory.CreateLogger<LyricsRenderer>());
        _browsingChanged = browsingChanged;
        _failed = failed;
        _releaseGraphics = releaseGraphics;
    }

    public void Start()
    {
        var thread = new Thread(Run) { IsBackground = true, Name = "Bodian lyrics renderer" };
        thread.Start();
    }
    public int LineIndexAt(double y) => _renderer.LineIndexAt(y);
    public void Send(Action<LyricsRenderer> command) => _commands.Enqueue(command);
    public void SetDocument(LyricDocument document)
        => Volatile.Write(ref _input, _input with { Document = document });
    public void SetViewport(double width, double height, double scale, double fontSize)
    {
        var input = _input;
        if (input.Width == width && input.Height == height && input.Scale == scale && input.FontSize == fontSize) return;
        Volatile.Write(ref _input, input with
        {
            Width = width, Height = height, Scale = scale, FontSize = fontSize,
            ResizeTimestamp = Stopwatch.GetTimestamp(),
        });
    }

    public void SetPaused(bool paused)
    {
        if (!paused) Volatile.Write(ref _forceResize, 1);
        Volatile.Write(ref _paused, paused ? 1 : 0);
        if (!paused) _resume.Set();
    }

    public void Stop()
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 0) _stop.Cancel();
    }

    private void Run()
    {
        try
        {
            _renderer.RebuildDeviceResources(_device);
            using var timer = new FramePacer(120, _stop.Token.WaitHandle);
            WaitHandle[] wakeHandles = [_stop.Token.WaitHandle, _resume];
            while (!_stop.IsCancellationRequested)
            {
                if (Volatile.Read(ref _paused) != 0)
                {
                    if (WaitHandle.WaitAny(wakeHandles) == 0) break;
                    timer.Reset();
                    ResetStatistics();
                    continue;
                }
                if (!timer.WaitForNextFrame()) break;
                if (Volatile.Read(ref _paused) != 0) continue;
                DrawFrame();
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception exception)
        {
            _logger.LogCritical(exception, "歌词后台渲染失败，已暂停绘制");
            if (Volatile.Read(ref _stopped) == 0) _failed();
        }
        finally
        {
            // 不能在 UI 线程等待这个循环或与 DrawFrame 并发释放原生资源。
            try
            {
                _renderer.Dispose();
                // Composition 对象的 Close 必须回到创建它们的 UI 线程。
                // 回调只在最后一帧结束后投递，UI 不等待这个线程。
                _releaseGraphics();
            }
            catch (Exception exception) { _logger.LogWarning(exception, "释放歌词绘制资源失败"); }
        }
    }

    private void DrawFrame()
    {
        var input = Volatile.Read(ref _input);
        if (input.Width <= 0 || input.Height <= 0) return;
        var resized = _width != input.Width || _height != input.Height || _scale != input.Scale || _fontSize != input.FontSize;
        // 合并表面重建，期间继续用已提交的尺寸绘制动画，合成器负责连续缩放。
        if (resized && _width > 0 && Volatile.Read(ref _forceResize) == 0
            && Stopwatch.GetElapsedTime(input.ResizeTimestamp) < ResizeDelay) resized = false;
        var drawStarted = Stopwatch.GetTimestamp();
        if (resized)
        {
            Interlocked.Exchange(ref _forceResize, 0);
            CanvasComposition.Resize(_surface, new Size(Math.Ceiling(input.Width * input.Scale),
                Math.Ceiling(input.Height * input.Scale)));
            _width = input.Width;
            _height = input.Height;
            _scale = input.Scale;
            _fontSize = input.FontSize;
            _renderer.SetDpi((float)(input.Scale * 96));
            _renderer.SetViewport(input.Width, input.Height);
        }
        _renderer.SetFontSize(_fontSize);
        _renderer.SetDocument(input.Document);
        while (_commands.TryDequeue(out var command)) command(_renderer);
        _renderer.Update(_clock.Position, _animationClock.Elapsed, _clock.JumpCount);
        using (var session = CanvasComposition.CreateDrawingSession(_surface,
            new Rect(0, 0, Math.Ceiling(_width * _scale), Math.Ceiling(_height * _scale)), (float)(_scale * 96)))
        {
            session.Clear(Windows.UI.Color.FromArgb(0, 0, 0, 0));
            _renderer.Draw(session);
        }
        if (_diagnostics) RecordFrame(drawStarted);
        if (_browsing == _renderer.IsBrowsing) return;
        _browsing = _renderer.IsBrowsing;
        _browsingChanged(_browsing);
    }

    private void RecordFrame(long drawStarted)
    {
        if (_statsStarted == 0) _statsStarted = drawStarted;
        var elapsed = Stopwatch.GetElapsedTime(drawStarted).TotalMilliseconds;
        _statsDrawMilliseconds += elapsed;
        _drawTimes.Add(elapsed);
        _statsMaxMilliseconds = Math.Max(_statsMaxMilliseconds, elapsed);
        if (_previousDraw != 0)
        {
            var interval = Stopwatch.GetElapsedTime(_previousDraw, drawStarted).TotalMilliseconds;
            _statsMaxFrameInterval = Math.Max(_statsMaxFrameInterval, interval);
            _frameIntervals.Add(interval);
        }
        _previousDraw = drawStarted;
        _statsFrames++;
        var window = Stopwatch.GetElapsedTime(_statsStarted).TotalSeconds;
        if (window < 5) return;
        _logger.LogInformation("歌词后台绘制统计：{Fps:F1} fps，平均绘制 {Average:F2} ms，最慢绘制 {Maximum:F2} ms，最大帧间隔 {Interval:F2} ms",
            _statsFrames / window, _statsDrawMilliseconds / _statsFrames, _statsMaxMilliseconds, _statsMaxFrameInterval);
        _frameIntervals.Sort();
        _drawTimes.Sort();
        _logger.LogInformation("歌词帧预算：绘制 P95 {DrawP95:F2} ms、P99 {DrawP99:F2} ms；帧间隔 P95 {IntervalP95:F2} ms、P99 {IntervalP99:F2} ms",
            Percentile(_drawTimes, 0.95), Percentile(_drawTimes, 0.99),
            Percentile(_frameIntervals, 0.95), Percentile(_frameIntervals, 0.99));
        ResetStatistics();
    }

    private static double Percentile(List<double> values, double percentile)
        => values.Count == 0 ? 0 : values[Math.Clamp((int)Math.Ceiling(values.Count * percentile) - 1, 0, values.Count - 1)];

    private void ResetStatistics()
    {
        _statsStarted = _previousDraw = 0;
        _statsFrames = 0;
        _statsDrawMilliseconds = _statsMaxMilliseconds = _statsMaxFrameInterval = 0;
        _frameIntervals.Clear();
        _drawTimes.Clear();
    }

    private sealed record RenderInput(LyricDocument Document, double Width, double Height,
        double Scale, double FontSize, long ResizeTimestamp);
}
