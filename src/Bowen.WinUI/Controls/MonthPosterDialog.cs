using Bowen.WinUI.Services;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace Bowen.WinUI.Controls;

/// <summary>月报海报的两种版式。</summary>
public enum MonthPosterKind
{
    /// <summary>封面墙 + 本月统计。</summary>
    Overview,

    /// <summary>播放最多的前 10 首小票。</summary>
    Tracks,
}

/// <summary>
/// 月报海报的预览与导出。
/// </summary>
/// <remarks>
/// 打开对话框后立即开始渲染，渲染完成前只显示转圈；保存与复制只认 <c>png</c>，
/// 与歌词 / 评论海报同一套口径。
/// </remarks>
internal static class MonthPosterDialog
{
    public static async Task ShowAsync(
        MonthPosterContent content,
        MonthPosterKind kind,
        XamlRoot xamlRoot,
        ElementTheme theme,
        nint windowHandle,
        IClipboardService clipboard,
        INoticeSink notices,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(content);

        var image = new Image
        {
            Stretch = Stretch.Uniform,
            MaxWidth = 300,
            MaxHeight = 520,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var ring = new ProgressRing
        {
            IsActive = true,
            Width = 48,
            Height = 48,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var status = new TextBlock
        {
            Text = "正在生成海报…",
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var stack = new StackPanel { Spacing = 12, MinWidth = 320 };
        stack.Children.Add(ring);
        stack.Children.Add(image);
        stack.Children.Add(status);

        var viewer = new CommentImageViewer
        {
            Title = "月报海报预览",
            Visibility = Visibility.Collapsed,
        };

        var root = new Grid();
        root.Children.Add(stack);
        root.Children.Add(viewer);

        var dialog = AppDialogs.Create(
            kind == MonthPosterKind.Overview ? "月度总览海报" : "歌曲总览海报",
            xamlRoot, theme, maxWidth: 520);
        dialog.Resources["ContentDialogMaxWidth"] = 1200d;
        dialog.Resources["ContentDialogMaxHeight"] = 900d;
        dialog.Content = root;
        dialog.PrimaryButtonText = "保存图片";
        dialog.SecondaryButtonText = "复制图片";
        dialog.CloseButtonText = "关闭";
        dialog.IsPrimaryButtonEnabled = false;
        dialog.IsSecondaryButtonEnabled = false;

        if (Application.Current.Resources.TryGetValue("AccentButtonStyle", out var accent)
            && accent is Style accentStyle)
        {
            dialog.PrimaryButtonStyle = accentStyle;
        }

        ToolTipService.SetToolTip(image, "点击放大预览");
        image.Tapped += (_, _) =>
        {
            if (image.Source is not { } source)
            {
                return;
            }

            viewer.ImageSource = source;
            viewer.Visibility = Visibility.Visible;
            viewer.FocusCloseButton();
        };
        viewer.CloseRequested += (_, _) =>
        {
            viewer.Visibility = Visibility.Collapsed;
            viewer.ImageSource = null;
        };

        byte[]? png = null;

        async Task RenderAsync()
        {
            try
            {
                png = kind == MonthPosterKind.Overview
                    ? await MonthOverviewPosterRenderer.RenderAsync(content)
                    : await MonthTracksPosterRenderer.RenderAsync(content);

                image.Source = await LoadBitmapAsync(png);
                ring.IsActive = false;
                ring.Visibility = Visibility.Collapsed;
                status.Text = "";
                dialog.IsPrimaryButtonEnabled = true;
                dialog.IsSecondaryButtonEnabled = true;
            }
            catch (Exception exception)
            {
                logger?.LogWarning(exception, "月报海报渲染失败");
                ring.IsActive = false;
                ring.Visibility = Visibility.Collapsed;
                status.Text = "海报生成失败，请稍后再试。";
            }
        }

        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;

            if (png is null)
            {
                return;
            }

            try
            {
                var picker = new FileSavePicker
                {
                    SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                    SuggestedFileName = SafeFileName(content, kind),
                };
                picker.FileTypeChoices.Add("PNG 图片", new List<string> { ".png" });
                InitializeWithWindow.Initialize(picker, windowHandle);

                var file = await picker.PickSaveFileAsync();
                if (file is null)
                {
                    return;
                }

                await FileIO.WriteBytesAsync(file, png);
                notices.Show("海报已保存", NoticeSeverity.Success);
            }
            catch (Exception exception)
            {
                logger?.LogWarning(exception, "保存月报海报失败");
                notices.Show("保存海报失败，请稍后再试。", NoticeSeverity.Error);
            }
        };

        dialog.SecondaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;

            if (png is null)
            {
                return;
            }

            try
            {
                await clipboard.SetImageAsync(png);
                notices.Show("海报已复制到剪贴板", NoticeSeverity.Success);
            }
            catch (Exception exception)
            {
                logger?.LogWarning(exception, "复制月报海报失败");
                notices.Show("复制海报失败，请稍后再试。", NoticeSeverity.Error);
            }
        };

        var showOperation = dialog.ShowAsync();
        _ = RenderAsync();
        await showOperation;
    }

    private static string SafeFileName(MonthPosterContent content, MonthPosterKind kind)
        => $"音波回忆_{content.Year}{content.Month:00}_{(kind == MonthPosterKind.Overview ? "月度总览" : "歌曲总览")}";

    private static async Task<BitmapImage> LoadBitmapAsync(byte[] png)
    {
        var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(png);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }

        stream.Seek(0);
        var bitmap = new BitmapImage();
        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }
}
