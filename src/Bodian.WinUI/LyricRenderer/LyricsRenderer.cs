using System.Numerics;
using Bodian.Core.Models.Lyrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using Windows.Foundation;
using Windows.UI;

namespace Bodian.WinUI.LyricRenderer;

/// <summary>全屏歌词：独立弹簧、连续字形渐变、长音浮动及像素级边缘遮罩。</summary>
internal sealed class LyricsRenderer(LyricsRenderSettings settings, ILogger<LyricsRenderer>? logger = null)
{
    private readonly ILogger<LyricsRenderer> _logger = logger ?? NullLogger<LyricsRenderer>.Instance;
    private readonly LyricsScrollAnimator _scroll = new();
    private LyricsDeviceResources? _device;
    private CanvasDevice? _creator;
    private LyricDocument _document = LyricDocument.Empty;
    private bool _layoutDirty = true;
    private bool _maskDirty = true;
    private double _width, _height;
    private float _dpi = 96;
    private double? _browseTarget;
    private TimeSpan _browseUntil;
    private double? _pointerY;
    private int _focusIndex = -1;
    private TimeSpan _position, _lastFrame;
    private long _lastJump = -1;
    private double[] _scales = [];
    private double[] _scaleVelocities = [];
    private Rect[] _hitRects = [];
    private CanvasLinearGradientBrush? _edgeBrush;
    private Color _played = Color.FromArgb(242, 255, 255, 255);
    private Color _unplayed = Color.FromArgb(80, 255, 255, 255);
    public int CurrentIndex { get; private set; } = -1;
    public bool IsBrowsing => _browseTarget.HasValue;

    public void SetDpi(float dpi)
    {
        if (Math.Abs(_dpi - dpi) < 0.1) return;
        _dpi = dpi;
        _maskDirty = true;
    }

    public void ScrollBy(double delta, TimeSpan now)
    {
        if (_device?.Snapshot is not { Count: > 0 } snapshot || _scroll.Count != snapshot.Count) return;
        var anchor = Math.Clamp(CurrentIndex, 0, snapshot.Count - 1);
        var minimum = snapshot.Lines[0].Height / 2 - _height * settings.PlayingLineTopOffsetFactor;
        var maximum = snapshot.Tops[^1] + snapshot.Lines[^1].Height / 2 - _height * settings.PlayingLineTopOffsetFactor;
        _browseTarget = Math.Clamp((_browseTarget ?? _scroll.OffsetAt(anchor)) + delta, minimum, Math.Max(minimum, maximum));
        _browseUntil = now + TimeSpan.FromSeconds(4);
        _scroll.Retarget(_browseTarget.Value, anchor, now, stagger: false);
    }

    public void ResumeFollowing() => _browseUntil = TimeSpan.Zero;

    public void SetPointerY(double? pointerY, TimeSpan now)
    {
        _pointerY = pointerY;
        if (IsBrowsing && pointerY.HasValue) _browseUntil = now + TimeSpan.FromSeconds(4);
    }

    public int LineIndexAt(double y)
    {
        var rects = Volatile.Read(ref _hitRects);
        for (var i = 0; i < rects.Length; i++)
            if (rects[i].Height > 0 && y >= rects[i].Top && y <= rects[i].Bottom) return i;
        return -1;
    }

    public void RebuildDeviceResources(CanvasDevice device)
    {
        Dispose();
        _creator = device;
        _device = new LyricsDeviceResources(device, settings);
        _layoutDirty = _maskDirty = true;
    }

    public void SetColors(Color played, Color unplayed)
    {
        if (_played == played && _unplayed == unplayed) return;
        _played = played;
        _unplayed = unplayed;
        _layoutDirty = true;
    }

    public void SetDocument(LyricDocument document)
    {
        if (ReferenceEquals(_document, document)) return;
        _document = document;
        _layoutDirty = true;
        _lastJump = -1;
    }

    public void SetFontSize(double fontSize)
    {
        fontSize = Math.Clamp(fontSize, 24, 80);
        if (Math.Abs(settings.BaseFontSize - fontSize) < 0.5) return;
        settings.BaseFontSize = fontSize;
        _layoutDirty = true;
    }

    public void SetViewport(double width, double height)
    {
        if (Math.Abs(width - _width) > 0.5) _layoutDirty = true;
        if (Math.Abs(width - _width) > 0.5 || Math.Abs(height - _height) > 0.5) _maskDirty = true;
        _width = width;
        _height = height;
    }

    public void Update(TimeSpan position, TimeSpan now, long jumpCount)
    {
        _position = LyricMotionMath.AdvanceHighlight(_position, position, _lastJump != jumpCount);
        var rebuilt = EnsureLayout();
        if (_device?.Snapshot is not { Count: > 0 } snapshot || _height <= 0) return;
        var current = Math.Max(0, snapshot.Document.IndexOfLineAt(_position));
        var target = snapshot.Tops[current] + snapshot.Lines[current].Height / 2 - _height * settings.PlayingLineTopOffsetFactor;
        var seconds = _lastFrame == default ? 1.0 / 60 : Math.Clamp((now - _lastFrame).TotalSeconds, 0, 1);
        _lastFrame = now;
        if (rebuilt || _lastJump != jumpCount || _scroll.Count != snapshot.Count)
        {
            _browseTarget = null;
            _scroll.Reset(snapshot.Count, target);
            _lastJump = jumpCount;
        }
        else if (_browseTarget.HasValue && now >= _browseUntil)
        {
            _browseTarget = null;
            _scroll.Retarget(target, current, now);
        }
        else if (!_browseTarget.HasValue && (current != CurrentIndex || Math.Abs(target - _scroll.Target) > 0.5))
        {
            _scroll.Retarget(target, current, now);
        }
        CurrentIndex = current;
        _scroll.Update(now, seconds);
        var centers = new double[snapshot.Count];
        for (var index = 0; index < snapshot.Count; index++)
            centers[index] = snapshot.Tops[index] + snapshot.Lines[index].Height / 2 - _scroll.OffsetAt(index);
        _focusIndex = IsBrowsing
            ? LyricMotionMath.NearestLine(centers, _height * settings.PlayingLineTopOffsetFactor)
            : current;
        var rects = new Rect[snapshot.Count];
        for (var i = 0; i < snapshot.Count; i++)
        {
            var active = i == _focusIndex && (IsBrowsing || _position >= snapshot.Document.Lines[i].Start);
            var scaleTarget = active ? settings.CurrentLineScale : settings.InactiveLineScale;
            (_scales[i], _scaleVelocities[i]) = LyricMotionMath.AdvanceSpring(
                _scales[i], _scaleVelocities[i], scaleTarget, seconds, 16, 1);
            var height = snapshot.Lines[i].Height;
            var top = snapshot.Tops[i] - _scroll.OffsetAt(i);
            rects[i] = new Rect(0, top + height * (1 - _scales[i]) / 2 - settings.LineGap / 2,
                _width, height * _scales[i] + settings.LineGap);
        }
        // 点击使用这一帧各行的实际位置，包含错峰弹簧的偏移。
        Volatile.Write(ref _hitRects, rects);
    }

    public void Draw(CanvasDrawingSession session)
    {
        if (_creator is null || _device?.Snapshot is not { Count: > 0 } snapshot || _height <= 0) return;
        EnsureMask();
        var hovered = _pointerY.HasValue ? LineIndexAt(_pointerY.Value) : -1;
        var previousTransform = session.Transform;
        using (session.CreateLayer(_edgeBrush!))
        {
            for (var index = 0; index < snapshot.Count; index++)
            {
                var line = snapshot.Lines[index];
                var top = snapshot.Tops[index] - _scroll.OffsetAt(index);
                if (top + line.Height < -settings.BaseFontSize || top > _height + settings.BaseFontSize) continue;
                EnsurePlainImage(line);
                var active = index == CurrentIndex && _position >= snapshot.Document.Lines[index].Start;
                var center = new Vector2(0, (float)(top + line.Height / 2));
                session.Transform = Matrix3x2.CreateTranslation(8, (float)top)
                    * Matrix3x2.CreateScale((float)_scales[index], center) * previousTransform;
                if ((IsBrowsing && index == _focusIndex) || (index == hovered && !active))
                {
                    session.DrawImage(line.FocusedImage!);
                }
                else if (active && !IsBrowsing)
                {
                    DrawActiveLine(session, snapshot, index);
                }
                else
                {
                    var distance = Math.Abs(index - _focusIndex);
                    line.Blur!.BlurAmount = (float)Math.Min(settings.FarBlurAmount, Math.Max(0, distance - 1) * 1.25);
                    session.DrawImage(line.Blur);
                }
            }
            session.Transform = previousTransform;
        }
    }

    private void EnsurePlainImage(LyricsLineLayout line)
    {
        if (line.PlainImage is not null) return;
        var color = WithOpacity(_played, settings.InactiveLineOpacity);
        line.Layout.SetColor(0, line.CharBounds.Length, color);
        line.PlainImage = new CanvasCommandList(_creator!);
        using (var buffer = line.PlainImage.CreateDrawingSession()) buffer.DrawTextLayout(line.Layout, 0, 0, color);
        line.Layout.SetColor(0, line.CharBounds.Length, _played);
        line.FocusedImage = new CanvasCommandList(_creator!);
        using (var buffer = line.FocusedImage.CreateDrawingSession()) buffer.DrawTextLayout(line.Layout, 0, 0, _played);
        line.Blur = new GaussianBlurEffect
        {
            Source = line.PlainImage,
            BorderMode = EffectBorderMode.Soft,
            CacheOutput = true,
        };
    }

    private void DrawActiveLine(CanvasDrawingSession session, LyricsLayoutSnapshot snapshot, int index)
    {
        var rendered = snapshot.Lines[index];
        var text = snapshot.Document.Lines[index];
        var wordByWord = snapshot.Document.Kind == LyricKind.WordByWord;
        if (!rendered.ActivePrepared)
        {
            foreach (var glyph in rendered.Glyphs)
            {
                glyph.Brush = new CanvasLinearGradientBrush(_creator!, _played, _unplayed);
                rendered.Layout.SetBrush(glyph.Start, glyph.Length, glyph.Brush);
            }
            if (wordByWord)
            {
                foreach (var syllable in rendered.LongSyllables)
                {
                    rendered.Layout.SetColor(syllable.CharStart, syllable.CharCount, Color.FromArgb(0, 255, 255, 255));
                    syllable.Brush = new CanvasLinearGradientBrush(_creator!, _played, _unplayed);
                    syllable.GlowImage = new CanvasCommandList(_creator!);
                    using (var glowSession = syllable.GlowImage.CreateDrawingSession())
                        glowSession.DrawTextLayout(syllable.Layout, 0, 0, _played);
                    syllable.Glow = new GaussianBlurEffect
                    {
                        Source = syllable.GlowImage,
                        BlurAmount = (float)(settings.BaseFontSize * settings.LongSyllableGlowRatio),
                        BorderMode = EffectBorderMode.Soft,
                        CacheOutput = true,
                    };
                    syllable.Layout.SetBrush(0, syllable.CharCount, syllable.Brush);
                }
            }
            rendered.ActivePrepared = true;
        }
        foreach (var glyph in rendered.Glyphs)
        {
            var progress = wordByWord && glyph.Syllable >= 0 && glyph.Syllable < text.Syllables.Count
                ? LyricMotionMath.GlyphProgress(text.Syllables[glyph.Syllable].ProgressAt(_position),
                    glyph.Offset, glyph.Bounds.Width, glyph.TotalWidth)
                : 1;
            if (progress == glyph.LastProgress) continue;
            glyph.LastProgress = progress;
            SetSweep(glyph.Brush!, glyph.Bounds.X, glyph.Bounds.Width, progress);
        }
        session.DrawTextLayout(rendered.Layout, 0, 0, _unplayed);
        if (!wordByWord) return;
        foreach (var syllable in rendered.LongSyllables)
        {
            var glyph = syllable.Glyph!;
            var progress = glyph.LastProgress;
            // 唱过的字逐渐落回原位；同一长音的字形依次起伏。
            var pulse = Math.Sin(Math.PI * progress);
            var bounds = syllable.Bounds;
            var origin = syllable.Layout.LayoutBounds;
            SetSweep(syllable.Brush!, bounds.X, bounds.Width, progress);
            var previous = session.Transform;
            var center = new Vector2((float)(bounds.X + bounds.Width / 2), (float)(bounds.Y + bounds.Height / 2));
            session.Transform = Matrix3x2.CreateScale((float)(1 + (settings.LongSyllableScale - 1) * pulse), center)
                * Matrix3x2.CreateTranslation(0, (float)(-settings.BaseFontSize * settings.FloatRatio * pulse)) * previous;
            var x = (float)(bounds.X - origin.X);
            var y = (float)(bounds.Y - origin.Y);
            if (pulse > 0.01)
            {
                using (session.CreateLayer((float)(pulse * 0.55))) session.DrawImage(syllable.Glow!, x, y);
            }
            session.DrawTextLayout(syllable.Layout, x, y, _played);
            session.Transform = previous;
        }
    }

    private void SetSweep(CanvasLinearGradientBrush brush, double x, double width, double progress)
    {
        var feather = Math.Max(1, width * settings.SweepFeatherRatio);
        // 羽化边缘从字形左侧之外走到右侧之外，0/1 时分别完整未唱/已唱。
        var edge = x - feather / 2 + progress * (width + feather);
        brush.StartPoint = new Vector2((float)(edge - feather / 2), 0);
        brush.EndPoint = new Vector2((float)(edge + feather / 2), 0);
    }

    private bool EnsureLayout()
    {
        if (!_layoutDirty || _creator is null || _device is null || _width <= 0) return false;
        _device.Snapshot?.Dispose();
        _device.Snapshot = _device.LayoutEngine.Build(_creator, _document, Math.Max(1, _width - 28));
        _scales = new double[_device.Snapshot.Count];
        Array.Fill(_scales, settings.InactiveLineScale);
        _scaleVelocities = new double[_scales.Length];
        _layoutDirty = false;
        _logger.LogInformation("全屏歌词排版：{Count} 行，字号 {FontSize:F1}，视口 {Width:F0}×{Height:F0}",
            _scales.Length, settings.BaseFontSize, _width, _height);
        return true;
    }

    private void EnsureMask()
    {
        if (!_maskDirty || _creator is null) return;
        _edgeBrush?.Dispose();
        _edgeBrush = new CanvasLinearGradientBrush(_creator, new CanvasGradientStop[]
        {
            new() { Position = 0, Color = Color.FromArgb(0, 255, 255, 255) },
            new() { Position = 0.08f, Color = Color.FromArgb(61, 255, 255, 255) },
            new() { Position = 0.12f, Color = Color.FromArgb(255, 255, 255, 255) },
            new() { Position = 0.88f, Color = Color.FromArgb(255, 255, 255, 255) },
            new() { Position = 0.92f, Color = Color.FromArgb(61, 255, 255, 255) },
            new() { Position = 1, Color = Color.FromArgb(0, 255, 255, 255) },
        }) { StartPoint = Vector2.Zero, EndPoint = new Vector2(0, (float)_height) };
        _maskDirty = false;
    }

    public void Dispose()
    {
        _edgeBrush?.Dispose();
        _edgeBrush = null;
        _device?.Dispose();
        _device = null;
        _creator = null;
        Volatile.Write(ref _hitRects, []);
        _lastFrame = default;
        _browseTarget = null;
        _pointerY = null;
        _focusIndex = -1;
        CurrentIndex = -1;
    }

    private static Color WithOpacity(Color color, double opacity)
        => Color.FromArgb((byte)Math.Clamp(color.A * opacity, 0, 255), color.R, color.G, color.B);
}
