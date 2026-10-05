using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Text;
using Windows.Foundation;
using Windows.UI;

namespace Bodian.WinUI.LyricRenderer;

/// <summary>同一行在未唱、悬停和逐字高亮时共用的固定字形像素。</summary>
internal sealed class LyricsLineGlyphMask : IDisposable
{
    public LyricsLineGlyphMask(CanvasDevice device, LyricsLineLayout line, float dpi)
    {
        Device = device;
        Dpi = dpi;
        var scale = dpi / 96.0;
        var ink = line.Layout.DrawBounds;
        var layout = line.Layout.LayoutBounds;
        var left = (Math.Floor(Math.Min(ink.Left, layout.Left) * scale) - 2) / scale;
        var top = (Math.Floor(Math.Min(ink.Top, layout.Top) * scale) - 2) / scale;
        var right = (Math.Ceiling(Math.Max(ink.Right, layout.Right) * scale) + 2) / scale;
        var bottom = (Math.Ceiling(Math.Max(ink.Bottom, layout.Bottom) * scale) + 2) / scale;
        Bounds = new Rect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
        Mask = CreateTarget();
        var white = Color.FromArgb(255, 255, 255, 255);
        line.Layout.SetColor(0, line.CharBounds.Length, white);
        using var session = Mask.CreateDrawingSession();
        session.Clear(Color.FromArgb(0, 0, 0, 0));
        session.TextAntialiasing = CanvasTextAntialiasing.Grayscale;
        session.DrawTextLayout(line.Layout, (float)-left, (float)-top, white);
    }

    public CanvasDevice Device { get; }
    public float Dpi { get; }
    public Rect Bounds { get; }
    public CanvasRenderTarget Mask { get; }

    public CanvasRenderTarget CreateTarget()
        => new(Device, (float)Bounds.Width, (float)Bounds.Height, Dpi);

    public CanvasRenderTarget Paint(Color color)
    {
        using var colors = CreateTarget();
        using (var paint = colors.CreateDrawingSession()) paint.Clear(color);
        using var image = new AlphaMaskEffect { Source = colors, AlphaMask = Mask };
        var result = CreateTarget();
        using (var session = result.CreateDrawingSession())
        {
            session.Clear(Color.FromArgb(0, 0, 0, 0));
            session.DrawImage(image);
        }
        return result;
    }

    public void Dispose() => Mask.Dispose();
}
