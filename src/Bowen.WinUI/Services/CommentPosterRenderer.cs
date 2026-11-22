using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using Bowen.Core.Models;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.UI;

namespace Bowen.WinUI.Services;

/// <summary>评论内容长到超过画布上限。</summary>
internal sealed class CommentPosterOverflowException : Exception
{
    public CommentPosterOverflowException(string message) : base(message)
    {
    }
}

/// <summary>渲染一张评论分享卡片所需的数据。</summary>
public sealed record CommentPosterContent(
    Track Track,
    string ShareLink,
    string Nickname,
    Uri? AvatarUri,
    string Content,
    Uri? ImageUri,
    string PublishTime,
    string Location,
    long LikeCount,
    string ReplyCaption);

/// <summary>
/// 把一条评论画成白底 PNG，底部复用歌词海报的页脚。
/// </summary>
/// <remarks>
/// 与歌词海报一样用 <see cref="CanvasRenderTarget"/>，导出分辨率不依赖窗口缩放。
/// 版式与配色固定；字体可由分享弹窗传入，未传时用卡片默认字体。
/// </remarks>
internal static class CommentPosterRenderer
{
    private const int Width = 900;
    private const float Margin = 108;
    private const float ContentWidth = Width - (Margin * 2);
    private const float TopMargin = 96;
    private const float AvatarSize = 88;
    private const float AvatarGap = 24;
    private const float AvatarToTextGap = 4;
    private const float NicknameFontSize = 32;
    private const float NicknameLineHeight = 42;
    private const float MetaFontSize = 21;
    private const float MetaLineHeight = 29;
    private const float HeaderToBodyGap = 48;
    private const float BodyFontSize = 34;
    private const float BodyLineHeight = 54;
    private const float TextToImageGap = 40;
    private const float ImageToFooterGap = 48;
    private const int MaxHeight = 16000;

    private const string DefaultFontFamily = "Microsoft YaHei UI";
    private const float AvatarCornerRadius = 16;

    private static readonly Color AvatarFallbackColor = Color.FromArgb(0xFF, 0x00, 0x87, 0x6A);
    private static readonly Color White = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);

    public static async Task<byte[]> RenderAsync(
        CommentPosterContent content,
        LyricPosterStyle? style = null,
        string? fontFamily = null,
        string? uiFontFamily = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var device = CanvasDevice.GetSharedDevice();
        style ??= LyricPosterStyle.Default;
        var font = string.IsNullOrWhiteSpace(fontFamily) ? DefaultFontFamily : fontFamily.Trim();

        using var avatar = await TryLoadAsync(device, content.AvatarUri);
        using var image = await TryLoadAsync(device, content.ImageUri);
        using var footer = await PosterFooterRenderer.LoadAsync(device, content.ShareLink, style.UseLightLogo);

        cancellationToken.ThrowIfCancellationRequested();

        using var textBrush = new CanvasSolidColorBrush(device, style.TextColor);
        using var secondaryBrush = new CanvasSolidColorBrush(device, WithAlpha(style.TextColor, 0x99));

        var textWidth = ContentWidth - AvatarSize - AvatarGap;
        using var nicknameFormat = CreateEllipsisFormat(NicknameFontSize, NicknameLineHeight, font);
        nicknameFormat.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        using var nickname = new CanvasTextLayout(device, content.Nickname, nicknameFormat, textWidth, 0);

        using var metaFormat = CreateEllipsisFormat(MetaFontSize, MetaLineHeight, font);
        using var meta = new CanvasTextLayout(device, BuildMeta(content), metaFormat, textWidth, 0);

        var replyCaption = string.IsNullOrWhiteSpace(content.ReplyCaption) ? null : content.ReplyCaption;
        using var replyFormat = CreateEllipsisFormat(MetaFontSize, MetaLineHeight, font);
        using var reply = replyCaption is null ? null : new CanvasTextLayout(device, replyCaption, replyFormat, textWidth, 0);

        // 按可见墨迹居中，而不是按排版框：排版框的上下留白不对称，
        // 直接拿 LayoutBounds 居中会让文字看起来整体偏上。
        var metaTopOffset = (float)nickname.LayoutBounds.Height
            + AvatarToTextGap
            + (reply is null ? 0 : (float)reply.LayoutBounds.Height + AvatarToTextGap);
        var inkTopOffset = (float)nickname.DrawBounds.Y;
        var inkHeight = metaTopOffset + (float)meta.DrawBounds.Bottom - inkTopOffset;
        var headerHeight = Math.Max(AvatarSize, inkHeight);
        var headerTextTop = TopMargin + ((headerHeight - inkHeight) / 2) - inkTopOffset;
        var bodyTop = TopMargin + headerHeight + HeaderToBodyGap;

        using var bodyFormat = CreateWrappedFormat(font);
        using var body = string.IsNullOrWhiteSpace(content.Content)
            ? null
            : new CanvasTextLayout(device, content.Content, bodyFormat, ContentWidth, 0);

        var y = bodyTop + (float)(body?.LayoutBounds.Height ?? 0);
        var imageHeight = 0f;
        if (image is not null)
        {
            imageHeight = ContentWidth * (float)(image.Size.Height / image.Size.Width);
            y += (body is null ? 0 : TextToImageGap) + imageHeight;
        }

        var footerTop = y + ImageToFooterGap;
        var height = (int)Math.Ceiling(
            footerTop + PosterFooterRenderer.Gap + PosterFooterRenderer.Height(footer.Qr is not null) + PosterFooterRenderer.BottomMargin);
        if (height > MaxHeight)
        {
            throw new CommentPosterOverflowException("评论内容过长，暂时无法生成卡片。");
        }

        using var target = new CanvasRenderTarget(device, Width, height, 96);
        using (var session = target.CreateDrawingSession())
        {
            session.Clear(style.Background);

            DrawAvatar(session, device, avatar, content.Nickname, top: TopMargin, font);
            DrawHeader(session, nickname, reply, meta, nicknameTop: headerTextTop, textBrush, secondaryBrush);
            DrawBody(session, device, body, bodyTop, textBrush);
            if (image is not null)
            {
                DrawImage(session, device, image, bodyTop + (float)(body?.LayoutBounds.Height ?? 0) + (body is null ? 0 : TextToImageGap), imageHeight);
            }

            PosterFooterRenderer.Draw(
                session, device, content.Track, Width, footer, style, textBrush, secondaryBrush, height, uiFontFamily);
        }

        using var stream = new InMemoryRandomAccessStream();
        await target.SaveAsync(stream, CanvasBitmapFileFormat.Png);
        return await ReadBytesAsync(stream);
    }

    private static void DrawAvatar(
        CanvasDrawingSession session,
        CanvasDevice device,
        CanvasBitmap? avatar,
        string nickname,
        float top,
        string font)
    {
        var rect = new Rect(Margin, top, AvatarSize, AvatarSize);

        if (avatar is not null)
        {
            using var clip = CanvasGeometry.CreateCircle(
                device, new System.Numerics.Vector2(Margin + (AvatarSize / 2), top + (AvatarSize / 2)), AvatarSize / 2);
            using var layer = session.CreateLayer(1, clip);

            // 居中裁切：头像比例不一定是正方形。
            var source = avatar.Size;
            var scale = Math.Max(AvatarSize / source.Width, AvatarSize / source.Height);
            var sourceWidth = AvatarSize / scale;
            var sourceHeight = AvatarSize / scale;
            var sourceRect = new Rect(
                (source.Width - sourceWidth) / 2,
                (source.Height - sourceHeight) / 2,
                sourceWidth,
                sourceHeight);
            session.DrawImage(avatar, rect, sourceRect, 1, CanvasImageInterpolation.HighQualityCubic);
            return;
        }

        using var brush = new CanvasSolidColorBrush(device, AvatarFallbackColor);
        session.FillCircle(Margin + (AvatarSize / 2), top + (AvatarSize / 2), AvatarSize / 2, brush);

        if (string.IsNullOrWhiteSpace(nickname)) return;

        var initial = StringInfo.GetNextTextElement(nickname.Trim());
        using var format = new CanvasTextFormat
        {
            FontFamily = font,
            FontSize = 36,
            HorizontalAlignment = CanvasHorizontalAlignment.Center,
            VerticalAlignment = CanvasVerticalAlignment.Center,
        };
        using var layout = new CanvasTextLayout(device, initial, format, AvatarSize, AvatarSize);
        using var white = new CanvasSolidColorBrush(device, White);
        session.DrawTextLayout(layout, Margin, top, white);
    }

    private static void DrawHeader(
        CanvasDrawingSession session,
        CanvasTextLayout nickname,
        CanvasTextLayout? reply,
        CanvasTextLayout meta,
        float nicknameTop,
        CanvasSolidColorBrush textBrush,
        CanvasSolidColorBrush secondaryBrush)
    {
        var x = Margin + AvatarSize + AvatarGap;
        var y = nicknameTop;
        session.DrawTextLayout(nickname, x, y, textBrush);
        y += (float)nickname.LayoutBounds.Height + AvatarToTextGap;

        if (reply is not null)
        {
            session.DrawTextLayout(reply, x, y, secondaryBrush);
            y += (float)reply.LayoutBounds.Height + AvatarToTextGap;
        }

        session.DrawTextLayout(meta, x, y, secondaryBrush);
    }

    private static void DrawBody(
        CanvasDrawingSession session,
        CanvasDevice device,
        CanvasTextLayout? body,
        float top,
        CanvasSolidColorBrush textBrush)
    {
        if (body is not null) session.DrawTextLayout(body, Margin, top, textBrush);
    }

    private static void DrawImage(
        CanvasDrawingSession session,
        CanvasDevice device,
        CanvasBitmap image,
        float top,
        float height)
    {
        var rect = new Rect(Margin, top, ContentWidth, height);
        using var clip = CanvasGeometry.CreateRoundedRectangle(device, rect, AvatarCornerRadius, AvatarCornerRadius);
        using var layer = session.CreateLayer(1, clip);
        session.DrawImage(image, rect, new Rect(0, 0, image.Size.Width, image.Size.Height), 1,
            CanvasImageInterpolation.HighQualityCubic);
    }

    private static string BuildMeta(CommentPosterContent content)
    {
        var parts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(content.PublishTime)) parts.Add(content.PublishTime);
        if (!string.IsNullOrWhiteSpace(content.Location)) parts.Add(content.Location);
        if (content.LikeCount > 0) parts.Add($"{content.LikeCount:N0} 赞");
        return string.Join(" · ", parts);
    }

    private static CanvasTextFormat CreateEllipsisFormat(float fontSize, float lineHeight, string font) => new()
    {
        FontFamily = font,
        FontSize = fontSize,
        LineSpacing = lineHeight,
        LineSpacingMode = CanvasLineSpacingMode.Uniform,
        WordWrapping = CanvasWordWrapping.NoWrap,
        TrimmingGranularity = CanvasTextTrimmingGranularity.Character,
        TrimmingSign = CanvasTrimmingSign.Ellipsis,
        HorizontalAlignment = CanvasHorizontalAlignment.Left,
        VerticalAlignment = CanvasVerticalAlignment.Top,
    };

    private static CanvasTextFormat CreateWrappedFormat(string font) => new()
    {
        FontFamily = font,
        FontSize = BodyFontSize,
        LineSpacing = BodyLineHeight,
        LineSpacingMode = CanvasLineSpacingMode.Uniform,
        WordWrapping = CanvasWordWrapping.Wrap,
        TrimmingGranularity = CanvasTextTrimmingGranularity.None,
        HorizontalAlignment = CanvasHorizontalAlignment.Left,
        VerticalAlignment = CanvasVerticalAlignment.Top,
    };

    private static async Task<CanvasBitmap?> TryLoadAsync(CanvasDevice device, Uri? uri)
    {
        if (uri is null) return null;

        try
        {
            return await CanvasBitmap.LoadAsync(device, uri);
        }
        catch (Exception)
        {
            // 头像或配图失败只少画一块，不中断导出。
            return null;
        }
    }

    private static Color WithAlpha(Color color, byte alpha)
        => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static async Task<byte[]> ReadBytesAsync(InMemoryRandomAccessStream stream)
    {
        stream.Seek(0);
        var bytes = new byte[stream.Size];

        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size);
        reader.ReadBytes(bytes);

        return bytes;
    }
}
