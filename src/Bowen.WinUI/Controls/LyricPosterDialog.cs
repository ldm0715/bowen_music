using Bowen.Core.Models;
using Bowen.Core.Models.Lyrics;
using Bowen.WinUI.Services;
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
    ILogger? Logger);

/// <summary>
/// 歌词海报对话框：先选句，再看预览，最后保存或复制。
/// </summary>
/// <remarks>
/// <para>
/// <b>两步放在同一个 ContentDialog 里。</b> 预览的「返回修改」只是切回第一步，
/// 勾选状态由 <see cref="LyricPosterDialogContent"/> 保存，不会丢。
/// </para>
/// <para>
/// <b>分享数只在第一次成功导出时上报一次。</b> 用户反复保存 / 复制同一张海报
/// 不应该把分享数刷上去；这个标记跟着对话框走。
/// </para>
/// </remarks>
internal static class LyricPosterDialog
{
    public static async Task ShowAsync(LyricPosterDialogContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var content = new LyricPosterDialogContent(context);

        var dialog = AppDialogs.Create("制作歌词海报", context.XamlRoot, context.Theme, maxWidth: 600);
        dialog.Content = content;
        dialog.PrimaryButtonText = "生成海报";
        dialog.CloseButtonText = "取消";
        dialog.DefaultButton = ContentDialogButton.Primary;
        dialog.IsPrimaryButtonEnabled = content.SelectedCount > 0;

        var previewing = false;
        var reported = false;
        byte[]? png = null;

        content.SelectionChanged += (_, _) =>
        {
            if (!previewing)
            {
                dialog.IsPrimaryButtonEnabled = content.SelectedCount > 0;
            }
        };

        dialog.PrimaryButtonClick += async (_, args) =>
        {
            // 两个阶段都留在对话框里：生成后看预览，保存后还要能继续复制。
            args.Cancel = true;

            if (!previewing)
            {
                await GeneratePreviewAsync();
                return;
            }

            if (png is not null)
            {
                await SaveAsync(png);
            }
        };

        dialog.SecondaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;

            if (!previewing || png is null)
            {
                return;
            }

            try
            {
                await context.Clipboard.SetImageAndTextAsync(png, context.ShareLink);
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
                return;
            }

            // 预览阶段的「返回修改」：保留勾选，回到第一步。
            args.Cancel = true;
            previewing = false;
            png = null;
            content.ShowSelectionStage();
            dialog.PrimaryButtonText = "生成海报";
            dialog.SecondaryButtonText = "";
            dialog.CloseButtonText = "取消";
            dialog.IsPrimaryButtonEnabled = content.SelectedCount > 0;
        };

        await dialog.ShowAsync();

        async Task GeneratePreviewAsync()
        {
            dialog.IsPrimaryButtonEnabled = false;
            content.SetBusy(true);

            try
            {
                png = await content.RenderPreviewAsync(new LyricPosterContent(
                    context.Track,
                    context.ShareLink,
                    content.SelectedLines));

                previewing = true;
                content.ShowPreviewStage();
                dialog.PrimaryButtonText = "保存图片";
                dialog.SecondaryButtonText = "复制图片";
                dialog.CloseButtonText = "返回修改";
            }
            catch (LyricPosterOverflowException)
            {
                png = null;
                content.ShowMessage("歌词太多，一页放不下，请减少勾选的歌词。");
            }
            catch (Exception exception)
            {
                png = null;
                context.Logger?.LogWarning(exception, "生成歌词海报失败");
                content.ShowMessage("海报生成失败，请稍后再试。");
            }
            finally
            {
                content.SetBusy(false);
                dialog.IsPrimaryButtonEnabled = true;
            }
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
