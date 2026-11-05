using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Bowen.WinUI.Controls;

/// <summary>
/// 用 Flyout 承载 <see cref="ColorPickerPanel"/>。
/// </summary>
/// <remarks>
/// <b>海报对话框已经是 ContentDialog，不能再弹第二个 ContentDialog。</b>
/// 自定义颜色因此改走 Flyout：它只是 popup，可以正常覆盖在对话框之上。
/// 歌词页 / 设置页没有这个限制，继续用 <see cref="ColorPickerDialog"/>。
/// </remarks>
internal static class ColorPickerFlyout
{
    public static async Task<uint?> ShowAsync(
        FrameworkElement anchor,
        Color current,
        ElementTheme theme,
        FrameworkElement preview,
        Action<Color> applyPreview,
        Func<Color, string> hintText,
        string title = "自定义颜色",
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(anchor);

        var panel = new ColorPickerPanel(current, preview, applyPreview, hintText);
        var completion = new TaskCompletionSource<uint?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var confirmed = false;

        try
        {
            var titleText = new TextBlock
            {
                Text = title,
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            };

            var cancel = new Button { Content = "取消" };
            var confirm = new Button { Content = "确定" };
            if (Application.Current.Resources.TryGetValue("AccentButtonStyle", out var accent)
                && accent is Style accentStyle)
            {
                confirm.Style = accentStyle;
            }

            confirm.IsEnabled = panel.IsValid;
            panel.ValidityChanged += (_, _) => confirm.IsEnabled = panel.IsValid;

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            buttons.Children.Add(cancel);
            buttons.Children.Add(confirm);

            var content = new StackPanel { Spacing = 12, RequestedTheme = theme };
            content.Children.Add(titleText);
            content.Children.Add(panel.Content);
            content.Children.Add(buttons);

            var flyout = new Flyout
            {
                Content = content,
                Placement = FlyoutPlacementMode.Top,
            };

            var presenterStyle = new Style(typeof(FlyoutPresenter));
            presenterStyle.Setters.Add(new Setter(FlyoutPresenter.MinWidthProperty, 0d));
            presenterStyle.Setters.Add(new Setter(FlyoutPresenter.MaxWidthProperty, 540d));
            presenterStyle.Setters.Add(new Setter(FlyoutPresenter.PaddingProperty, new Thickness(16)));
            presenterStyle.Setters.Add(new Setter(FlyoutPresenter.CornerRadiusProperty, new CornerRadius(8)));
            if (Application.Current.Resources.TryGetValue("BowenAcrylicBrush", out var brush) && brush is Brush acrylic)
            {
                presenterStyle.Setters.Add(new Setter(FlyoutPresenter.BackgroundProperty, acrylic));
            }

            flyout.FlyoutPresenterStyle = presenterStyle;

            flyout.Closed += (_, _) =>
            {
                if (!confirmed)
                {
                    completion.TrySetResult(null);
                }

                panel.Dispose();
            };

            cancel.Click += (_, _) => flyout.Hide();
            confirm.Click += (_, _) =>
            {
                confirmed = true;
                completion.TrySetResult(panel.Result);
                flyout.Hide();
            };

            flyout.ShowAt(anchor);
            return await completion.Task;
        }
        catch (Exception exception)
        {
            logger?.LogWarning(exception, "打开自定义颜色面板失败");
            panel.Dispose();
            completion.TrySetResult(null);
            return await completion.Task;
        }
    }
}
