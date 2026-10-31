using System.Numerics;
using Bowen.Core.Models.Lyrics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Text;
using Windows.Foundation;
using Windows.UI;

namespace Bowen.WinUI.LyricRenderer;

/// <summary>同一整行排版缓存字形；逐字只更新颜色，长音另用自己的排版做发光、放大与上浮。</summary>
internal sealed class LyricsActiveLineImage : IDisposable
{
    private readonly CanvasRenderTarget _mask, _colors, _result;
    private readonly AlphaMaskEffect _image;
    private readonly CanvasLinearGradientBrush[] _brushes;
    private readonly double[] _progress;
    private readonly List<(int Glyph, CanvasRenderTarget Image, GaussianBlurEffect Glow, Rect Bounds,
        LyricsLongSyllable Syllable)> _glows = [];
    private readonly LyricsRenderSettings _settings;
    private readonly Color _played, _unplayed;
    private readonly double _featherRatio;
    private readonly Rect _bounds;
    private bool _painted;

    public LyricsActiveLineImage(CanvasDevice device, LyricsLineLayout line, LyricsRenderSettings settings,
        Color played, Color unplayed, float dpi, bool wordByWord)
    {
        Dpi = dpi;
        _settings = settings;
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
                // 长音字单独排版绘制，扫色画刷挂在这份排版上。
                syllable.Brush ??= new CanvasLinearGradientBrush(device, played, unplayed);
                syllable.Layout.SetBrush(0, syllable.CharCount, syllable.Brush);
                // Soft 边界的模糊输出会向四周扩出约 3 倍模糊半径；图层必须按这个范围开，
                // 只按字形矩形裁会把光晕切掉，只剩框内一团色块。
                _glows.Add((glyphIndex, cropped, glow,
                    glow.GetBounds(device, Matrix3x2.Identity), syllable));
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
                // 正在缩放/上浮的长音字由独立排版绘制，先从主纹理里挖掉，避免原位留下重影。
                // 整个过程都在 changed 里，挖孔集合与当前进度一一对应。
                foreach (var (glyphIndex, _, _, _, _) in _glows)
                {
                    if (glyphIndex < 0) continue;
                    if (!(Math.Sin(Math.PI * _progress[glyphIndex]) > 0.01)) continue;
                    var region = line.Glyphs[glyphIndex].Bounds;
                    paint.FillRectangle(region, Color.FromArgb(0, 0, 0, 0));
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
        foreach (var (glyphIndex, _, glow, glowBounds, _) in _glows)
        {
            if (glyphIndex < 0) continue;
            var pulse = Math.Sin(Math.PI * _progress[glyphIndex]);
            if (!(pulse > 0.01)) continue;
            var region = line.Glyphs[glyphIndex].Bounds;
            var previous = session.Transform;
            session.Transform = LiftTransform(region, pulse) * previous;
            // 图层收在模糊输出自己的范围内：既保住光晕，又不会像不带矩形的 CreateLayer
            // 那样每帧按整块表面开中间位图。
            var layerBounds = new Rect(region.X + glowBounds.X, region.Y + glowBounds.Y,
                Math.Max(1, glowBounds.Width), Math.Max(1, glowBounds.Height));
            using (session.CreateLayer((float)(pulse * 0.55), layerBounds))
                session.DrawImage(glow, (float)region.X, (float)region.Y);
            session.Transform = previous;
        }
        session.DrawImage(_result, (float)_bounds.X, (float)_bounds.Y);
        foreach (var (glyphIndex, _, _, _, syllable) in _glows)
        {
            if (glyphIndex < 0) continue;
            var progress = _progress[glyphIndex];
            var pulse = Math.Sin(Math.PI * progress);
            if (!(pulse > 0.01)) continue;
            DrawLiftedSyllable(session, syllable, progress, pulse);
        }
    }

    /// <summary>长音字的缩放与上浮按进度起伏，唱完落回原位。</summary>
    private Matrix3x2 LiftTransform(Rect bounds, double pulse)
    {
        var center = new Vector2((float)(bounds.X + bounds.Width / 2), (float)(bounds.Y + bounds.Height / 2));
        var scale = 1 + (_settings.LongSyllableScale - 1) * pulse;
        var lift = -_settings.BaseFontSize * _settings.FloatRatio * pulse;
        return Matrix3x2.CreateScale((float)scale, center)
            * Matrix3x2.CreateTranslation(0, (float)lift);
    }

    private void DrawLiftedSyllable(CanvasDrawingSession session, LyricsLongSyllable syllable,
        double progress, double pulse)
    {
        if (syllable.Brush is not { } brush) return;
        var bounds = syllable.Bounds;
        var feather = Math.Max(1, bounds.Width * _featherRatio);
        var edge = bounds.X - feather / 2 + progress * (bounds.Width + feather);
        brush.StartPoint = new Vector2((float)(edge - feather / 2), 0);
        brush.EndPoint = new Vector2((float)(edge + feather / 2), 0);
        var previous = session.Transform;
        session.Transform = LiftTransform(bounds, pulse) * previous;
        var origin = syllable.Layout.LayoutBounds;
        session.DrawTextLayout(syllable.Layout, (float)(bounds.X - origin.X), (float)(bounds.Y - origin.Y), _played);
        session.Transform = previous;
    }

    public void Dispose()
    {
        foreach (var (_, image, glow, _, _) in _glows)
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
