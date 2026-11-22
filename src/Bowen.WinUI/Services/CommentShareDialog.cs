using Bowen.WinUI.Controls;
using Bowen.WinUI.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI;
using WinRT.Interop;

namespace Bowen.WinUI.Services;

/// <summary>打开评论分享卡片对话框所需的一切。</summary>
internal sealed record CommentShareContext(
    CommentPosterContent Content,
    XamlRoot XamlRoot,
    ElementTheme Theme,
    nint WindowHandle,
    IClipboardService Clipboard,
    INoticeSink Notices,
    FontSettingsViewModel FontSettings,
    ILogger? Logger);

/// <summary>评论分享卡片：打开即渲染，预览后保存或复制。</summary>
/// <remarks>
/// 与歌词海报一样只认最新一次渲染结果；渲染期间两个导出按钮都禁用。
/// 卡片主题默认跟随应用主题，预览页右上角可以单独切换，不影响应用主题。
/// 切换主题时旧图保持不动，新图渲染完成后在原位置做淡入淡出，窗口不重新布局。
/// </remarks>
internal static class CommentShareDialog
{
    private static readonly LyricPosterStyle LightStyle = LyricPosterStyle.Default;
    private static readonly LyricPosterStyle DarkStyle = new(
        string.Empty,
        Color.FromArgb(0xFF, 0x1A, 0x1A, 0x1A),
        Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF),
        UseLightLogo: true);

    public static async Task ShowAsync(CommentShareContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var imageA = CreatePreviewImage();
        var imageB = CreatePreviewImage();
        imageB.Opacity = 0;
        imageB.Visibility = Visibility.Collapsed;

        var progress = new ProgressRing
        {
            Width = 28,
            Height = 28,
            IsActive = true,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var status = new TextBlock
        {
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };

        var imageHost = new Grid { MinHeight = 140 };
        imageHost.Children.Add(imageA);
        imageHost.Children.Add(imageB);
        imageHost.Children.Add(progress);

        var toggle = BuildThemeToggle();
        var fontPicker = new FontPicker
        {
            ItemsSource = context.FontSettings.PosterFontOptions,
            SelectedIndex = 0,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(fontPicker, "海报字体");
        ToolTipService.SetToolTip(fontPicker, "海报字体");

        var header = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 0, 0, 4) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new TextBlock
        {
            Text = "分享评论",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(title, 0);
        Grid.SetColumn(fontPicker, 1);
        Grid.SetColumn(toggle, 2);
        header.Children.Add(title);
        header.Children.Add(fontPicker);
        header.Children.Add(toggle);

        var content = new StackPanel { Spacing = 14 };
        content.Children.Add(header);
        content.Children.Add(imageHost);
        content.Children.Add(status);

        var viewer = new CommentImageViewer { Visibility = Visibility.Collapsed };
        var root = new Grid();
        root.Children.Add(content);
        root.Children.Add(viewer);

        // 标题放进内容里，右上角才有位置摆主题切换按钮。
        var dialog = Controls.AppDialogs.Create(null, context.XamlRoot, context.Theme, maxWidth: 520);
        // 点预览放大时要能撑开，不能被对话框模板的默认最大宽度卡住。
        dialog.Resources["ContentDialogMaxWidth"] = 4000d;
        dialog.Resources["ContentDialogMaxHeight"] = 4000d;
        dialog.Content = root;
        dialog.PrimaryButtonText = "保存图片";
        dialog.SecondaryButtonText = "复制图片";
        dialog.CloseButtonText = "关闭";
        dialog.IsPrimaryButtonEnabled = false;
        dialog.IsSecondaryButtonEnabled = false;

        if (Application.Current.Resources.TryGetValue("AccentButtonStyle", out var accent) && accent is Style accentStyle)
        {
            dialog.PrimaryButtonStyle = accentStyle;
        }

        ToolTipService.SetToolTip(imageA, "点击放大预览");
        ToolTipService.SetToolTip(imageB, "点击放大预览");
        imageA.Tapped += (_, _) => ShowViewer(imageA.Source);
        imageB.Tapped += (_, _) => ShowViewer(imageB.Source);
        viewer.CloseRequested += (_, _) => CloseViewer();

        var dark = context.Theme == ElementTheme.Dark;
        var selectedFont = context.FontSettings.CurrentFontFamily;
        byte[]? latest = null;
        var rendered = false;
        var rendering = false;
        var generation = 0;

        UpdateToggle();

        dialog.Opened += async (_, _) => await RenderAsync();

        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            if (!rendered || latest is null) return;
            await SaveAsync(latest);
        };

        dialog.SecondaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            if (!rendered || latest is null) return;
            await CopyAsync(latest);
        };

        toggle.Click += async (_, _) =>
        {
            if (rendering) return;
            dark = !dark;
            UpdateToggle();
            await RenderAsync();
        };

        fontPicker.RegisterPropertyChangedCallback(
            FontPicker.SelectedIndexProperty,
            (_, _) =>
            {
                if (rendering) return;

                var index = fontPicker.SelectedIndex;
                selectedFont = index <= 0
                    ? context.FontSettings.CurrentFontFamily
                    : context.FontSettings.PosterFontOptions[index].FamilyName;

                _ = RenderAsync();
            });

        await dialog.ShowAsync();

        void UpdateToggle()
        {
            toggle.Content = IconGeometry.CreatePathIcon(dark ? "IconThemeLight" : "IconThemeDark");
            ToolTipService.SetToolTip(toggle, dark ? "切换为浅色卡片" : "切换为深色卡片");
        }

        void UpdateButtons()
        {
            dialog.IsPrimaryButtonEnabled = rendered && !rendering;
            dialog.IsSecondaryButtonEnabled = rendered && !rendering;
        }

        async Task RenderAsync()
        {
            var current = ++generation;
            rendering = true;
            toggle.IsEnabled = false;
            fontPicker.IsEnabled = false;
            progress.IsActive = true;
            progress.Visibility = Visibility.Visible;
            status.Visibility = Visibility.Collapsed;
            UpdateButtons();

            try
            {
                var png = await CommentPosterRenderer.RenderAsync(
                    context.Content,
                    dark ? DarkStyle : LightStyle,
                    selectedFont,
                    context.FontSettings.CurrentFontFamily);
                var bitmap = await LoadBitmapAsync(png);
                if (current != generation) return;

                latest = png;
                rendered = true;
                Crossfade(bitmap);
                progress.IsActive = false;
                progress.Visibility = Visibility.Collapsed;
                status.Visibility = Visibility.Collapsed;
            }
            catch (CommentPosterOverflowException exception)
            {
                if (current != generation) return;
                Fail(exception.Message);
            }
            catch (Exception exception)
            {
                context.Logger?.LogWarning(exception, "评论分享卡片渲染失败：{Title}", context.Content.Track.Title);
                if (current != generation) return;
                Fail("卡片生成失败，请稍后再试。");
            }
            finally
            {
                if (current == generation)
                {
                    rendering = false;
                    toggle.IsEnabled = true;
                    fontPicker.IsEnabled = true;
                    UpdateButtons();
                }
            }
        }

        /// <summary>新图压在旧图上淡入，旧图同时淡出；完成后再把旧图换成新图。</summary>
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
                var fadeIn = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(220) };
                Storyboard.SetTarget(fadeIn, imageB);
                Storyboard.SetTargetProperty(fadeIn, "Opacity");
                var fadeOut = new DoubleAnimation { From = 1, To = 0, Duration = TimeSpan.FromMilliseconds(220) };
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
                    if (viewer.Visibility == Visibility.Visible) viewer.ImageSource = source;
                };
                storyboard.Begin();
            }

            if (viewer.Visibility == Visibility.Visible) viewer.ImageSource = source;
        }

        void Fail(string message)
        {
            rendered = false;
            latest = null;
            progress.IsActive = false;
            progress.Visibility = Visibility.Collapsed;
            status.Text = message;
            status.Visibility = Visibility.Visible;
            if (Application.Current.Resources.TryGetValue("SystemFillColorCriticalBrush", out var brush)
                && brush is Brush errorBrush)
            {
                status.Foreground = errorBrush;
            }
        }

        void ShowViewer(ImageSource? source)
        {
            if (source is not BitmapImage { PixelWidth: > 0 } bitmap) return;

            var screen = context.XamlRoot.Size;
            var aspect = Math.Clamp(bitmap.PixelWidth / (double)bitmap.PixelHeight, 0.25, 1.2);
            var contentHeight = Math.Max(420, screen.Height - 160);
            var viewportHeight = Math.Max(300, contentHeight - 152);
            var contentWidth = Math.Max(360, (viewportHeight * aspect) + 64);

            root.Width = Math.Min(screen.Width - 80, contentWidth);
            root.Height = contentHeight;
            viewer.Title = "评论卡片";
            viewer.ImageSource = source;
            viewer.Visibility = Visibility.Visible;
            dialog.DispatcherQueue.TryEnqueue(viewer.FocusCloseButton);
        }

        void CloseViewer()
        {
            viewer.Visibility = Visibility.Collapsed;
            viewer.ImageSource = null;
            root.Width = double.NaN;
            root.Height = double.NaN;
        }

        async Task CopyAsync(byte[] png)
        {
            try
            {
                var text = context.Content.ShareLink;
                if (string.IsNullOrWhiteSpace(text))
                {
                    await context.Clipboard.SetImageAsync(png);
                }
                else
                {
                    await context.Clipboard.SetImageAndTextAsync(png, text);
                }

                context.Notices.Show("卡片已复制到剪贴板", NoticeSeverity.Success);
            }
            catch (Exception exception)
            {
                context.Logger?.LogWarning(exception, "复制评论分享卡片失败");
                context.Notices.Show("复制卡片失败，请稍后再试。", NoticeSeverity.Error);
            }
        }

        async Task SaveAsync(byte[] png)
        {
            try
            {
                var picker = new FileSavePicker
                {
                    SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                    SuggestedFileName = SafeFileName(context.Content),
                };
                picker.FileTypeChoices.Add("PNG 图片", new List<string> { ".png" });
                InitializeWithWindow.Initialize(picker, context.WindowHandle);

                var file = await picker.PickSaveFileAsync();
                if (file is null) return;

                await FileIO.WriteBytesAsync(file, png);
                context.Notices.Show("评论卡片已保存", NoticeSeverity.Success);
            }
            catch (Exception exception)
            {
                context.Logger?.LogWarning(exception, "保存评论分享卡片失败");
                context.Notices.Show("保存卡片失败，请稍后再试。", NoticeSeverity.Error);
            }
        }
    }

    private static Image CreatePreviewImage() => new()
    {
        Stretch = Stretch.Uniform,
        MaxWidth = 420,
        MaxHeight = 460,
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    private static Button BuildThemeToggle()
    {
        var button = new Button
        {
            Width = 32,
            Height = 32,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (Application.Current.Resources.TryGetValue("SubtleButtonStyle", out var subtle) && subtle is Style style)
        {
            button.Style = style;
        }

        AutomationProperties.SetName(button, "切换卡片主题");
        return button;
    }

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

    private static string SafeFileName(CommentPosterContent content)
    {
        var name = $"{content.Track.Title} 评论卡片";
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(character => invalid.Contains(character) ? '_' : character).ToArray())
            .Trim()
            .TrimEnd('.', ' ');

        return sanitized.Length <= 96 ? sanitized : sanitized[..96];
    }
}
