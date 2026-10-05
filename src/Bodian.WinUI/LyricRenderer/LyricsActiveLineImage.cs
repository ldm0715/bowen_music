using System.Numerics;
using Bodian.Core.Models.Lyrics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Text;
using Windows.Foundation;
using Windows.UI;

namespace Bodian.WinUI.LyricRenderer;

/// <summary>用同一整行排版缓存字形；逐字只更新颜色，长音发光也裁自同一字形遮罩。</summary>
internal sealed class LyricsActiveLineImage : IDisposable
{
    private readonly CanvasRenderTarget _mask, _colors, _result;
    private readonly AlphaMaskEffect _image;
    private readonly CanvasLinearGradientBrush[] _brushes;
    private readonly double[] _progress;
    private readonly List<(int Glyph, CanvasRenderTarget Image, GaussianBlurEffect Glow)> _glows = [];
    private readonly Color _played, _unplayed;
    private readonly double _featherRatio;
    private readonly Rect _bounds;
    private bool _painted;

    public LyricsActiveLineImage(CanvasDevice device, LyricsLineLayout line, LyricsRenderSettings settings,
        Color played, Color unplayed, float dpi, bool wordByWord)
    {
        Dpi = dpi;
        _played = played;
        _unplayed = unplayed;
        _featherRatio = settings.SweepFeatherRatio;
        var mask = line.GlyphMask ?? throw new InvalidOperationException("歌词行必须先建立共同字形遮罩");
        _bounds = mask.Bounds;
        var left = _bounds.Left;
        var top = _bounds.Top;
        _mask = mask.Mask;
        _colors = mask.CreateTarget();
        _result = mask.CreateTarget();
        _image = new AlphaMaskEffect { Source = _colors, AlphaMask = _mask };
        _brushes = new CanvasLinearGradientBrush[line.Glyphs.Length];
        _progress = new double[_brushes.Length];
        Array.Fill(_progress, double.NaN);
        for (var index = 0; index < _brushes.Length; index++)
            _brushes[index] = new CanvasLinearGradientBrush(device, played, unplayed);
        if (!wordByWord) return;
        // 发光来自完整行中相同的文字像素，不再单独排版/重画长音文字。
        using var glowColor = new CanvasRenderTarget(device, (float)_bounds.Width, (float)_bounds.Height, dpi);
        using (var session = glowColor.CreateDrawingSession()) session.Clear(played);
        var fullGlowImage = new AlphaMaskEffect { Source = glowColor, AlphaMask = _mask };
        try
        {
            foreach (var syllable in line.LongSyllables)
            {
                var glyphIndex = Array.FindIndex(line.Glyphs, glyph => glyph.Start == syllable.CharStart);
                var region = syllable.Bounds;
                var cropped = new CanvasRenderTarget(device, (float)Math.Max(1, region.Width),
                    (float)Math.Max(1, region.Height), dpi);
                using (var session = cropped.CreateDrawingSession())
                {
                    session.Clear(Color.FromArgb(0, 0, 0, 0));
                    session.DrawImage(fullGlowImage, (float)(left - region.X), (float)(top - region.Y));
                }
                var glow = new GaussianBlurEffect
                {
                    Source = cropped,
                    BlurAmount = (float)(settings.BaseFontSize * settings.LongSyllableGlowRatio),
                    BorderMode = EffectBorderMode.Soft,
                    CacheOutput = true,
                };
                _glows.Add((glyphIndex, cropped, glow));
            }
        }
        finally { fullGlowImage.Dispose(); }
    }

    public float Dpi { get; }

    public void Draw(CanvasDrawingSession session, LyricLine text, LyricsLineLayout line,
        TimeSpan position, bool wordByWord)
    {
        var changed = !_painted;
        for (var index = 0; index < line.Glyphs.Length; index++)
        {
            var glyph = line.Glyphs[index];
            var progress = wordByWord && glyph.Syllable >= 0 && glyph.Syllable < text.Syllables.Count
                ? LyricMotionMath.GlyphProgress(text.Syllables[glyph.Syllable].ProgressAt(position),
                    glyph.Offset, glyph.Bounds.Width, glyph.TotalWidth)
                : 1;
            if (_progress[index] == progress) continue;
            _progress[index] = progress;
            changed = true;
            var feather = Math.Max(1, glyph.Bounds.Width * _featherRatio);
            var edge = glyph.Bounds.X - feather / 2 + progress * (glyph.Bounds.Width + feather);
            _brushes[index].StartPoint = new Vector2((float)(edge - feather / 2), 0);
            _brushes[index].EndPoint = new Vector2((float)(edge + feather / 2), 0);
        }
        if (changed)
        {
            using (var paint = _colors.CreateDrawingSession())
            {
                paint.Clear(wordByWord ? _unplayed : _played);
                paint.Antialiasing = CanvasAntialiasing.Aliased;
                paint.Blend = CanvasBlend.Copy;
                paint.Transform = Matrix3x2.CreateTranslation((float)-_bounds.X, (float)-_bounds.Y);
                for (var index = 0; index < line.Glyphs.Length; index++)
                {
                    var region = line.Glyphs[index].Bounds;
                    if (region.Width > 0 && region.Height > 0)
                        paint.FillRectangle(region, _brushes[index]);
                }
            }
            // 高亮也先提交成与未唱层相同 DPI、相同原点的位图，再进行窗口坐标变换。
            using (var paint = _result.CreateDrawingSession())
            {
                paint.Clear(Color.FromArgb(0, 0, 0, 0));
                paint.DrawImage(_image);
            }
            _painted = true;
        }
        foreach (var (glyphIndex, _, glow) in _glows)
        {
            if (glyphIndex < 0) continue;
            var pulse = Math.Sin(Math.PI * _progress[glyphIndex]);
            var region = line.Glyphs[glyphIndex].Bounds;
            if (pulse > 0.01)
                using (session.CreateLayer((float)(pulse * 0.55)))
                    session.DrawImage(glow, (float)region.X, (float)region.Y);
        }
        session.DrawImage(_result, (float)_bounds.X, (float)_bounds.Y);
    }

    public void Dispose()
    {
        foreach (var (_, image, glow) in _glows)
        {
            glow.Dispose();
            image.Dispose();
        }
        foreach (var brush in _brushes) brush.Dispose();
        _image.Dispose();
        _colors.Dispose();
        _result.Dispose();
    }
}
