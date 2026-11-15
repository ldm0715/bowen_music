using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.UI;

namespace Bowen.WinUI.Services;

/// <summary>两张月报海报共用的绘制小工具。</summary>
internal static class MonthPosterDrawing
{
    /// <summary>海报中文使用的字体。渲染前由 <see cref="MonthPosterContent.UiFontFamily"/> 写入。</summary>
    public static string ChineseFont { get; set; } = "Microsoft YaHei UI";

    public const string DisplayFont = "Segoe UI";
    public const string MonoFont = "Consolas";

    public static CanvasTextFormat Text(
        float size,
        string? family = null,
        bool bold = false,
        CanvasHorizontalAlignment alignment = CanvasHorizontalAlignment.Left,
        CanvasWordWrapping wrapping = CanvasWordWrapping.NoWrap)
    {
        var format = new CanvasTextFormat
        {
            FontFamily = family ?? ChineseFont,
            FontSize = size,
            HorizontalAlignment = alignment,
            VerticalAlignment = CanvasVerticalAlignment.Top,
            WordWrapping = wrapping,
            TrimmingGranularity = CanvasTextTrimmingGranularity.Character,
            TrimmingSign = CanvasTrimmingSign.Ellipsis,
            LineSpacingMode = CanvasLineSpacingMode.Uniform,
            LineSpacing = size * 1.22f,
        };

        if (bold)
        {
            format.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
        }

        return format;
    }

    public static void Draw(
        CanvasDrawingSession session,
        CanvasDevice device,
        string text,
        float x,
        float y,
        float width,
        CanvasTextFormat format,
        Color color)
    {
        using var layout = new CanvasTextLayout(device, text, format, width, 0);
        session.DrawTextLayout(layout, x, y, color);
    }

    public static void DrawRight(
        CanvasDrawingSession session,
        CanvasDevice device,
        string text,
        float right,
        float y,
        float width,
        CanvasTextFormat format,
        Color color)
    {
        format.HorizontalAlignment = CanvasHorizontalAlignment.Right;
        using var layout = new CanvasTextLayout(device, text, format, width, 0);
        session.DrawTextLayout(layout, right - width, y, color);
    }

    public static void DrawCenter(
        CanvasDrawingSession session,
        CanvasDevice device,
        string text,
        float centerX,
        float y,
        float width,
        CanvasTextFormat format,
        Color color)
    {
        format.HorizontalAlignment = CanvasHorizontalAlignment.Center;
        using var layout = new CanvasTextLayout(device, text, format, width, 0);
        session.DrawTextLayout(layout, centerX - (width / 2), y, color);
    }

    /// <summary>把图片按「居中裁切」画进指定矩形，圆角裁切。</summary>
    public static void DrawCover(
        CanvasDrawingSession session,
        CanvasDevice device,
        CanvasBitmap? bitmap,
        Rect rect,
        float radius,
        Color placeholder)
    {
        using var clip = radius > 0
            ? CanvasGeometry.CreateRoundedRectangle(device, rect, radius, radius)
            : CanvasGeometry.CreateRectangle(device, rect);
        using var layer = session.CreateLayer(1, clip);

        if (bitmap is null)
        {
            using var brush = new CanvasSolidColorBrush(device, placeholder);
            session.FillRectangle(rect, brush);
            return;
        }

        var source = bitmap.Size;
        var scale = Math.Max(rect.Width / source.Width, rect.Height / source.Height);
        var sourceWidth = rect.Width / scale;
        var sourceHeight = rect.Height / scale;
        var sourceRect = new Rect(
            (source.Width - sourceWidth) / 2,
            (source.Height - sourceHeight) / 2,
            sourceWidth,
            sourceHeight);

        session.DrawImage(bitmap, rect, sourceRect, 1, CanvasImageInterpolation.HighQualityCubic);
    }

    public static void DrawCircleImage(
        CanvasDrawingSession session,
        CanvasDevice device,
        CanvasBitmap? bitmap,
        float centerX,
        float centerY,
        float radius,
        Color placeholder,
        string? fallbackInitial)
    {
        using var clip = CanvasGeometry.CreateCircle(
            device, new System.Numerics.Vector2(centerX, centerY), radius);
        using var layer = session.CreateLayer(1, clip);

        if (bitmap is not null)
        {
            var rect = new Rect(centerX - radius, centerY - radius, radius * 2, radius * 2);
            var source = bitmap.Size;
            var scale = Math.Max(rect.Width / source.Width, rect.Height / source.Height);
            var sourceWidth = rect.Width / scale;
            var sourceHeight = rect.Height / scale;
            var sourceRect = new Rect(
                (source.Width - sourceWidth) / 2,
                (source.Height - sourceHeight) / 2,
                sourceWidth,
                sourceHeight);
            session.DrawImage(bitmap, rect, sourceRect, 1, CanvasImageInterpolation.HighQualityCubic);
            return;
        }

        using var brush = new CanvasSolidColorBrush(device, placeholder);
        session.FillCircle(centerX, centerY, radius, brush);

        if (string.IsNullOrWhiteSpace(fallbackInitial))
        {
            return;
        }

        using var format = Text(radius * 0.95f, ChineseFont, bold: true, CanvasHorizontalAlignment.Center);
        DrawCenter(
            session, device, fallbackInitial, centerX, centerY - (radius * 0.62f), radius * 2,
            format, Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
    }

    /// <summary>
    /// 从 exe 目录的 Assets 读取应用图标（深色版）。
    /// </summary>
    /// <remarks>
    /// 不用 <c>ms-appx</c>：本项目是未打包 WinUI，Win2D 的 URI 加载在未打包环境下不可靠。
    /// Asset 已通过 <c>CopyToOutputDirectory</c> 落在 exe 旁边。
    /// </remarks>
    public static async Task<CanvasBitmap?> TryLoadBrandIconAsync(CanvasDevice device)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Ripple.png");

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            using var stream = new InMemoryRandomAccessStream();

            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }

            stream.Seek(0);
            return await CanvasBitmap.LoadAsync(device, stream);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static Color WithAlpha(Color color, byte alpha)
        => Color.FromArgb(alpha, color.R, color.G, color.B);

    /// <summary>两张海报共用的页脚：应用图标 + 品牌 + 标语 + 日期。</summary>
    public static void DrawBrandFooter(
        CanvasDrawingSession session,
        CanvasDevice device,
        CanvasBitmap? icon,
        float width,
        float height,
        int year,
        int month)
    {
        var white = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
        var muted = Color.FromArgb(0xFF, 0x94, 0xA3, 0xB8);
        var brandY = height - 136;
        var taglineY = height - 96;

        using var brandFormat = Text(26, ChineseFont);
        using var brandLayout = new CanvasTextLayout(device, "波纹音乐", brandFormat, 260, 0);
        using var taglineFormat = Text(18, ChineseFont);
        using var taglineLayout = new CanvasTextLayout(device, "用音乐记录时光的波纹", taglineFormat, 320, 0);

        // 图标上下边界直接取两行文字的可见墨迹，而不是布局框。
        var brandInk = brandLayout.DrawBounds;
        var taglineInk = taglineLayout.DrawBounds;
        var visualTop = brandY + (float)brandInk.Y;
        var visualBottom = taglineY + (float)(taglineInk.Y + taglineInk.Height);
        var iconSize = Math.Max(44f, visualBottom - visualTop);

        if (icon is not null)
        {
            DrawCover(
                session, device, icon,
                new Rect(80, visualTop, iconSize, iconSize), 14,
                Color.FromArgb(0, 0, 0, 0));
        }

        session.DrawTextLayout(brandLayout, 160, brandY, white);
        session.DrawTextLayout(taglineLayout, 160, taglineY, muted);

        using var dateFormat = Text(
            18, DisplayFont, alignment: CanvasHorizontalAlignment.Right);
        DrawRight(
            session, device, $"{year}.{month:00}",
            width - 80, height - 130, 260, dateFormat, muted);
    }

    public static CanvasLinearGradientBrush VerticalGradient(
        CanvasDevice device,
        float height,
        Color top,
        Color bottom) => new(device, top, bottom)
    {
        StartPoint = new System.Numerics.Vector2(0, 0),
        EndPoint = new System.Numerics.Vector2(0, height),
    };

    /// <summary>画一条正弦波。用于背景的「波纹」。</summary>
    public static void DrawWave(
        CanvasDrawingSession session,
        CanvasDevice device,
        float width,
        float centerY,
        float amplitude,
        float wavelength,
        float phase,
        Color color,
        float strokeWidth)
    {
        using var path = new CanvasPathBuilder(device);
        path.BeginFigure(new System.Numerics.Vector2(0, centerY + ((float)Math.Sin(phase) * amplitude)));

        for (var x = 8f; x <= width; x += 8f)
        {
            var y = centerY + ((float)Math.Sin((x / wavelength) + phase) * amplitude);
            path.AddLine(new System.Numerics.Vector2(x, y));
        }

        path.EndFigure(CanvasFigureLoop.Open);
        using var geometry = CanvasGeometry.CreatePath(path);
        session.DrawGeometry(geometry, color, strokeWidth);
    }

    /// <summary>一排均衡器小柱，用播放次数做种子，保证每次导出形状一致。</summary>
    public static void DrawEqualizer(
        CanvasDrawingSession session,
        float x,
        float baseline,
        float width,
        float height,
        long seed,
        Color color)
    {
        const int bars = 5;
        const float gap = 4f;
        var barWidth = (width - (gap * (bars - 1))) / bars;

        for (var i = 0; i < bars; i++)
        {
            var ratio = 0.28f + (((seed * 37) + (i * 53)) % 70) / 100f;
            var barHeight = height * ratio;

            session.FillRoundedRectangle(
                new Rect(x + (i * (barWidth + gap)), baseline - barHeight, barWidth, barHeight),
                barWidth / 2,
                barWidth / 2,
                color);
        }
    }

    public static void DrawGlassPanel(CanvasDrawingSession session, Rect rect, float radius)
    {
        session.FillRoundedRectangle(
            rect, radius, radius, Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF));
        session.DrawRoundedRectangle(
            rect, radius, radius, Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF), 1);
    }

    public static async Task<CanvasBitmap?> TryLoadAsync(CanvasDevice device, Uri? uri)
    {
        if (uri is null)
        {
            return null;
        }

        try
        {
            return await CanvasBitmap.LoadAsync(device, uri);
        }
        catch (Exception)
        {
            // 某张图失败只少画一块，不中断整张海报。
            return null;
        }
    }
}
