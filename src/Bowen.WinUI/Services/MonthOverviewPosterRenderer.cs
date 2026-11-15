using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.UI;

namespace Bowen.WinUI.Services;

/// <summary>
/// 月度总览海报：深色波纹底、悬浮封面、玻璃统计卡。
/// </summary>
/// <remarks>
/// 这是波纹音乐自己的版式，不复刻任何官方海报：用同心波纹、波形线和均衡器
/// 建立「波纹」识别，避免和波点官方视觉混淆。
/// </remarks>
internal static class MonthOverviewPosterRenderer
{
    private const int Width = 1080;
    private const int Height = 1700;
    private const float Margin = 80;

    private static MonthPosterPalette _palette = MonthPosterPalette.Dark;

    private static Color TopBackground => _palette.BackgroundTop;
    private static Color BottomBackground => _palette.BackgroundBottom;
    private static Color White => _palette.PrimaryText;
    private static Color Muted => _palette.MutedText;
    private static Color Cyan => _palette.Accent;
    private static Color Violet => _palette.AccentSecondary;
    private static Color Placeholder => _palette.Placeholder;
    private static Color AvatarFallback => _palette.AvatarFallback;

    public static async Task<byte[]> RenderAsync(
        MonthPosterContent content,
        bool dark,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        _palette = dark ? MonthPosterPalette.Dark : MonthPosterPalette.Light;
        MonthPosterDrawing.ChineseFont = string.IsNullOrWhiteSpace(content.UiFontFamily)
            ? "Microsoft YaHei UI"
            : content.UiFontFamily;

        var device = CanvasDevice.GetSharedDevice();
        var bitmaps = new List<CanvasBitmap?>();

        try
        {
            var avatar = await MonthPosterDrawing.TryLoadAsync(device, content.AvatarUri);
            bitmaps.Add(avatar);

            var footerIcon = await MonthPosterDrawing.TryLoadBrandIconAsync(device, dark);
            bitmaps.Add(footerIcon);

            var covers = new List<CanvasBitmap?>();

            foreach (var uri in content.AlbumCovers.Take(9))
            {
                var bitmap = await MonthPosterDrawing.TryLoadAsync(device, uri);
                covers.Add(bitmap);
                bitmaps.Add(bitmap);
            }

            cancellationToken.ThrowIfCancellationRequested();

            using var target = new CanvasRenderTarget(device, Width, Height, 96);
            using (var session = target.CreateDrawingSession())
            {
                DrawBackground(session, device);
                DrawHeader(session, device, content, avatar);
                DrawHero(session, device, content, covers);
                DrawAlbumWall(session, device, covers);
                DrawStats(session, device, content);
                MonthPosterDrawing.DrawBrandFooter(
                    session, device, footerIcon, Width, Height, content.Year, content.Month,
                    White, Muted);
            }

            using var stream = new InMemoryRandomAccessStream();
            await target.SaveAsync(stream, CanvasBitmapFileFormat.Png);
            return await ReadBytesAsync(stream);
        }
        finally
        {
            foreach (var bitmap in bitmaps)
            {
                bitmap?.Dispose();
            }
        }
    }

    private static void DrawBackground(CanvasDrawingSession session, CanvasDevice device)
    {
        using var background = MonthPosterDrawing.VerticalGradient(
            device, Height, TopBackground, BottomBackground);
        session.FillRectangle(new Rect(0, 0, Width, Height), background);

        // 底部两条很淡的波形，把版面和「波纹」主题连起来。
        MonthPosterDrawing.DrawWave(
            session, device, Width, 1460, 32, 260, 0.4f,
            MonthPosterDrawing.WithAlpha(Cyan, 0x14), 2);
        MonthPosterDrawing.DrawWave(
            session, device, Width, 1520, 22, 180, 2.1f,
            MonthPosterDrawing.WithAlpha(Violet, 0x10), 1.5f);
    }

    private static void DrawHeader(
        CanvasDrawingSession session,
        CanvasDevice device,
        MonthPosterContent content,
        CanvasBitmap? avatar)
    {
        var nickname = string.IsNullOrWhiteSpace(content.Nickname) ? "听歌的人" : content.Nickname.Trim();
        var initial = string.IsNullOrEmpty(nickname) ? null : StringInfo.GetNextTextElement(nickname);

        // 头像外圈左缘对齐 80 的页边距。
        session.DrawCircle(138, 120, 58, MonthPosterDrawing.WithAlpha(Cyan, 0xB0), 2);
        MonthPosterDrawing.DrawCircleImage(
            session, device, avatar, 138, 120, 52, AvatarFallback, initial);

        using var nameFormat = MonthPosterDrawing.Text(34, MonthPosterDrawing.ChineseFont);
        MonthPosterDrawing.Draw(session, device, nickname, 214, 100, 420, nameFormat, White);

        using var kickerFormat = MonthPosterDrawing.Text(16, MonthPosterDrawing.DisplayFont);
        MonthPosterDrawing.Draw(
            session, device, "M O N T H L Y   W A V E", 216, 150, 420, kickerFormat, Cyan);

        // 顶部两组文字各占两行：头像/昵称与品牌对齐，第二行的小字与月份对齐。
        using var brandFormat = MonthPosterDrawing.Text(
            40, MonthPosterDrawing.ChineseFont, bold: true,
            alignment: CanvasHorizontalAlignment.Right);
        MonthPosterDrawing.DrawRight(session, device, "波纹音乐", 1000, 92, 460, brandFormat, White);

        using var monthFormat = MonthPosterDrawing.Text(
            32, MonthPosterDrawing.DisplayFont, bold: true,
            alignment: CanvasHorizontalAlignment.Right);
        MonthPosterDrawing.DrawRight(
            session, device, $"{content.Year}.{content.Month:00}", 1000, 142, 460, monthFormat, Cyan);
    }

    private static void DrawHero(
        CanvasDrawingSession session,
        CanvasDevice device,
        MonthPosterContent content,
        IReadOnlyList<CanvasBitmap?> covers)
    {
        const float centerX = 270;
        const float centerY = 540;

        // 同心波纹。
        session.DrawCircle(centerX, centerY, 230, MonthPosterDrawing.WithAlpha(Cyan, 0x40), 2);
        session.DrawCircle(centerX, centerY, 300, MonthPosterDrawing.WithAlpha(Violet, 0x30), 2);
        session.DrawCircle(centerX, centerY, 370, MonthPosterDrawing.WithAlpha(Cyan, 0x20), 2);
        session.DrawCircle(centerX, centerY, 440, MonthPosterDrawing.WithAlpha(Violet, 0x14), 2);

        var hero = covers.Count > 0 ? covers[0] : null;
        var coverRect = new Rect(Margin, 350, 380, 380);

        session.FillRoundedRectangle(
            new Rect(64, 334, 412, 412), 40, 40, MonthPosterDrawing.WithAlpha(Cyan, 0x14));
        MonthPosterDrawing.DrawCover(session, device, hero, coverRect, 28, Placeholder);
        session.DrawRoundedRectangle(
            coverRect, 28, 28, MonthPosterDrawing.WithAlpha(White, 0x24), 1);

        // 徽标放在封面水平中心、顶部内缩。文字按墨迹尺寸手动居中，
        // 不依赖 CanvasTextFormat 的对齐行为。
        const float badgeWidth = 116;
        const float badgeHeight = 36;
        const float badgeX = 100;
        const float badgeY = 370;

        using var measureFormat = MonthPosterDrawing.Text(17, MonthPosterDrawing.ChineseFont);
        using var measure = new CanvasTextLayout(device, "本月最爱", measureFormat, 1000, 0);
        var inkWidth = (float)measure.DrawBounds.Width;
        var inkHeight = (float)measure.DrawBounds.Height;
        if (inkWidth <= 0)
        {
            inkWidth = (float)measure.LayoutBounds.Width;
        }

        if (inkHeight <= 0)
        {
            inkHeight = (float)measure.LayoutBounds.Height;
        }

        var textX = badgeX + ((badgeWidth - inkWidth) / 2) - (float)measure.DrawBounds.X;
        var textY = badgeY + ((badgeHeight - inkHeight) / 2) - (float)measure.DrawBounds.Y;

        session.FillRoundedRectangle(
            new Rect(badgeX, badgeY, badgeWidth, badgeHeight),
            badgeHeight / 2, badgeHeight / 2,
            Color.FromArgb(0xD0, 0x07, 0x0A, 0x12));
        session.DrawTextLayout(measure, textX, textY, MonthPosterPalette.Dark.Accent);

        using var labelFormat = MonthPosterDrawing.Text(20, MonthPosterDrawing.DisplayFont);
        MonthPosterDrawing.Draw(
            session, device, "M O N T H L Y   F A V O U R I T E", 520, 417, 480, labelFormat, Cyan);

        using var titleFormat = MonthPosterDrawing.Text(
            44, MonthPosterDrawing.ChineseFont, bold: true,
            wrapping: CanvasWordWrapping.Wrap);
        MonthPosterDrawing.Draw(
            session, device, content.TopSongTitle, 520, 477, 480, titleFormat, White);

        using var artistFormat = MonthPosterDrawing.Text(26, MonthPosterDrawing.ChineseFont);
        MonthPosterDrawing.Draw(
            session, device, content.TopSongArtist, 520, 597, 480, artistFormat, Muted);

        if (content.Tracks.Count > 0)
        {
            using var countFormat = MonthPosterDrawing.Text(20, MonthPosterDrawing.DisplayFont);
            MonthPosterDrawing.Draw(
                session, device, $"{content.Tracks[0].PlayCount} 次播放",
                520, 639, 300, countFormat, Cyan);
        }
    }

    private static void DrawAlbumWall(
        CanvasDrawingSession session,
        CanvasDevice device,
        IReadOnlyList<CanvasBitmap?> covers)
    {
        if (covers.Count <= 1)
        {
            return;
        }

        var positions = new (float X, float Y)[]
        {
            (80, 790), (320, 790), (560, 790), (800, 790),
            (80, 1040), (320, 1040), (560, 1040), (800, 1040),
        };

        const float size = 200;

        for (var i = 1; i < covers.Count && i <= positions.Length; i++)
        {
            var (x, y) = positions[i - 1];
            var rect = new Rect(x, y, size, size);

            // 一层很轻的阴影，让封面像浮在波纹上。
            session.FillRoundedRectangle(
                new Rect(x, y + 10, size, size), 14, 14,
                MonthPosterDrawing.WithAlpha(Color.FromArgb(0xFF, 0x00, 0x00, 0x00), 0x50));
            MonthPosterDrawing.DrawCover(session, device, covers[i], rect, 14, Placeholder);
            session.DrawRoundedRectangle(
                rect, 14, 14, MonthPosterDrawing.WithAlpha(White, 0x20), 1);
        }

        MonthPosterDrawing.DrawWave(
            session, device, Width, 1010, 26, 260, 0.2f,
            MonthPosterDrawing.WithAlpha(Cyan, 0x14), 1.5f);
        MonthPosterDrawing.DrawWave(
            session, device, Width, 1260, 20, 220, 2.4f,
            MonthPosterDrawing.WithAlpha(Violet, 0x12), 1.5f);
    }

    private static void DrawStats(
        CanvasDrawingSession session,
        CanvasDevice device,
        MonthPosterContent content)
    {
        // 缩小成一块居中的数据面板，四列等宽，内容整体垂直居中。
        // 与专辑墙同宽（80 → 1000），四列铺满，内容整体垂直居中。
        var panel = new Rect(80, 1300, 920, 150);
        MonthPosterDrawing.DrawGlassPanel(
            session, panel, 20, _palette.GlassFill, _palette.GlassStroke);

        for (var i = 1; i < 4; i++)
        {
            var x = 80f + (i * 230f);
            session.DrawLine(
                x, 1320, x, 1430,
                MonthPosterDrawing.WithAlpha(White, 0x14), 1);
        }

        using var valueFormat = MonthPosterDrawing.Text(
            40, MonthPosterDrawing.DisplayFont, bold: true,
            alignment: CanvasHorizontalAlignment.Center);
        using var labelFormat = MonthPosterDrawing.Text(
            17, MonthPosterDrawing.ChineseFont,
            alignment: CanvasHorizontalAlignment.Center);

        DrawStat(session, device, panel, 195, content.ListenTimeText, "听歌时长", Cyan, valueFormat, labelFormat);
        DrawStat(
            session, device, panel, 425,
            content.PlayCount is { } playCount ? $"{playCount} 次" : "—",
            "播放次数", White, valueFormat, labelFormat);
        DrawStat(
            session, device, panel, 655,
            content.SongCount is { } songCount ? $"{songCount} 首" : "—",
            "听过曲目", White, valueFormat, labelFormat);
        DrawStat(
            session, device, panel, 885,
            content.TimeRank is { } rank ? $"{rank}%" : "—",
            "超过用户", Violet, valueFormat, labelFormat);
    }

    private static void DrawStat(
        CanvasDrawingSession session,
        CanvasDevice device,
        Rect panel,
        float centerX,
        string value,
        string label,
        Color color,
        CanvasTextFormat valueFormat,
        CanvasTextFormat labelFormat)
    {
        const float columnWidth = 230;
        const float gap = 30;
        var x = centerX - (columnWidth / 2);

        using var labelLayout = new CanvasTextLayout(device, label, labelFormat, columnWidth, 0);
        using var valueLayout = new CanvasTextLayout(device, value, valueFormat, columnWidth, 0);

        var labelInk = labelLayout.DrawBounds;
        var valueInk = valueLayout.DrawBounds;
        var labelHeight = (float)(labelInk.Height > 0 ? labelInk.Height : labelLayout.LayoutBounds.Height);
        var valueHeight = (float)(valueInk.Height > 0 ? valueInk.Height : valueLayout.LayoutBounds.Height);

        var blockHeight = labelHeight + gap + valueHeight;
        var top = (float)panel.Top + (((float)panel.Height - blockHeight) / 2);

        // 直接按可见墨迹的中心定位，不依赖布局框的顶部或对齐属性。
        var labelX = centerX - (float)(labelInk.X + (labelInk.Width / 2));
        var labelY = top + (labelHeight / 2) - (float)(labelInk.Y + (labelInk.Height / 2));
        var valueCenterY = top + labelHeight + gap + (valueHeight / 2);
        var valueX = centerX - (float)(valueInk.X + (valueInk.Width / 2));
        var valueY = valueCenterY - (float)(valueInk.Y + (valueInk.Height / 2));

        session.DrawTextLayout(labelLayout, labelX, labelY, Muted);
        session.DrawTextLayout(valueLayout, valueX, valueY, color);
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
}
