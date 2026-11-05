using Bowen.Core.Models;
using Bowen.Core.Models.Lyrics;
using Bowen.Core.Services.Abstractions;
using Bowen.WinUI.Media;
using Bowen.WinUI.Services;
using Bowen.WinUI.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Bowen.WinUI.Controls;

/// <summary>打开歌词海报对话框所需的一切。</summary>
public sealed record LyricPosterDialogContext(
    Track Track,
    string ShareLink,
    IReadOnlyList<LyricPosterLine> Lines,
    int DefaultLineIndex,
    XamlRoot XamlRoot,
    ElementTheme Theme,
    nint WindowHandle,
    IClipboardService Clipboard,
    Func<Task> ReportShareAsync,
    INoticeSink Notices,
    ILogger? Logger,
    IReadOnlyList<FontOption> FontOptions,
    ILyricPosterSettingsStore SettingsStore,
    CoverPaletteLoader PaletteLoader);

/// <summary>
/// 歌词海报对话框：先选句，再看预览，最后保存或复制。
/// </summary>
/// <remarks>
/// <para>
/// <b>两步放在同一个 ContentDialog 里。</b> 预览的「返回修改」只是切回第一步，
/// 勾选状态与自定义设置由 <see cref="LyricPosterDialogContent"/> 保存，不会丢。
/// </para>
/// <para>
/// <b>保存 / 复制只认最新渲染结果。</b> 右侧设置变化会触发重渲染，渲染期间按钮禁用；
/// 导出永远读 <see cref="LyricPosterDialogContent.LatestPng"/>，不缓存旧图。
/// </para>
/// </remarks>
internal static class LyricPosterDialog
{
    public static async Task ShowAsync(LyricPosterDialogContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var content = new LyricPosterDialogContent(context);

        var dialog = AppDialogs.Create(null, context.XamlRoot, context.Theme, maxWidth: 760);
        // 看大图时内容会扩到接近窗口，最大宽高要留出空间。
        dialog.Resources["ContentDialogMaxWidth"] = 4000d;
        dialog.Resources["ContentDialogMaxHeight"] = 4000d;
        dialog.Content = content;
        dialog.PrimaryButtonText = "生成海报";
        dialog.SecondaryButtonText = "复制歌词";
        dialog.CloseButtonText = "";
        dialog.DefaultButton = ContentDialogButton.Primary;

        // 主按钮一开始就套强调色，避免对话框打开后靠默认按钮状态再慢一拍上色。
        if (Application.Current.Resources.TryGetValue("AccentButtonStyle", out var accent)
            && accent is Style accentStyle)
        {
            dialog.PrimaryButtonStyle = accentStyle;
        }

        // 标题与关闭按钮放在内容里；这里只负责把 X 接到 Hide。
        content.CloseRequested += (_, _) => dialog.Hide();

        var previewing = false;
        var reported = false;

        void UpdateButtons()
        {
            dialog.IsPrimaryButtonEnabled = previewing
                ? content.LatestPng is not null && !content.IsRendering
                : content.SelectedCount > 0 && !content.IsRendering;
            dialog.IsSecondaryButtonEnabled = previewing
                ? content.LatestPng is not null && !content.IsRendering
                : content.SelectedCount > 0;
        }

        UpdateButtons();
        content.SelectionChanged += (_, _) => UpdateButtons();
        content.RenderStateChanged += (_, _) => UpdateButtons();

        dialog.PrimaryButtonClick += async (_, args) =>
        {
            // 两个阶段都留在对话框里：生成后看预览，保存后还要能继续复制。
            args.Cancel = true;

            if (!previewing)
            {
                await GeneratePreviewAsync();
                return;
            }

            if (!content.IsRendering && content.LatestPng is { } latest)
            {
                await SaveAsync(latest);
            }
        };

        dialog.SecondaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;

            if (!previewing)
            {
                content.CopyLyrics();
                return;
            }

            if (content.IsRendering || content.LatestPng is not { } latest)
            {
                return;
            }

            try
            {
                await context.Clipboard.SetImageAndTextAsync(latest, context.ShareLink);
                await ReportOnceAsync();
                context.Notices.Show("海报已复制到剪贴板", NoticeSeverity.Success);
            }
            catch (Exception exception)
            {
                context.Logger?.LogWarning(exception, "复制歌词海报失败");
                context.Notices.Show("复制海报失败，请稍后再试。", NoticeSeverity.Error);
            }
        };

        dialog.CloseButtonClick += (_, args) =>
        {
            if (!previewing)
            {
                // 选句阶段没有底部关闭按钮；这里只可能是 Esc，允许它关窗。
                return;
            }

            // 预览阶段的「返回修改」：保留勾选与设置，回到第一步。
            args.Cancel = true;
            previewing = false;
            content.ShowSelectionStage();
            dialog.PrimaryButtonText = "生成海报";
            dialog.SecondaryButtonText = "复制歌词";
            dialog.CloseButtonText = "";
            UpdateButtons();
        };

        await dialog.ShowAsync();

        async Task GeneratePreviewAsync()
        {
            await content.RenderLatestAsync();

            if (content.LatestPng is null)
            {
                content.ShowMessage(content.LastRenderError ?? "海报生成失败，请稍后再试。");
                UpdateButtons();
                return;
            }

            previewing = true;
            content.ShowPreviewStage();
            dialog.PrimaryButtonText = "保存图片";
            dialog.SecondaryButtonText = "复制图片";
            dialog.CloseButtonText = "返回修改";
            UpdateButtons();
        }

        async Task SaveAsync(byte[] bytes)
        {
            try
            {
                var picker = new FileSavePicker
                {
                    SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                    SuggestedFileName = SafeFileName(context.Track),
                };
                picker.FileTypeChoices.Add("PNG 图片", [".png"]);
                InitializeWithWindow.Initialize(picker, context.WindowHandle);

                var file = await picker.PickSaveFileAsync();
                if (file is null)
                {
                    return;
                }

                await FileIO.WriteBytesAsync(file, bytes);
                await ReportOnceAsync();
                context.Notices.Show("歌词海报已保存", NoticeSeverity.Success);
            }
            catch (Exception exception)
            {
                context.Logger?.LogWarning(exception, "保存歌词海报失败");
                context.Notices.Show("保存海报失败，请稍后再试。", NoticeSeverity.Error);
            }
        }

        async Task ReportOnceAsync()
        {
            if (reported)
            {
                return;
            }

            // 先置位再等待：上报失败也不该在下次导出时重试，否则会重复计数。
            reported = true;

            try
            {
                await context.ReportShareAsync();
            }
            catch (Exception exception)
            {
                context.Logger?.LogDebug(exception, "歌词海报分享上报失败");
            }
        }
    }

    /// <summary>「歌手 - 歌名 歌词海报」，替换 Windows 文件名非法字符并限制长度。</summary>
    private static string SafeFileName(Track track)
    {
        var name = $"{track.ArtistText} - {track.Title} 歌词海报";
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(character => invalid.Contains(character) ? '_' : character).ToArray())
            .Trim()
            .TrimEnd('.', ' ');

        return sanitized.Length <= 96 ? sanitized : sanitized[..96];
    }
}
