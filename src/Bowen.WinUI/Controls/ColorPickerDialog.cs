using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Bowen.WinUI.Controls;

/// <summary>
/// 纯色背景的 HSV 色盘弹窗。歌词页与外壳共用，预览与提示由调用方决定。
/// </summary>
/// <remarks>
/// <para>
/// <b>预览不是固定的。</b> 歌词页要看到歌词在底色上的效果；外壳要看整个界面的缩略图。
/// 调用方传进来一个已经搭好的 <paramref name="preview"/> 和一个只改背景色的回调，
/// 弹窗不关心预览里画的是什么。
/// </para>
/// <para>
/// <b>提示文案也由调用方给。</b> 歌词页永远是白字，深色底才安全；外壳的正文颜色跟随主题，
/// 两种主题下「偏亮」与「偏暗」的含义不同。
/// </para>
/// <para>
/// 真正的色盘 UI 在 <see cref="ColorPickerPanel"/>。这里只负责套一层 ContentDialog；
/// 需要挂在另一个 ContentDialog 之上时改用 <see cref="ColorPickerFlyout"/>。
/// </para>
/// </remarks>
internal static class ColorPickerDialog
{
    /// <summary>
    /// 打开色盘。
    /// </summary>
    /// <param name="current">初始颜色。</param>
    /// <param name="xamlRoot">弹窗挂载的 XamlRoot。</param>
    /// <param name="theme">弹窗主题。</param>
    /// <param name="preview">调用方搭好的预览元素。</param>
    /// <param name="applyPreview">颜色变化时更新预览；只改背景即可。</param>
    /// <param name="hintText">颜色变化时返回对比度提示。</param>
    /// <param name="logger">可选日志。</param>
    /// <param name="title">对话框标题。</param>
    /// <returns>确定时返回 <c>0xAARRGGBB</c>；取消返回 <c>null</c>。</returns>
    public static async Task<uint?> ShowAsync(
        Color current,
        XamlRoot xamlRoot,
        ElementTheme theme,
        FrameworkElement preview,
        Action<Color> applyPreview,
        Func<Color, string> hintText,
        ILogger? logger = null,
        string title = "背景颜色")
    {
        ArgumentNullException.ThrowIfNull(xamlRoot);

        var panel = new ColorPickerPanel(current, preview, applyPreview, hintText);

        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = xamlRoot,
                Title = title,
                Content = panel.Content,
                PrimaryButtonText = "确定",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                RequestedTheme = theme,
            };
            dialog.IsPrimaryButtonEnabled = panel.IsValid;
            panel.ValidityChanged += (_, _) => dialog.IsPrimaryButtonEnabled = panel.IsValid;

            if (Application.Current.Resources.TryGetValue("BowenAcrylicBrush", out var brush) && brush is Brush acrylic)
            {
                dialog.Resources["ContentDialogBackground"] = acrylic;
            }

            var response = await dialog.ShowAsync();
            return response == ContentDialogResult.Primary ? panel.Result : null;
        }
        finally
        {
            panel.Dispose();
        }
    }

    /// <summary>相对亮度，0..1。供调用方判断底色与正文的对比。</summary>
    internal static double RelativeLuminance(Color color)
        => ((0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B)) / 255;
}
