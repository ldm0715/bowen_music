using System.Numerics;
using Bodian.Core.Playback;
using Bodian.WinUI.Playback;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace Bodian.WinUI.Controls;

public sealed class AudioSpectrumView : UserControl
{
    private const int DisplayBarCount = 112;
    private readonly IPlaybackService _engine;
    private readonly AudioSpectrumSource _source;
    private readonly CompositionCanvasHost _canvas;
    private bool _loaded;
    private bool _paused;
    private volatile bool _playing;

    public AudioSpectrumView(IPlaybackService engine, AudioSpectrumSource source)
    {
        _engine = engine;
        _source = source;
        IsHitTestVisible = false;
        _canvas = new CompositionCanvasHost("频谱", (device, _) => new SpectrumRenderer(device, source, () => _playing));
        Content = _canvas;
        Loaded += (_, _) => { _loaded = true; _engine.StateChanged += OnStateChanged; UpdatePlayback(); };
        Unloaded += (_, _) => { _loaded = false; _engine.StateChanged -= OnStateChanged; _source.Stop(); };
    }

    public bool IsPaused
    {
        get => _paused;
        set { if (_paused == value) return; _paused = value; UpdatePlayback(); }
    }

    private void OnStateChanged(object? sender, PlaybackStateChangedEventArgs args) => UpdatePlayback();
    private void UpdatePlayback()
    {
        _playing = _engine.State == PlaybackState.Playing;
        if (_loaded && !_paused && _playing) _source.Start();
        else _source.Stop();
        _canvas.IsPaused = _paused;
        _canvas.Invalidate();
    }

    private sealed class SpectrumRenderer(CanvasDevice device, AudioSpectrumSource source, Func<bool> isPlaying) : ICompositionCanvasRenderer
    {
        private readonly float[] _bands = new float[AudioSpectrumAnalyzer.BandCount];
        private readonly float[] _display = new float[DisplayBarCount];
        private CanvasLinearGradientBrush? _gradient;
        private TimeSpan _lastDraw;
        private static CanvasLinearGradientBrush CreateGradient(CanvasDevice device)
            => new(device, new CanvasGradientStop[]
            {
                new() { Position = 0, Color = Color.FromArgb(220, 184, 219, 236) },
                new() { Position = 0.45f, Color = Color.FromArgb(255, 137, 183, 207) },
                new() { Position = 1, Color = Color.FromArgb(184, 95, 145, 174) },
            });

        public bool Draw(CanvasDrawingSession session, double viewportWidth, double viewportHeight, TimeSpan now)
        {
            _gradient ??= CreateGradient(device);
            source.CopyLevels(_bands);
            var playing = isPlaying();
            var elapsed = _lastDraw == default ? 1.0 / 120 : Math.Clamp((now - _lastDraw).TotalSeconds, 0, 0.25);
            _lastDraw = now;
            var width = (float)viewportWidth;
            var height = (float)viewportHeight;
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
                var target = playing ? Math.Min(1, Math.Pow(level, 0.72) * (1.12 - position * 0.28)) : 0;
                var timeConstant = target > _display[index] ? 0.055 : 0.16;
                _display[index] += (float)((target - _display[index]) * (1 - Math.Exp(-elapsed / timeConstant)));
                anyActive |= _display[index] > 0.002;
                var edgeDistance = Math.Abs(position - 0.5) * 2;
                var edgeOpacity = (float)Math.Clamp(Math.Min(position, 1 - position) / 0.015, 0, 1);
                var barHeight = (float)Math.Max(3, _display[index] * height * 0.88 * (1 - edgeDistance * edgeDistance * 0.16));
                var top = baseline - barHeight;
                _gradient.StartPoint = new Vector2(0, top);
                _gradient.EndPoint = new Vector2(0, baseline);
                _gradient.Opacity = (playing ? 0.36f + _display[index] * 0.42f : 0.2f) * edgeOpacity;
                session.FillRoundedRectangle(index * (barWidth + gap), top, barWidth, barHeight,
                    barWidth / 2, barWidth / 2, _gradient);
            }
            return playing || anyActive;
        }

        public void Dispose() => _gradient?.Dispose();
    }
}
