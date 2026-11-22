using System.Runtime.InteropServices.WindowsRuntime;
using Bowen.Core.Media;
using Bowen.Core.Models;
using Bowen.Core.Models.Lyrics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.UI;

namespace Bowen.WinUI.Services;

/// <summary>选中的歌词放不进画布。</summary>
/// <remarks>
/// 高度按内容自适应；只有选中内容长到超过 Direct2D 纹理上限时才会出现。
/// </remarks>
internal sealed class LyricPosterOverflowException : Exception
{
    public LyricPosterOverflowException(string message) : base(message)
    {
    }
}

/// <summary>渲染一张歌词海报所需的数据。</summary>
public sealed record LyricPosterContent(
    Track Track,
    string ShareLink,
    IReadOnlyList<LyricPosterLine> Lines,
    LyricPosterStyle? Style = null);

/// <summary>
/// 把选中的歌词与曲目信息画成一张白底 PNG。
/// </summary>
/// <remarks>
/// <para>
/// <b>预览与导出共用这里。</b> 预览只是把同一份 PNG 按比例显示，不另外做一套 XAML 版式。
/// </para>
/// <para>
/// <b>宽度固定 1080，高度按歌词内容增长。</b> 歌词少时卡片自动变矮，不会在中间留一大片空白；
/// 长句按 1080 宽的正文区折行，折行后的高度计入总高。
/// </para>
/// <para>
/// 使用 <see cref="CanvasRenderTarget"/> 而不是 <c>RenderTargetBitmap</c>：后者只能拿到显示分辨率。
/// 字体固定，不读歌词页的字体设置 —— 分享图换一次花体就会失控。
/// </para>
/// </remarks>
internal static class LyricPosterRenderer
{
    /// <summary>画布宽度上限。实际宽度按内容收缩，最多到这里。</summary>
    public const int MaxWidth = 960;

    /// <summary>画布宽度下限。低于它页脚歌名与二维码会挤在一起。</summary>
    private const int MinWidth = 780;

    /// <summary>画布高度上限。超过就不出图，避免生成无法解码的超长纹理。</summary>
    private const int MaxHeight = 16000;

    /// <summary>歌词很少时也不至于缩成一条，给一个下限。</summary>
    private const int MinHeight = 720;

    private const float Margin = 108;
    private const float CoverTop = 96;
    private const float CoverToLyricsGap = 104;
    private const float MaxLyricsWidth = MaxWidth - (Margin * 2);

    /// <summary>封面随选中行数收缩：行少时更大，行多时更小。</summary>
    private const float MaxCoverSize = 560;
    private const float MinCoverSize = 280;
    private const float CoverDecreasePerLine = 12;
    private const float CoverCornerRadius = 16;
    private const float MaxCoverCornerRadius = 28;

    /// <summary>自适应字号的参考值与上下限。</summary>
    private const float ReferenceLyricFontSize = 40;

    /// <summary>最小字号。低于它就把长行折行，不再继续缩字。</summary>
    private const float MinLyricFontSize = 42;
    private const float MaxLyricFontSize = 46;
    private const float FontDecreasePerLine = 1f;
    private const float LyricLineSpacing = 1.8f;

    private const string DefaultFontFamily = "Microsoft YaHei UI";

    private readonly record struct Typography(float FontSize, float LineHeight);

    /// <summary>渲染 PNG 字节。封面或二维码失败只少画一块，不中断导出。</summary>
    public static async Task<byte[]> RenderAsync(
        LyricPosterContent content,
        string? footerFontFamily = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(content.Lines);

        var device = CanvasDevice.GetSharedDevice();
        var style = content.Style ?? LyricPosterStyle.Default;

        using var cover = await TryLoadCoverAsync(device, content.Track.CoverImage);
        using var footer = await PosterFooterRenderer.LoadAsync(device, content.ShareLink, style.UseLightLogo);

        cancellationToken.ThrowIfCancellationRequested();

        // 封面与字号共用同一把「内容量」尺子：行少则都大，行多则都小。
        var coverSize = ResolveCoverSize(content.Lines.Count);
        var coverRadius = ResolveCoverRadius(coverSize);
        var typography = ResolveTypography(device, content.Lines, style.FontFamily);

        // 画布宽度按内容收缩：右侧只保留与左边一致的边距，不再固定 1080。
        var lyricContentWidth = MeasureMaxNaturalWidth(device, content.Lines, typography, style.FontFamily);
        var width = (int)Math.Clamp(
            Math.Ceiling(Math.Max(coverSize, lyricContentWidth) + (Margin * 2)),
            MinWidth,
            MaxWidth);
        var lyricsTop = CoverTop + coverSize + CoverToLyricsGap;

        var layouts = BuildLyricLayouts(device, content.Lines, typography, style.FontFamily);

        try
        {
            var height = CalculateHeight(Measure(layouts), footer.Qr is not null, lyricsTop);
            using var target = new CanvasRenderTarget(device, width, height, 96);

            using (var session = target.CreateDrawingSession())
            {
                session.Clear(style.Background);

                using var textBrush = new CanvasSolidColorBrush(device, style.TextColor);
                using var secondaryBrush = new CanvasSolidColorBrush(device, WithAlpha(style.TextColor, 0x99));

                DrawCover(session, device, cover, coverSize, coverRadius, WithAlpha(style.TextColor, 0x12));
                DrawLyrics(session, layouts, lyricsTop, textBrush);
                PosterFooterRenderer.Draw(
                    session, device, content.Track, width, footer, style, textBrush, secondaryBrush, height,
                    footerFontFamily);
            }

            using var stream = new InMemoryRandomAccessStream();
            await target.SaveAsync(stream, CanvasBitmapFileFormat.Png);
            return await ReadBytesAsync(stream);
        }
        finally
        {
            Dispose(layouts);
        }
    }

    private static float ResolveCoverSize(int lineCount)
        => Math.Clamp(
            MaxCoverSize - ((lineCount - 1) * CoverDecreasePerLine),
            MinCoverSize,
            MaxCoverSize);

    private static float ResolveCoverRadius(float coverSize)
        => Math.Clamp(
            coverSize * (CoverCornerRadius / 240f),
            CoverCornerRadius,
            MaxCoverCornerRadius);

    private static int CalculateHeight(float lyricsHeight, bool hasQr, float lyricsTop)
    {
        var footerHeight = PosterFooterRenderer.Height(hasQr);
        var height = (int)Math.Ceiling(lyricsTop + lyricsHeight + PosterFooterRenderer.Gap + footerHeight + PosterFooterRenderer.BottomMargin);

        if (height > MaxHeight)
        {
            throw new LyricPosterOverflowException("歌词折行后超过画布高度上限。");
        }

        return Math.Max(height, MinHeight);
    }

    private static void DrawCover(
        CanvasDrawingSession session,
        CanvasDevice device,
        CanvasBitmap? cover,
        float size,
        float radius,
        Color placeholderColor)
    {
        // 封面与歌词、品牌共用同一条左边线，不做水平居中。
        var rect = new Rect(Margin, CoverTop, size, size);

        if (cover is null)
        {
            using var brush = new CanvasSolidColorBrush(device, placeholderColor);
            session.FillRoundedRectangle(rect, radius, radius, brush);
            return;
        }

        using var clip = CanvasGeometry.CreateRoundedRectangle(device, rect, radius, radius);
        using var layer = session.CreateLayer(1, clip);

        // 居中裁切：封面比例不一定是正方形，不能直接拉伸变形。
        var source = cover.Size;
        var scale = Math.Max(size / source.Width, size / source.Height);
        var sourceWidth = size / scale;
        var sourceHeight = size / scale;
        var sourceRect = new Rect(
            (source.Width - sourceWidth) / 2,
            (source.Height - sourceHeight) / 2,
            sourceWidth,
            sourceHeight);

        session.DrawImage(cover, rect, sourceRect, 1, CanvasImageInterpolation.HighQualityCubic);
    }

    private static Typography ResolveTypography(
        CanvasDevice device, IReadOnlyList<LyricPosterLine> lines, string fontFamily)
    {
        var reference = new Typography(
            ReferenceLyricFontSize,
            ReferenceLyricFontSize * LyricLineSpacing);

        var maxNaturalWidth = MeasureMaxNaturalWidth(device, lines, reference, fontFamily);
        var countBased = Math.Clamp(
            MaxLyricFontSize - ((lines.Count - 1) * FontDecreasePerLine),
            MinLyricFontSize,
            MaxLyricFontSize);

        var widthFit = maxNaturalWidth > 0
            ? ReferenceLyricFontSize * MaxLyricsWidth / maxNaturalWidth
            : MaxLyricFontSize;

        // 字号有下限：放不下就折行，不继续缩小。
        var fontSize = Math.Clamp(Math.Min(countBased, widthFit), MinLyricFontSize, MaxLyricFontSize);

        return new Typography(fontSize, fontSize * LyricLineSpacing);
    }

    /// <summary>按参考字号量每行不折行时的自然宽度，取最大值。</summary>
    private static float MeasureMaxNaturalWidth(
        CanvasDevice device, IReadOnlyList<LyricPosterLine> lines, Typography typography, string fontFamily)
    {
        var maxWidth = 0f;

        var resolvedFont = string.IsNullOrWhiteSpace(fontFamily) ? DefaultFontFamily : fontFamily.Trim();
        using var format = CreateAutoWidthFormat(typography.FontSize, typography.LineHeight, resolvedFont);

        foreach (var line in lines)
        {
            using var layout = new CanvasTextLayout(device, line.Text, format, 0, 0);
            maxWidth = Math.Max(maxWidth, (float)layout.LayoutBounds.Width);
        }

        return maxWidth;
    }

    private static List<CanvasTextLayout> BuildLyricLayouts(
        CanvasDevice device, IReadOnlyList<LyricPosterLine> lines, Typography typography, string fontFamily)
    {
        var layouts = new List<CanvasTextLayout>(lines.Count);

        // 原文与译文使用完全相同的字号与行距。
        var resolvedFont = string.IsNullOrWhiteSpace(fontFamily) ? DefaultFontFamily : fontFamily.Trim();
        using var format = CreateWrappedFormat(typography.FontSize, typography.LineHeight, resolvedFont);

        foreach (var line in lines)
        {
            layouts.Add(new CanvasTextLayout(device, line.Text, format, MaxLyricsWidth, 0));
        }

        return layouts;
    }

    private static float Measure(IReadOnlyList<CanvasTextLayout> layouts)
    {
        var height = 0f;
        foreach (var layout in layouts)
        {
            height += (float)layout.LayoutBounds.Height;
        }

        return height;
    }

    private static void DrawLyrics(
        CanvasDrawingSession session,
        IReadOnlyList<CanvasTextLayout> layouts,
        float lyricsTop,
        CanvasSolidColorBrush textBrush)
    {
        var y = lyricsTop;

        foreach (var layout in layouts)
        {
            session.DrawTextLayout(layout, Margin, y, textBrush);
            y += (float)layout.LayoutBounds.Height;
        }
    }

    /// <summary>量自然宽度用：不折行、不截断，配合 <c>requestedWidth = 0</c> 取整行宽度。</summary>
    private static CanvasTextFormat CreateAutoWidthFormat(float fontSize, float lineHeight, string fontFamily) => new()
    {
        FontFamily = fontFamily,
        FontSize = fontSize,
        LineSpacing = lineHeight,
        LineSpacingMode = CanvasLineSpacingMode.Uniform,
        WordWrapping = CanvasWordWrapping.NoWrap,
        TrimmingGranularity = CanvasTextTrimmingGranularity.None,
        HorizontalAlignment = CanvasHorizontalAlignment.Left,
        VerticalAlignment = CanvasVerticalAlignment.Top,
    };

    /// <summary>正文用：允许折行，不设截断。</summary>
    private static CanvasTextFormat CreateWrappedFormat(float fontSize, float lineHeight, string fontFamily) => new()
    {
        FontFamily = fontFamily,
        FontSize = fontSize,
        LineSpacing = lineHeight,
        LineSpacingMode = CanvasLineSpacingMode.Uniform,
        WordWrapping = CanvasWordWrapping.Wrap,
        TrimmingGranularity = CanvasTextTrimmingGranularity.None,
        HorizontalAlignment = CanvasHorizontalAlignment.Left,
        VerticalAlignment = CanvasVerticalAlignment.Top,
    };

    private static Color WithAlpha(Color color, byte alpha)
        => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static async Task<CanvasBitmap?> TryLoadCoverAsync(CanvasDevice device, Uri? cover)
    {
        if (cover is null)
        {
            return null;
        }

        try
        {
            var uri = CoverArtUrl.Jpeg(cover, 640);
            return uri is null ? null : await CanvasBitmap.LoadAsync(device, uri);
        }
        catch (Exception)
        {
            // 封面失败不阻塞导出：占位块照样排，歌词与底部照常画。
            return null;
        }
    }

    private static async Task<byte[]> ReadBytesAsync(InMemoryRandomAccessStream stream)
    {
        stream.Seek(0);
        var bytes = new byte[stream.Size];

        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size);
        reader.ReadBytes(bytes);

        return bytes;
    }

    private static void Dispose(List<CanvasTextLayout> layouts)
    {
        foreach (var layout in layouts)
        {
            layout.Dispose();
        }

        layouts.Clear();
    }
}
