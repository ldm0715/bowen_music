using System.Diagnostics;
using Bodian.Core.Models;
using Bodian.Core.Models.Lyrics;
using Bodian.Core.Playback;
using Bodian.WinUI.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Composition;
using Microsoft.UI.Composition;
using Windows.Foundation;
using Windows.UI;

namespace Bodian.WinUI.LyricRenderer;

internal sealed record DesktopLyricsRenderInput(LyricDocument Document, string Placeholder,
    double Width, double Height, double Scale, double FontSize, Color Highlight,
    bool DualLine, DesktopLyricsAlignment Alignment, bool IsPlaying);

/// <summary>按歌词时间轴唤醒；逐字或长句滚动最高 60 Hz，静止时保留已提交表面。</summary>
internal sealed class DesktopLyricsRenderLoop(CanvasDevice device, CompositionDrawingSurface surface,
    LyricsPlaybackClock clock, ILogger logger, Action failed, Action releaseGraphics)
{
    private static readonly Color Clear = Color.FromArgb(0, 0, 0, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly object _lifetimeGate = new();
    private readonly AutoResetEvent _resume = new(false);
    private readonly bool _diagnostics = Environment.GetEnvironmentVariable("BODIAN_LYRICS_DIAGNOSTICS") == "1";
    private DesktopLyricsRenderInput _input = new(LyricDocument.Empty, AppIdentity.DisplayName, 0, 0, 1,
        DesktopLyricsSettings.DefaultFontSize, Color.FromArgb(255, 0, 229, 191), false, DesktopLyricsAlignment.Center, false);
    private DesktopLyricsRenderInput? _applied;
    private DesktopLyricsLineRenderer? _current, _next;
    private LyricLine? _placeholder;
    private int _paused, _stopped;
    private long _statsStarted;
    private TimeSpan? _drawnPosition;
    private long _inputRevision, _drawnRevision = -1, _resizeTimestamp;
    private static readonly TimeSpan ResizeDelay = TimeSpan.FromMilliseconds(90);
    private int _statsFrames;
    private double _statsMilliseconds, _statsMaximum;

    public void SetInput(DesktopLyricsRenderInput input)
    {
        var previous = Volatile.Read(ref _input);
        if (previous == input || Volatile.Read(ref _stopped) != 0) return;
        if (previous.Width != input.Width || previous.Height != input.Height || previous.Scale != input.Scale)
            Interlocked.Exchange(ref _resizeTimestamp, Stopwatch.GetTimestamp());
        Volatile.Write(ref _input, input);
        Invalidate();
    }
    public void Invalidate()
    {
        lock (_lifetimeGate)
        {
            if (_stopped != 0) return;
            Interlocked.Increment(ref _inputRevision);
            _resume.Set();
        }
    }
    public void NotifyPositionChanged()
    {
        lock (_lifetimeGate)
            if (_stopped == 0) _resume.Set();
    }
    public void Start() => new Thread(Run) { IsBackground = true, Name = "Bodian desktop lyrics renderer" }.Start();
    public void SetPaused(bool paused)
    {
        if (Volatile.Read(ref _stopped) != 0) return;
        Volatile.Write(ref _paused, paused ? 1 : 0);
        NotifyPositionChanged();
    }
    public void Stop()
    {
        lock (_lifetimeGate)
        {
            if (_stopped != 0) return;
            _stopped = 1;
            _stop.Cancel();
        }
    }

    private void Run()
    {
        try
        {
            using var timer = new FramePacer(60, _stop.Token.WaitHandle);
            WaitHandle[] wake = [_stop.Token.WaitHandle, _resume];
            while (!_stop.IsCancellationRequested)
            {
                if (Volatile.Read(ref _paused) != 0)
                {
                    if (WaitHandle.WaitAny(wake) == 0) break;
                    timer.Reset();
                    _statsStarted = 0;
                    _statsFrames = 0;
                    _statsMilliseconds = _statsMaximum = 0;
                    continue;
                }
                DrawFrame();
                var input = Volatile.Read(ref _input);
                var resizePending = input.Width > 0 && input.Height > 0 && _applied is not null
                    && (_applied.Width != input.Width || _applied.Height != input.Height || _applied.Scale != input.Scale);
                var delay = LyricRefreshSchedule.NextUpdateDelay(input.Document, clock.Position, input.IsPlaying,
                    animateHighlight: true, overflowing: _current?.Bounds.Width > input.Width);
                if (!resizePending && delay is { } interval && interval <= LyricRefreshSchedule.AnimationInterval)
                {
                    if (!timer.WaitForNextFrame()) break;
                    continue;
                }
                // 等待下一音节、切句或位置校正；暂停及空态不轮询。尺寸合并仍补最终帧。
                var timeout = resizePending ? 100 : delay.HasValue
                    ? (int)Math.Clamp(Math.Ceiling(delay.Value.TotalMilliseconds), 1, int.MaxValue) : Timeout.Infinite;
                if (WaitHandle.WaitAny(wake, timeout) == 0) break;
                timer.Reset();
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogError(exception, "桌面歌词后台渲染失败");
            if (Volatile.Read(ref _stopped) == 0) failed();
        }
        finally
        {
            try
            {
                _current?.Dispose();
                _next?.Dispose();
            }
            finally
            {
                try { releaseGraphics(); }
                finally
                {
                    lock (_lifetimeGate)
                    {
                        _stopped = 1;
                        _resume.Dispose();
                        _stop.Dispose();
                    }
                }
            }
        }
    }

    private void DrawFrame()
    {
        var input = Volatile.Read(ref _input);
        if (input.Width <= 0 || input.Height <= 0) return;
        var position = clock.Position;
        var revision = Interlocked.Read(ref _inputRevision);
        if (_applied == input && _drawnPosition == position && _drawnRevision == revision) return;
        // 拖动中保留已提交的完整双行表面，尺寸稳定后再重建，避免逐次 Resize 清空表面。
        var previous = _applied;
        if (previous is not null && (previous.Width != input.Width || previous.Height != input.Height || previous.Scale != input.Scale)
            && Stopwatch.GetElapsedTime(Interlocked.Read(ref _resizeTimestamp)) < ResizeDelay) return;
        var started = Stopwatch.GetTimestamp();
        ApplyInput(input);
        var index = input.Document.IndexOfLineAt(position);
        var first = Math.Max(0, index);
        var current = input.Document.IsEmpty ? Placeholder(input.Placeholder) : input.Document.Lines[first];
        var next = input.DualLine && first + 1 < input.Document.Lines.Count ? input.Document.Lines[first + 1] : null;
        var linesChanged = !ReferenceEquals(_current?.Line, current) || !ReferenceEquals(_next?.Line, next);
        UpdateLines(current, next, input);
        var changed = _current!.UpdateHighlight(position, input.Document.Kind, index >= 0);
        changed |= linesChanged;
        changed |= _current.UpdateScroll(position, input.Document.Kind, index >= 0, input.Width, input.Scale);
        if (_next is not null)
        {
            changed |= _next.UpdateHighlight(position, input.Document.Kind, active: false);
            changed |= _next.UpdateScroll(position, input.Document.Kind, active: false, input.Width, input.Scale);
        }
        // 同一画面只提交一次；暂停、空态和音节间隙不反复清空再画相同的文字。
        if (!changed && previous == input && _drawnRevision == revision)
        {
            _drawnPosition = position;
            return;
        }

        using (var session = CanvasComposition.CreateDrawingSession(surface,
            new Rect(0, 0, Math.Ceiling(input.Width * input.Scale), Math.Ceiling(input.Height * input.Scale)),
            (float)(input.Scale * 96)))
        {
            session.Clear(Clear);
            session.TextAntialiasing = CanvasTextAntialiasing.Grayscale;
            var metrics = DesktopLyricsLayoutMetrics.Calculate(input.FontSize, input.Height, input.DualLine);
            var lineHeight = metrics.LineHeight;
            var y = metrics.Top;
            var staggered = input.DualLine && input.Alignment == DesktopLyricsAlignment.Staggered;
            _current.Draw(session, input.Width, y, lineHeight,
                staggered ? CanvasHorizontalAlignment.Left : CanvasHorizontalAlignment.Center, input.Scale);
            if (_next is not null)
                _next.Draw(session, input.Width, y + lineHeight + metrics.Gap, lineHeight,
                    staggered ? CanvasHorizontalAlignment.Right : CanvasHorizontalAlignment.Center, input.Scale);
        }
        _drawnPosition = position;
        _drawnRevision = revision;
        if (_diagnostics) RecordFrame(started);
    }

    private void ApplyInput(DesktopLyricsRenderInput input)
    {
        var previous = _applied;
        if (previous is null || previous.Width != input.Width || previous.Height != input.Height || previous.Scale != input.Scale)
            CanvasComposition.Resize(surface, new Size(Math.Ceiling(input.Width * input.Scale), Math.Ceiling(input.Height * input.Scale)));
        var fontSize = DesktopLyricsLayoutMetrics.Calculate(input.FontSize, input.Height, input.DualLine).FontSize;
        var previousFontSize = previous is null ? 0
            : DesktopLyricsLayoutMetrics.Calculate(previous.FontSize, previous.Height, previous.DualLine).FontSize;
        if (previous is null || previousFontSize != fontSize || previous.Highlight != input.Highlight
            || previous.Scale != input.Scale || !ReferenceEquals(previous.Document, input.Document))
        {
            _current?.Dispose(); _current = null;
            _next?.Dispose(); _next = null;
        }
        _applied = input;
    }

    private LyricLine Placeholder(string text)
    {
        if (_placeholder?.Text != text) _placeholder = new LyricLine(TimeSpan.Zero, TimeSpan.Zero, text, []);
        return _placeholder;
    }

    private void UpdateLines(LyricLine current, LyricLine? next, DesktopLyricsRenderInput input)
    {
        var fontSize = DesktopLyricsLayoutMetrics.Calculate(input.FontSize, input.Height, input.DualLine).FontSize;
        if (!ReferenceEquals(_current?.Line, current))
        {
            _current?.Dispose();
            // 下一句的预排版在切句时直接接续，避免同一句重复测量。
            if (ReferenceEquals(_next?.Line, current)) { _current = _next; _next = null; }
            else _current = new DesktopLyricsLineRenderer(device, current, fontSize, input.Scale, input.Highlight);
        }
        if (!ReferenceEquals(_next?.Line, next))
        {
            _next?.Dispose();
            _next = next is null ? null : new DesktopLyricsLineRenderer(device, next, fontSize, input.Scale, input.Highlight);
        }
    }

    private void RecordFrame(long started)
    {
        if (_statsStarted == 0) _statsStarted = started;
        var milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        _statsMilliseconds += milliseconds;
        _statsMaximum = Math.Max(_statsMaximum, milliseconds);
        _statsFrames++;
        var seconds = Stopwatch.GetElapsedTime(_statsStarted).TotalSeconds;
        if (seconds < 5) return;
        logger.LogInformation("桌面歌词渲染：{Fps:F1} fps，平均绘制 {Average:F2} ms，最慢绘制 {Maximum:F2} ms",
            _statsFrames / seconds, _statsMilliseconds / _statsFrames, _statsMaximum);
        _statsStarted = 0; _statsFrames = 0; _statsMilliseconds = _statsMaximum = 0;
    }
}
