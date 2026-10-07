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

/// <summary>全屏歌词：稳定焦点、独立错峰滚动、连续字形渐变和长音发光。</summary>
internal sealed class LyricsRenderer(LyricsRenderSettings settings, ILogger<LyricsRenderer>? logger = null)
{
    private readonly ILogger<LyricsRenderer> _logger = logger ?? NullLogger<LyricsRenderer>.Instance;
    private readonly LyricsScrollAnimator _scroll = new();
    private LyricsDeviceResources? _device;
    private CanvasDevice? _creator;
    private LyricDocument _document = LyricDocument.Empty;
    private bool _layoutDirty = true;
    private readonly LinkedList<CachedLayout> _layouts = new();
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
    private readonly object _hitRectGate = new();
    private Rect[] _hitRects = [];
    private Rect[] _nextHitRects = [];
    private bool _scaleAnimating;
    private double[] _centers = [];
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
        lock (_hitRectGate)
        {
            for (var i = 0; i < _hitRects.Length; i++)
                if (_hitRects[i].Height > 0 && y >= _hitRects[i].Top && y <= _hitRects[i].Bottom) return i;
            return -1;
        }
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

    public bool IsMotionAnimating => _scroll.IsAnimating || _scaleAnimating;

    public bool NeedsContinuousFrames(bool playing)
    {
        if (IsMotionAnimating) return true;
        if (!playing || IsBrowsing || _document.Kind != LyricKind.WordByWord || CurrentIndex < 0 || CurrentIndex >= _document.Lines.Count) return false;
        foreach (var syllable in _document.Lines[CurrentIndex].Syllables)
            if (syllable.Duration > TimeSpan.Zero && _position >= syllable.Start && _position < syllable.End) return true;
        return false;
    }

    public bool Update(TimeSpan position, TimeSpan now, long jumpCount)
    {
        var previousPosition = _position;
        var previousIndex = CurrentIndex;
        var wasBrowsing = IsBrowsing;
        var changed = _layoutDirty || _maskDirty;
        _position = LyricMotionMath.AdvanceHighlight(_position, position, _lastJump != jumpCount);
        var rebuilt = EnsureLayout();
        if (_device?.Snapshot is not { Count: > 0 } snapshot || _height <= 0)
        {
            CurrentIndex = -1;
            _scaleAnimating = false;
            if (_scroll.Count > 0) _scroll.Reset(0, 0);
            EnsureMask();
            return changed;
        }
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
        var centers = _centers;
        for (var index = 0; index < snapshot.Count; index++)
            centers[index] = snapshot.Tops[index] + snapshot.Lines[index].Height / 2 - _scroll.OffsetAt(index);
        _focusIndex = IsBrowsing
            ? LyricMotionMath.NearestLine(centers, _height * settings.PlayingLineTopOffsetFactor)
            : current;
        var rects = _nextHitRects;
        _scaleAnimating = false;
        for (var i = 0; i < snapshot.Count; i++)
        {
            var active = i == _focusIndex && (IsBrowsing || _position >= snapshot.Document.Lines[i].Start);
            var scaleTarget = active ? settings.CurrentLineScale : settings.InactiveLineScale;
            var previousScale = _scales[i];
            (_scales[i], _scaleVelocities[i]) = LyricMotionMath.AdvanceSpring(
                _scales[i], _scaleVelocities[i], scaleTarget, seconds, 16, 1);
            if (Math.Abs(_scales[i] - scaleTarget) < 0.0001 && Math.Abs(_scaleVelocities[i]) < 0.0001)
                (_scales[i], _scaleVelocities[i]) = (scaleTarget, 0);
            else _scaleAnimating = true;
            changed |= previousScale != _scales[i];
            var height = snapshot.Lines[i].Height;
            var top = snapshot.Tops[i] - _scroll.OffsetAt(i);
            rects[i] = new Rect(0, top + height * (1 - _scales[i]) / 2 - settings.LineGap / 2,
                _width, height * _scales[i] + settings.LineGap);
        }
        // 点击使用这一帧各行的实际位置，包含错峰弹簧的偏移。
        lock (_hitRectGate) Array.Copy(rects, _hitRects, rects.Length);
        // 包含音节结束的最后一帧；跨过短音节或停在间隙也要提交正确的颜色。
        if (!IsBrowsing && _document.Kind == LyricKind.WordByWord && previousPosition != _position)
            foreach (var syllable in _document.Lines[current].Syllables)
                if (syllable.ProgressAt(previousPosition) != syllable.ProgressAt(_position)) { changed = true; break; }
        return changed || rebuilt || _scroll.HasMoved || current != previousIndex || wasBrowsing != IsBrowsing;
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
                // 只保留视口附近的纹理；已经播放过的行不会累积整首歌的 GPU 位图和长音缓存。
                if (top + line.Height < -settings.BaseFontSize || top > _height + settings.BaseFontSize)
                {
                    var margin = settings.BaseFontSize * settings.ViewportMarginLines;
                    if (top + line.Height < -margin || top > _height + margin) line.ReleaseDrawingResources();
                    else line.ReleaseActiveResources();
                    continue;
                }
                if (index != CurrentIndex || IsBrowsing) line.ReleaseActiveResources();
                EnsurePlainImage(line);
                var active = index == CurrentIndex && _position >= snapshot.Document.Lines[index].Start;
                var center = new Vector2(0, (float)(top + line.Height / 2));
                session.Transform = Matrix3x2.CreateTranslation(8, (float)top)
                    * Matrix3x2.CreateScale((float)_scales[index], center) * previousTransform;
                if ((IsBrowsing && index == _focusIndex) || (index == hovered && !active))
                {
                    line.FocusedImage ??= line.GlyphMask!.Paint(_played);
                    session.DrawImage(line.FocusedImage, (float)line.GlyphMask!.Bounds.X, (float)line.GlyphMask.Bounds.Y);
                }
                else if (active && !IsBrowsing)
                {
                    DrawActiveLine(session, snapshot, index);
                }
                else
                {
                    var distance = Math.Abs(index - _focusIndex);
                    var blurAmount = (float)Math.Min(settings.FarBlurAmount, Math.Max(0, distance - 1) * 1.25);
                    // 同值也写效果属性会使缓存失效；只有焦点距离变化才重算模糊。
                    if (line.BlurAmount != blurAmount) line.Blur!.BlurAmount = line.BlurAmount = blurAmount;
                    if (blurAmount < 0.01)
                        session.DrawImage(line.PlainImage!, (float)line.GlyphMask!.Bounds.X, (float)line.GlyphMask.Bounds.Y);
                    else session.DrawImage(line.Blur, (float)line.GlyphMask!.Bounds.X, (float)line.GlyphMask.Bounds.Y);
                }
            }
            session.Transform = previousTransform;
        }
    }

    private void EnsurePlainImage(LyricsLineLayout line)
    {
        if (line.PlainImage is not null && line.GlyphMask?.Dpi == _dpi) return;
        line.ReleaseDrawingResources();
        line.GlyphMask = new LyricsLineGlyphMask(_creator!, line, _dpi);
        line.PlainImage = line.GlyphMask.Paint(WithOpacity(_played, settings.InactiveLineOpacity));
        line.BlurAmount = 0;
        line.Blur = new GaussianBlurEffect
        {
            BlurAmount = 0,
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
            if (wordByWord) _device!.LayoutEngine.PrepareGlyphs(_creator!, text, rendered);
            rendered.ActivePrepared = true;
        }
        if (rendered.ActiveImage is null || rendered.ActiveImage.Dpi != _dpi)
        {
            rendered.ActiveImage?.Dispose();
            rendered.ActiveImage = new LyricsActiveLineImage(_creator!, rendered, settings,
                _played, _unplayed, _dpi, wordByWord);
        }
        rendered.ActiveImage.Draw(session, text, rendered, _position, wordByWord);
    }

    private bool EnsureLayout()
    {
        if (!_layoutDirty || _creator is null || _device is null || _width <= 0) return false;
        var key = new LayoutKey(_document, Math.Max(1, _width - 28), settings.BaseFontSize, _played, _unplayed);
        if (_device.Snapshot is { } previous)
            foreach (var line in previous.Lines) line.ReleaseDrawingResources();
        _device.Snapshot = null;
        for (var node = _layouts.First; node is not null;)
        {
            var next = node.Next;
            if (!ReferenceEquals(node.Value.Key.Document, _document))
            {
                _layouts.Remove(node);
                node.Value.Snapshot.Dispose();
            }
            node = next;
        }
        var cached = _layouts.First;
        while (cached is not null && cached.Value.Key != key) cached = cached.Next;
        if (cached is not null)
        {
            _layouts.Remove(cached);
            _layouts.AddFirst(cached);
            _device.Snapshot = cached.Value.Snapshot;
        }
        else
        {
            var snapshot = _device.LayoutEngine.Build(_creator, _document, key.Width);
            _layouts.AddFirst(new CachedLayout(key, snapshot));
            _device.Snapshot = snapshot;
            // 保留普通窗口、全屏和一个最近尺寸，反复切换无需重新排版整首歌。
            if (_layouts.Count > 3 && _layouts.Last is { } oldest)
            {
                _layouts.RemoveLast();
                oldest.Value.Snapshot.Dispose();
            }
        }
        _scales = new double[_device.Snapshot.Count];
        Array.Fill(_scales, settings.InactiveLineScale);
        _scaleVelocities = new double[_scales.Length];
        _centers = new double[_scales.Length];
        _nextHitRects = new Rect[_scales.Length];
        lock (_hitRectGate) _hitRects = new Rect[_scales.Length];
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
        if (_device is not null) _device.Snapshot = null;
        foreach (var cached in _layouts) cached.Snapshot.Dispose();
        _layouts.Clear();
        _device?.Dispose();
        _device = null;
        _creator = null;
        lock (_hitRectGate) _hitRects = [];
        _nextHitRects = [];
        _lastFrame = default;
        _browseTarget = null;
        _pointerY = null;
        _focusIndex = -1;
        CurrentIndex = -1;
    }

    private sealed record LayoutKey(LyricDocument Document, double Width, double FontSize, Color Played, Color Unplayed);
    private sealed record CachedLayout(LayoutKey Key, LyricsLayoutSnapshot Snapshot);

    private static Color WithOpacity(Color color, double opacity)
        => Color.FromArgb((byte)Math.Clamp(color.A * opacity, 0, 255), color.R, color.G, color.B);
}
