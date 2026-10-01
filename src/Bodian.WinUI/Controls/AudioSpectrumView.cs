using System.Diagnostics;
using System.Numerics;
using Bodian.Core.Playback;
using Bodian.WinUI.Playback;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace Bodian.WinUI.Controls;

public sealed class AudioSpectrumView : UserControl
{
    private const int DisplayBarCount = 112;
    private readonly IPlaybackService _engine;
    private readonly AudioSpectrumSource _source;
    private readonly CanvasControl _canvas;
    private readonly DispatcherQueueTimer _timer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly float[] _bands = new float[AudioSpectrumAnalyzer.BandCount];
    private readonly float[] _display = new float[DisplayBarCount];
    private CanvasLinearGradientBrush? _gradient;
    private bool _loaded;
    private bool _paused;
    private bool _playing;
    private TimeSpan _lastDraw;

    public AudioSpectrumView(IPlaybackService engine, AudioSpectrumSource source)
    {
        _engine = engine;
        _source = source;
        IsHitTestVisible = false;
        _canvas = new CanvasControl { ClearColor = Color.FromArgb(0, 0, 0, 0) };
        Content = _canvas;
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(1000.0 / 30);
        _timer.Tick += (_, _) => _canvas.Invalidate();
        _canvas.CreateResources += OnCreateResources;
        _canvas.Draw += OnDraw;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public bool IsPaused
    {
        get => _paused;
        set { _paused = value; UpdatePlayback(); }
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        _loaded = true;
        _engine.StateChanged += OnStateChanged;
        UpdatePlayback();
        _canvas.Invalidate();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        _loaded = false;
        _engine.StateChanged -= OnStateChanged;
        _timer.Stop();
        _source.Stop();
        _gradient?.Dispose();
        _gradient = null;
        Array.Clear(_bands);
        Array.Clear(_display);
    }

    private void OnStateChanged(object? sender, PlaybackStateChangedEventArgs args) => UpdatePlayback();

    private void UpdatePlayback()
    {
        _playing = _engine.State == PlaybackState.Playing;
        if (_loaded && !_paused && _playing) _source.Start();
        else _source.Stop();
        if (_loaded && !_paused) _timer.Start();
        else _timer.Stop();
    }

    private void OnCreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args)
    {
        _gradient?.Dispose();
        _gradient = CreateGradient(sender.Device);
    }

    private static CanvasLinearGradientBrush CreateGradient(CanvasDevice device)
        => new(device, new CanvasGradientStop[]
        {
            new() { Position = 0, Color = Color.FromArgb(220, 184, 219, 236) },
            new() { Position = 0.45f, Color = Color.FromArgb(255, 137, 183, 207) },
            new() { Position = 1, Color = Color.FromArgb(184, 95, 145, 174) },
        });

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (!_loaded || sender.ActualWidth <= 0 || sender.ActualHeight <= 0) return;
        _gradient ??= CreateGradient(sender.Device);
        _source.CopyLevels(_bands);
        var now = _clock.Elapsed;
        var elapsed = _lastDraw == default ? 1.0 / 30 : Math.Clamp((now - _lastDraw).TotalSeconds, 0, 0.25);
        _lastDraw = now;
        var width = (float)sender.ActualWidth;
        var height = (float)sender.ActualHeight;
        var baseline = height - 2;
        var barWidth = Math.Clamp(width / (DisplayBarCount * 3.5f), 1.2f, 2.4f);
        var gap = Math.Max(0, (width - barWidth * DisplayBarCount) / (DisplayBarCount - 1));
        var anyActive = false;
        for (var index = 0; index < DisplayBarCount; index++)
        {
            var position = index / (float)(DisplayBarCount - 1);
            var sourcePosition = position * (AudioSpectrumAnalyzer.BandCount - 1);
            var left = (int)sourcePosition;
            var right = Math.Min(left + 1, AudioSpectrumAnalyzer.BandCount - 1);
            var level = _bands[left] + (_bands[right] - _bands[left]) * (sourcePosition - left);
            var target = _playing ? Math.Min(1, Math.Pow(level, 0.72) * (1.12 - position * 0.28)) : 0;
            var timeConstant = target > _display[index] ? 0.055 : 0.16;
            _display[index] += (float)((target - _display[index]) * (1 - Math.Exp(-elapsed / timeConstant)));
            anyActive |= _display[index] > 0.002;
            var edgeDistance = Math.Abs(position - 0.5) * 2;
            var edgeOpacity = (float)Math.Clamp(Math.Min(position, 1 - position) / 0.015, 0, 1);
            var barHeight = (float)Math.Max(3, _display[index] * height * 0.88 * (1 - edgeDistance * edgeDistance * 0.16));
            var top = baseline - barHeight;
            _gradient.StartPoint = new Vector2(0, top);
            _gradient.EndPoint = new Vector2(0, baseline);
            _gradient.Opacity = (_playing ? 0.36f + _display[index] * 0.42f : 0.2f) * edgeOpacity;
            args.DrawingSession.FillRoundedRectangle(index * (barWidth + gap), top, barWidth, barHeight,
                barWidth / 2, barWidth / 2, _gradient);
        }
        if (!_playing && !anyActive) _timer.Stop();
    }
}
