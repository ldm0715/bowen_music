using System.Numerics;
using Bodian.Core.Models.Lyrics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Text;
using Windows.Foundation;
using Windows.UI;

namespace Bodian.WinUI.LyricRenderer;

/// <summary>一行固定字形的遮罩和可更新的扫色层；播放进度不改变字形、字号或位置。</summary>
internal sealed class DesktopLyricsLineRenderer : IDisposable
{
    private static readonly Color White = Color.FromArgb(255, 255, 255, 255);
    private readonly LyricsLineLayout _rendered;
    private readonly CanvasRenderTarget _mask;
    private readonly CanvasRenderTarget _colors;
    private readonly AlphaMaskEffect _image;
    private readonly double _imageX, _imageY, _imageWidth, _imageHeight;

    public DesktopLyricsLineRenderer(CanvasDevice device, LyricLine line, double fontSize,
        double width, double scale, Color highlight)
    {
        Line = line;
        var text = string.IsNullOrEmpty(line.Text) ? " " : line.Text;
        using var format = new CanvasTextFormat
        {
            FontFamily = LyricsRenderSettings.Default.FontFamily,
            FontSize = (float)fontSize,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            WordWrapping = CanvasWordWrapping.NoWrap,
            HorizontalAlignment = CanvasHorizontalAlignment.Left,
            VerticalAlignment = CanvasVerticalAlignment.Top,
        };
        var layout = new CanvasTextLayout(device, text, format, 0, 0);
        // 长句在建排版时适配一次。不会随着唱到的位置横移或改变字号。
        var availableWidth = Math.Max(1, width - 4 / scale);
        if (layout.LayoutBounds.Width > availableWidth)
        {
            format.FontSize = (float)Math.Max(1, fontSize * availableWidth / layout.LayoutBounds.Width);
            layout.Dispose();
            layout = new CanvasTextLayout(device, text, format, 0, 0);
        }
        FontSize = format.FontSize;
        var mapping = new int[text.Length];
        Array.Fill(mapping, -1);
        var start = 0;
        for (var index = 0; index < line.Syllables.Count; index++)
        {
            var end = Math.Min(mapping.Length, start + line.Syllables[index].Text.Length);
            for (var character = start; character < end; character++) mapping[character] = index;
            start = end;
        }
        _rendered = new LyricsLineLayout(layout, new Rect[text.Length], mapping, []);
        using (var layoutEngine = new LyricsLayoutEngine(new LyricsRenderSettings
        {
            BaseFontSize = FontSize,
            LongSyllableThreshold = TimeSpan.Zero,
        })) layoutEngine.PrepareGlyphs(device, line, _rendered);

        var bounds = layout.LayoutBounds;
        var ink = layout.DrawBounds;
        _imageX = (Math.Floor(Math.Min(bounds.X, ink.X) * scale) - 2) / scale;
        _imageY = (Math.Floor(Math.Min(bounds.Y, ink.Y) * scale) - 2) / scale;
        _imageWidth = (Math.Ceiling(Math.Max(bounds.Right, ink.Right) * scale) + 2) / scale - _imageX;
        _imageHeight = (Math.Ceiling(Math.Max(bounds.Bottom, ink.Bottom) * scale) + 2) / scale - _imageY;
        _mask = new CanvasRenderTarget(device, (float)Math.Max(1, _imageWidth),
            (float)Math.Max(1, _imageHeight), (float)(96 * scale));
        _colors = new CanvasRenderTarget(device, (float)Math.Max(1, _imageWidth),
            (float)Math.Max(1, _imageHeight), (float)(96 * scale));
        using (var session = _mask.CreateDrawingSession())
        {
            session.Clear(Color.FromArgb(0, 0, 0, 0));
            session.TextAntialiasing = CanvasTextAntialiasing.Grayscale;
            session.DrawTextLayout(layout, (float)-_imageX, (float)-_imageY, White);
        }
        using (var session = _colors.CreateDrawingSession()) session.Clear(White);
        _image = new AlphaMaskEffect { Source = _colors, AlphaMask = _mask };
        highlight.A = 255;
        foreach (var glyph in _rendered.Glyphs)
            glyph.Brush = new CanvasLinearGradientBrush(device, highlight, White);
    }

    public LyricLine Line { get; }
    public double FontSize { get; }
    public Rect Bounds => _rendered.Layout.LayoutBounds;

    public bool UpdateHighlight(TimeSpan position, LyricKind kind, bool active)
    {
        var changed = false;
        foreach (var glyph in _rendered.Glyphs)
        {
            var progress = !active ? 0 : kind == LyricKind.WordByWord
                && glyph.Syllable >= 0 && glyph.Syllable < Line.Syllables.Count
                ? LyricMotionMath.GlyphProgress(Line.Syllables[glyph.Syllable].ProgressAt(position),
                    glyph.Offset, glyph.Bounds.Width, glyph.TotalWidth)
                : 1;
            if (progress == glyph.LastProgress) continue;
            changed = true;
            glyph.LastProgress = progress;
            var feather = Math.Max(1, glyph.Bounds.Width * 0.5);
            var edge = glyph.Bounds.X - feather / 2 + progress * (glyph.Bounds.Width + feather);
            glyph.Brush!.StartPoint = new Vector2((float)(edge - feather / 2), 0);
            glyph.Brush.EndPoint = new Vector2((float)(edge + feather / 2), 0);
        }
        if (!changed) return false;
        // 颜色纹理始终不透明，最终 alpha 完全取自只画过一次的字形遮罩。
        using var session = _colors.CreateDrawingSession();
        session.Clear(White);
        session.Antialiasing = CanvasAntialiasing.Aliased;
        session.Transform = Matrix3x2.CreateTranslation((float)-_imageX, (float)-_imageY);
        foreach (var glyph in _rendered.Glyphs)
        {
            if (glyph.LastProgress <= 0 || glyph.Bounds.Width <= 0) continue;
            session.FillRectangle(new Rect(glyph.Bounds.X, _imageY, glyph.Bounds.Width, _imageHeight), glyph.Brush!);
        }
        return true;
    }

    public void Draw(CanvasDrawingSession session, double viewportWidth, double y, double lineHeight,
        CanvasHorizontalAlignment alignment, double scale)
    {
        var bounds = Bounds;
        var horizontalOffset = alignment switch
        {
            CanvasHorizontalAlignment.Left => 0,
            CanvasHorizontalAlignment.Right => viewportWidth - bounds.Width,
            _ => (viewportWidth - bounds.Width) / 2,
        };
        var x = Math.Round((horizontalOffset - bounds.X) * scale) / scale;
        var top = Math.Round((y + (lineHeight - bounds.Height) / 2 - bounds.Y) * scale) / scale;
        using (session.CreateLayer(1, new Rect(0, y, viewportWidth, lineHeight)))
            session.DrawImage(_image, (float)(x + _imageX), (float)(top + _imageY));
    }

    public void Dispose()
    {
        _image.Dispose();
        _colors.Dispose();
        _mask.Dispose();
        _rendered.Dispose();
    }
}
