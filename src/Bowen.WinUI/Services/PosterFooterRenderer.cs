using System.Runtime.InteropServices.WindowsRuntime;
using Bowen.Core.Models;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Text;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.UI;

namespace Bowen.WinUI.Services;

/// <summary>
/// 海报页脚：左侧品牌块、右侧歌名 / 歌手、右下角分享二维码。
/// </summary>
/// <remarks>
/// 歌词海报与评论分享卡片共用这一份绘制，字号、间距和二维码托板只维护一处。
/// 封面与正文由各自的渲染器负责，这里只认画布宽度与底部高度。
/// </remarks>
internal static class PosterFooterRenderer
{
    /// <summary>正文 / 封面与页脚之间的距离。</summary>
    public const float Gap = 80;

    /// <summary>页脚底边到画布底边的距离。</summary>
    public const float BottomMargin = 72;

    private const float Margin = 108;
    private const float BrandBlockWidth = 176;
    private const float BrandIconSize = 48;
    private const float BrandTextGap = 12;
    private const float QrBoxSize = BrandIconSize * 2;
    private const float QrPlatePadding = 8;
    private const float QrPlateSize = QrBoxSize + (QrPlatePadding * 2);
    private const float QrPlateRadius = 8;
    private const float TitleFontSize = 28;
    private const float TitleLineHeight = 38;
    private const float ArtistFontSize = 22;
    private const float ArtistLineHeight = 30;
    private const float TrackTextMaxWidth = 420;
    private const float TitleArtistGap = 6;
    private const float DividerGap = 24;
    private const float DividerThickness = 2;
    private const string DefaultFontFamily = "Microsoft YaHei UI";
    private const string BrandName = "波纹音乐";

    private static readonly Color White = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
    private static readonly QrImageFactory QrFactory = new();

    /// <summary>页脚占用的高度；没有二维码时退回两行文字的高度。</summary>
    public static float Height(bool hasQr) => hasQr ? QrPlateSize : TitleLineHeight + ArtistLineHeight;

    /// <summary>页脚图片资源。任何一项加载失败只少画一块，不中断导出。</summary>
    public sealed record Assets(CanvasBitmap? Brand, CanvasBitmap? Qr) : IDisposable
    {
        public void Dispose()
        {
            Brand?.Dispose();
            Qr?.Dispose();
        }
    }

    public static async Task<Assets> LoadAsync(CanvasDevice device, string shareLink, bool useLightLogo)
    {
        var brand = await TryLoadBitmapAsync(device, BrandIconPath(useLightLogo));
        var qr = await TryLoadQrAsync(device, shareLink);
        return new Assets(brand, qr);
    }

    public static void Draw(
        CanvasDrawingSession session,
        CanvasDevice device,
        Track track,
        int width,
        Assets assets,
        LyricPosterStyle style,
        CanvasSolidColorBrush textBrush,
        CanvasSolidColorBrush secondaryBrush,
        int height)
    {
        var plateX = width - Margin - QrPlateSize;
        var plateY = height - BottomMargin - QrPlateSize;
        var centerY = plateY + (QrPlateSize / 2);

        if (assets.Qr is not null)
        {
            // 二维码始终放在白色托板上，深色背景也能扫。
            var plate = new Rect(plateX, plateY, QrPlateSize, QrPlateSize);
            using var plateBrush = new CanvasSolidColorBrush(device, White);
            session.FillRoundedRectangle(plate, QrPlateRadius, QrPlateRadius, plateBrush);

            var qrX = plateX + ((QrPlateSize - QrBoxSize) / 2);
            var qrY = plateY + ((QrPlateSize - QrBoxSize) / 2);
            var drawSize = Math.Min(QrBoxSize, assets.Qr.Size.Width);
            var rect = new Rect(qrX + ((QrBoxSize - drawSize) / 2), qrY + ((QrBoxSize - drawSize) / 2), drawSize, drawSize);
            session.DrawImage(assets.Qr, rect, new Rect(0, 0, assets.Qr.Size.Width, assets.Qr.Size.Height), 1,
                CanvasImageInterpolation.NearestNeighbor);
        }

        var dividerX = plateX - DividerGap;
        var rightEdge = assets.Qr is not null ? dividerX - DividerGap : width - Margin;

        if (assets.Qr is not null)
        {
            using var dividerBrush = new CanvasSolidColorBrush(device, WithAlpha(style.TextColor, 0x26));
            session.DrawLine(dividerX, plateY, dividerX, plateY + QrPlateSize, dividerBrush, DividerThickness);
        }

        var titleInkTop = DrawTrackText(session, device, track, rightEdge, centerY, textBrush, secondaryBrush);
        DrawBrand(session, device, assets.Brand, textBrush, titleInkTop);
    }

    /// <returns>歌名首行字形墨迹的顶部，供左下品牌文字对齐。</returns>
    private static float DrawTrackText(
        CanvasDrawingSession session,
        CanvasDevice device,
        Track track,
        float rightEdge,
        float centerY,
        CanvasSolidColorBrush textBrush,
        CanvasSolidColorBrush secondaryBrush)
    {
        // 给左侧品牌块留出避让，窄画布上长歌名也不会压到「波纹音乐」。
        var maxWidth = Math.Min(TrackTextMaxWidth, Math.Max(120, rightEdge - Margin - BrandBlockWidth - 24));

        using var titleFormat = CreateSingleLineFormat(TitleFontSize, TitleLineHeight);
        titleFormat.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
        using var artistFormat = CreateSingleLineFormat(ArtistFontSize, ArtistLineHeight);

        using var title = new CanvasTextLayout(device, track.Title, titleFormat, maxWidth, 0);
        using var artist = new CanvasTextLayout(device, track.ArtistText, artistFormat, maxWidth, 0);

        var blockHeight = (float)(title.LayoutBounds.Height + TitleArtistGap + artist.LayoutBounds.Height);
        var titleTop = centerY - (blockHeight / 2);

        // 用两行文字的实际墨迹中心去对齐二维码中心，排版框的上下留白不算。
        var artistTop = titleTop + (float)title.LayoutBounds.Height + TitleArtistGap;
        var inkTop = titleTop + (float)title.DrawBounds.Y;
        var inkBottom = artistTop + (float)artist.DrawBounds.Bottom;
        var shift = centerY - ((inkTop + inkBottom) / 2);
        titleTop += shift;
        artistTop += shift;

        // 超长标题在排版时已被省略号截断；右对齐时再夹一次宽度，保证不会越过右边界。
        var titleWidth = Math.Min((float)title.LayoutBounds.Width, maxWidth);
        var artistWidth = Math.Min((float)artist.LayoutBounds.Width, maxWidth);

        session.DrawTextLayout(title, rightEdge - titleWidth, titleTop, textBrush);
        session.DrawTextLayout(artist, rightEdge - artistWidth, artistTop, secondaryBrush);

        return titleTop + (float)title.DrawBounds.Y;
    }

    private static void DrawBrand(
        CanvasDrawingSession session,
        CanvasDevice device,
        CanvasBitmap? brand,
        CanvasSolidColorBrush textBrush,
        float textInkTop)
    {
        using var format = CreateSingleLineFormat(26, 34);
        format.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        using var layout = new CanvasTextLayout(device, BrandName, format, 300, 0);

        var ink = layout.DrawBounds;

        // 文字墨迹顶部与右侧歌名齐平；图标再与文字墨迹的垂直中心对齐。
        var textTop = textInkTop - (float)ink.Y;
        var center = textTop + (float)(ink.Y + (ink.Height / 2));
        var iconTop = center - (BrandIconSize / 2);

        if (brand is not null)
        {
            var rect = new Rect(Margin, iconTop, BrandIconSize, BrandIconSize);
            session.DrawImage(brand, rect, new Rect(0, 0, brand.Size.Width, brand.Size.Height), 1,
                CanvasImageInterpolation.HighQualityCubic);
        }

        session.DrawTextLayout(layout, Margin + BrandIconSize + BrandTextGap, (float)textTop, textBrush);
    }

    /// <summary>单行信息用：超宽直接省略号，不折行。</summary>
    private static CanvasTextFormat CreateSingleLineFormat(float fontSize, float lineHeight) => new()
    {
        FontFamily = DefaultFontFamily,
        FontSize = fontSize,
        LineSpacing = lineHeight,
        LineSpacingMode = CanvasLineSpacingMode.Uniform,
        WordWrapping = CanvasWordWrapping.NoWrap,
        TrimmingGranularity = CanvasTextTrimmingGranularity.Character,
        TrimmingSign = CanvasTrimmingSign.Ellipsis,
        HorizontalAlignment = CanvasHorizontalAlignment.Left,
        VerticalAlignment = CanvasVerticalAlignment.Top,
    };

    private static Color WithAlpha(Color color, byte alpha)
        => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static async Task<CanvasBitmap?> TryLoadBitmapAsync(CanvasDevice device, string path)
    {
        try
        {
            return await CanvasBitmap.LoadAsync(device, path);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static async Task<CanvasBitmap?> TryLoadQrAsync(CanvasDevice device, string shareLink)
    {
        if (string.IsNullOrWhiteSpace(shareLink))
        {
            return null;
        }

        try
        {
            // 2 px/模块：版本 4–5 的曲目链接约 82–90 px，正好放进 96 px 的二维码区，
            // 且保持整数像素，不靠缩放糊边。
            var png = QrFactory.CreatePngBytes(shareLink, pixelsPerModule: 2);
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(png.AsBuffer());
            stream.Seek(0);
            return await CanvasBitmap.LoadAsync(device, stream);
        }
        catch (Exception)
        {
            // 二维码失败只少一块内容，不阻塞导出。
            return null;
        }
    }

    private static string BrandIconPath(bool useLight)
        => Path.Combine(AppContext.BaseDirectory, "Assets", useLight ? "RippleLight.png" : "Ripple.png");
}
