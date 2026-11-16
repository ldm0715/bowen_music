using Bowen.WinUI.Services;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
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

        var dark = theme == ElementTheme.Dark
            || (theme == ElementTheme.Default
                && Application.Current.RequestedTheme == ApplicationTheme.Dark);

        var imageA = CreatePreviewImage();
        var imageB = CreatePreviewImage();
        imageB.Opacity = 0;
        imageB.Visibility = Visibility.Collapsed;

        var ring = new ProgressRing
        {
            IsActive = true,
            Width = 32,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var status = new TextBlock
        {
            Text = "正在生成海报…",
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };

        // 加载圈和提示文字一起压在图片上方居中，避免单独占一行。
        var loadingPanel = new StackPanel
        {
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        loadingPanel.Children.Add(ring);
        loadingPanel.Children.Add(status);

        var imageHost = new Grid { MinHeight = 140 };
        imageHost.Children.Add(imageA);
        imageHost.Children.Add(imageB);
        imageHost.Children.Add(loadingPanel);

        var toggle = new Button
        {
            Width = 32,
            Height = 32,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Content = IconGeometry.CreatePathIcon(dark ? "IconThemeLight" : "IconThemeDark"),
        };
        ToolTipService.SetToolTip(toggle, dark ? "切换为浅色海报" : "切换为深色海报");

        var title = new TextBlock
        {
            Text = kind == MonthPosterKind.Overview ? "月度总览海报" : "歌曲总览海报",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var header = new Grid { ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(title, 0);
        Grid.SetColumn(toggle, 1);
        header.Children.Add(title);
        header.Children.Add(toggle);

        var stack = new StackPanel { Spacing = 12, MinWidth = 320 };
        stack.Children.Add(header);
        stack.Children.Add(imageHost);

        var viewer = new CommentImageViewer
        {
            Title = "月报海报预览",
            Visibility = Visibility.Collapsed,
        };

        var root = new Grid();
        root.Children.Add(stack);
        root.Children.Add(viewer);

        var dialog = AppDialogs.Create(null, xamlRoot, theme, maxWidth: 520);
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

        ToolTipService.SetToolTip(imageA, "点击放大预览");
        ToolTipService.SetToolTip(imageB, "点击放大预览");
        imageA.Tapped += (sender, _) => ShowViewer(((Image)sender).Source);
        imageB.Tapped += (sender, _) => ShowViewer(((Image)sender).Source);
        viewer.CloseRequested += (_, _) =>
        {
            viewer.Visibility = Visibility.Collapsed;
            viewer.ImageSource = null;
        };

        void ShowViewer(ImageSource? source)
        {
            if (source is null)
            {
                return;
            }

            viewer.ImageSource = source;
            viewer.Visibility = Visibility.Visible;
            viewer.FocusCloseButton();
        }

        void Crossfade(ImageSource source)
        {
            if (imageA.Source is null)
            {
                imageA.Source = source;
                imageA.Opacity = 0;
                Fade(imageA, 0, 1, 180, () => imageA.Opacity = 1);
            }
            else
            {
                imageB.Source = source;
                imageB.Opacity = 0;
                imageB.Visibility = Visibility.Visible;
                imageA.Opacity = 1;

                var storyboard = new Storyboard();
                var fadeIn = new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = TimeSpan.FromMilliseconds(220),
                };
                Storyboard.SetTarget(fadeIn, imageB);
                Storyboard.SetTargetProperty(fadeIn, "Opacity");
                var fadeOut = new DoubleAnimation
                {
                    From = 1,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(220),
                };
                Storyboard.SetTarget(fadeOut, imageA);
                Storyboard.SetTargetProperty(fadeOut, "Opacity");
                storyboard.Children.Add(fadeIn);
                storyboard.Children.Add(fadeOut);
                storyboard.Completed += (_, _) =>
                {
                    imageA.Source = source;
                    imageA.Opacity = 1;
                    imageB.Visibility = Visibility.Collapsed;
                    imageB.Source = null;
                    imageB.Opacity = 0;
                    if (viewer.Visibility == Visibility.Visible)
                    {
                        viewer.ImageSource = source;
                    }
                };
                storyboard.Begin();
            }

            if (viewer.Visibility == Visibility.Visible)
            {
                viewer.ImageSource = source;
            }
        }

        byte[]? png = null;
        var generation = 0;
        var rendering = false;

        async Task RenderAsync()
        {
            var current = ++generation;
            rendering = true;
            toggle.IsEnabled = false;
            loadingPanel.Visibility = Visibility.Visible;
            ring.IsActive = true;
            ring.Visibility = Visibility.Visible;
            status.Text = "正在生成海报…";
            status.Visibility = Visibility.Visible;
            dialog.IsPrimaryButtonEnabled = false;
            dialog.IsSecondaryButtonEnabled = false;

            try
            {
                var bytes = kind == MonthPosterKind.Overview
                    ? await MonthOverviewPosterRenderer.RenderAsync(content, dark)
                    : await MonthTracksPosterRenderer.RenderAsync(content, dark);
                if (current != generation) return;

                var bitmap = await LoadBitmapAsync(bytes);
                if (current != generation) return;

                png = bytes;
                Crossfade(bitmap);
                loadingPanel.Visibility = Visibility.Collapsed;
                ring.IsActive = false;
                dialog.IsPrimaryButtonEnabled = true;
                dialog.IsSecondaryButtonEnabled = true;
            }
            catch (Exception exception)
            {
                if (current != generation) return;
                logger?.LogWarning(exception, "月报海报渲染失败");
                ring.IsActive = false;
                ring.Visibility = Visibility.Collapsed;
                status.Text = "海报生成失败，请稍后再试。";
                status.Visibility = Visibility.Visible;
                loadingPanel.Visibility = Visibility.Visible;
                dialog.IsPrimaryButtonEnabled = png is not null;
                dialog.IsSecondaryButtonEnabled = png is not null;
            }
            finally
            {
                if (current == generation)
                {
                    rendering = false;
                    toggle.IsEnabled = true;
                }
            }
        }

        toggle.Click += async (_, _) =>
        {
            if (rendering) return;
            dark = !dark;
            toggle.Content = IconGeometry.CreatePathIcon(dark ? "IconThemeLight" : "IconThemeDark");
            ToolTipService.SetToolTip(toggle, dark ? "切换为浅色海报" : "切换为深色海报");
            await RenderAsync();
        };

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

        dialog.Opened += (_, _) => _ = RenderAsync();
        await dialog.ShowAsync();
    }

    private static string SafeFileName(MonthPosterContent content, MonthPosterKind kind)
        => $"音波回忆_{content.Year}{content.Month:00}_{(kind == MonthPosterKind.Overview ? "月度总览" : "歌曲总览")}";

    private static Image CreatePreviewImage() => new()
    {
        Stretch = Stretch.Uniform,
        MaxWidth = 300,
        MaxHeight = 520,
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    private static void Fade(UIElement element, double from, double to, int milliseconds, Action completed)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromMilliseconds(milliseconds),
        };
        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, "Opacity");
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Completed += (_, _) => completed();
        storyboard.Begin();
    }

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
