using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.UI;

namespace Bowen.WinUI.Services;

/// <summary>
/// 歌曲总览海报：深色波形底 + 前 10 名「波形榜单」。
/// </summary>
/// <remarks>
/// 这是波纹音乐自己的版式，不复刻官方小票：用均衡器柱、波形线和同心圆
/// 建立「波纹」识别，避免和波点官方视觉混淆。
/// </remarks>
internal static class MonthTracksPosterRenderer
{
    private const int Width = 1080;
    private const int MaxTracks = 10;
    private const float ListTop = 620;
    private const float RowHeight = 108;

    private static readonly Color TopBackground = Color.FromArgb(0xFF, 0x07, 0x0A, 0x12);
    private static readonly Color BottomBackground = Color.FromArgb(0xFF, 0x0E, 0x17, 0x30);
    private static readonly Color White = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
    private static readonly Color Muted = Color.FromArgb(0xFF, 0x94, 0xA3, 0xB8);
    private static readonly Color Cyan = Color.FromArgb(0xFF, 0x5E, 0xEA, 0xD4);
    private static readonly Color Violet = Color.FromArgb(0xFF, 0x8B, 0x5C, 0xF6);
    private static readonly Color Placeholder = Color.FromArgb(0xFF, 0x1B, 0x24, 0x3B);

    public static async Task<byte[]> RenderAsync(
        MonthPosterContent content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        MonthPosterDrawing.ChineseFont = string.IsNullOrWhiteSpace(content.UiFontFamily)
            ? "Microsoft YaHei UI"
            : content.UiFontFamily;

        var device = CanvasDevice.GetSharedDevice();
        var tracks = content.Tracks.Take(MaxTracks).ToArray();
        var listHeight = tracks.Length * RowHeight;
        var height = (int)Math.Max(1680, ListTop + listHeight + 250);
        CanvasBitmap? cover = null;
        CanvasBitmap? footerIcon = null;

        try
        {
            cover = await MonthPosterDrawing.TryLoadAsync(device, content.TopSongCover);
            footerIcon = await MonthPosterDrawing.TryLoadBrandIconAsync(device);
            cancellationToken.ThrowIfCancellationRequested();

            using var target = new CanvasRenderTarget(device, Width, height, 96);
            using (var session = target.CreateDrawingSession())
            {
                DrawBackground(session, device, height, listHeight);
                DrawHeader(session, device, content, tracks.Length);
                DrawHero(session, device, content, cover);
                DrawTracks(session, device, tracks);
                DrawFooter(session, device, content, height, footerIcon);
            }

            using var stream = new InMemoryRandomAccessStream();
            await target.SaveAsync(stream, CanvasBitmapFileFormat.Png);
            return await ReadBytesAsync(stream);
        }
        finally
        {
            cover?.Dispose();
            footerIcon?.Dispose();
        }
    }

    private static void DrawBackground(
        CanvasDrawingSession session,
        CanvasDevice device,
        int height,
        float listHeight)
    {
        using var background = MonthPosterDrawing.VerticalGradient(
            device, height, TopBackground, BottomBackground);
        session.FillRectangle(new Rect(0, 0, Width, height), background);

        MonthPosterDrawing.DrawWave(
            session, device, Width, 520, 40, 240, 0.4f,
            MonthPosterDrawing.WithAlpha(Cyan, 0x20), 2);
        MonthPosterDrawing.DrawWave(
            session, device, Width, 960, 62, 320, 1.3f,
            MonthPosterDrawing.WithAlpha(Violet, 0x16), 2);
        MonthPosterDrawing.DrawWave(
            session, device, Width, 1450, 30, 200, 2.2f,
            MonthPosterDrawing.WithAlpha(Cyan, 0x12), 1.5f);

        // 列表的横向细分隔线，像音轨刻度。
        for (var i = 0; i <= (int)(listHeight / RowHeight); i++)
        {
            var y = ListTop + (i * RowHeight) + 100;
            session.DrawLine(
                80, y, Width - 80, y,
                MonthPosterDrawing.WithAlpha(White, 0x12), 1);
        }
    }

    private static void DrawHeader(
        CanvasDrawingSession session,
        CanvasDevice device,
        MonthPosterContent content,
        int trackCount)
    {
        using var titleFormat = MonthPosterDrawing.Text(
            84, MonthPosterDrawing.ChineseFont, bold: true);
        MonthPosterDrawing.Draw(session, device, "月度波形", 80, 142, 620, titleFormat, White);

        using var kickerFormat = MonthPosterDrawing.Text(20, MonthPosterDrawing.DisplayFont);
        MonthPosterDrawing.Draw(
            session, device, "M O N T H L Y   W A V E", 84, 264, 620, kickerFormat, Muted);

        using var dateFormat = MonthPosterDrawing.Text(
            64, MonthPosterDrawing.DisplayFont, bold: true,
            alignment: CanvasHorizontalAlignment.Right);
        MonthPosterDrawing.DrawRight(
            session, device, $"{content.Year}.{content.Month:00}",
            1000, 148, 320, dateFormat, Cyan);

        using var topFormat = MonthPosterDrawing.Text(
            22, MonthPosterDrawing.DisplayFont,
            alignment: CanvasHorizontalAlignment.Right);
        MonthPosterDrawing.DrawRight(
            session, device, $"TOP {trackCount}", 1000, 264, 320, topFormat, Muted);
    }

    private static void DrawHero(
        CanvasDrawingSession session,
        CanvasDevice device,
        MonthPosterContent content,
        CanvasBitmap? cover)
    {
        const float centerX = 180;
        const float centerY = 470;

        session.DrawCircle(centerX, centerY, 100, MonthPosterDrawing.WithAlpha(Cyan, 0x50), 2);
        session.DrawCircle(centerX, centerY, 120, MonthPosterDrawing.WithAlpha(Violet, 0x38), 2);
        session.DrawCircle(centerX, centerY, 140, MonthPosterDrawing.WithAlpha(Cyan, 0x20), 2);

        MonthPosterDrawing.DrawCircleImage(
            session, device, cover, centerX, centerY, 90, Placeholder, null);

        using var labelFormat = MonthPosterDrawing.Text(20, MonthPosterDrawing.DisplayFont);
        MonthPosterDrawing.Draw(
            session, device, "M O N T H L Y   F A V O U R I T E", 330, 382, 620, labelFormat, Cyan);

        using var titleFormat = MonthPosterDrawing.Text(36, MonthPosterDrawing.ChineseFont);
        MonthPosterDrawing.Draw(
            session, device, content.TopSongTitle, 330, 438, 620, titleFormat, White);

        using var artistFormat = MonthPosterDrawing.Text(24, MonthPosterDrawing.ChineseFont);
        MonthPosterDrawing.Draw(
            session, device, content.TopSongArtist, 330, 496, 620, artistFormat, Muted);

        if (content.Tracks.Count > 0)
        {
            using var countFormat = MonthPosterDrawing.Text(20, MonthPosterDrawing.DisplayFont);
            MonthPosterDrawing.Draw(
                session, device, $"{content.Tracks[0].PlayCount} 次播放",
                330, 540, 300, countFormat, Cyan);
        }
    }

    private static void DrawTracks(
        CanvasDrawingSession session,
        CanvasDevice device,
        IReadOnlyList<MonthPosterTrack> tracks)
    {
        using var rankFormat = MonthPosterDrawing.Text(
            36, MonthPosterDrawing.MonoFont, bold: true);
        using var titleFormat = MonthPosterDrawing.Text(32, MonthPosterDrawing.ChineseFont);
        using var artistFormat = MonthPosterDrawing.Text(22, MonthPosterDrawing.ChineseFont);
        using var countFormat = MonthPosterDrawing.Text(
            26, MonthPosterDrawing.MonoFont, alignment: CanvasHorizontalAlignment.Right);

        var y = ListTop;

        foreach (var track in tracks)
        {
            MonthPosterDrawing.Draw(
                session, device, track.Rank.ToString("00"), 80, y + 8, 80, rankFormat, Cyan);
            MonthPosterDrawing.Draw(
                session, device, track.Title, 180, y + 2, 620, titleFormat, White);
            MonthPosterDrawing.Draw(
                session, device, track.Artist, 180, y + 44, 600, artistFormat, Muted);
            MonthPosterDrawing.DrawRight(
                session, device, track.PlayCount.ToString(), 880, y + 14, 90, countFormat, Cyan);

            MonthPosterDrawing.DrawEqualizer(
                session, 910, y + 66, 90, 40, track.PlayCount,
                MonthPosterDrawing.WithAlpha(Cyan, 0x90));

            y += RowHeight;
        }
    }

    private static void DrawFooter(
        CanvasDrawingSession session,
        CanvasDevice device,
        MonthPosterContent content,
        int height,
        CanvasBitmap? footerIcon)
    {
        MonthPosterDrawing.DrawWave(
            session, device, Width, height - 150, 26, 220, 0.8f,
            MonthPosterDrawing.WithAlpha(Cyan, 0x16), 1.5f);

        // 与月度总览共用同一个页脚：真实应用图标 + 品牌 + 标语 + 日期。
        MonthPosterDrawing.DrawBrandFooter(
            session, device, footerIcon, Width, height, content.Year, content.Month);
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
