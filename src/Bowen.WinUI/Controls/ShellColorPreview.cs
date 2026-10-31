using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace Bowen.WinUI.Controls;

/// <summary>
/// 外壳纯色色盘的预览：一张整个界面的小缩略图（标题栏、侧栏、内容卡片、播放条）。
/// </summary>
/// <remarks>
/// 只画示意结构，不截真实窗口；颜色变化时改最外层背景，卡片与文字保持当前主题的颜色，
/// 这样能直观看到所选底色下正文和卡片是否还分得清。
/// </remarks>
internal static class ShellColorPreview
{
    public static (FrameworkElement Root, Action<Color> Apply) Create(ElementTheme theme)
    {
        var dark = theme == ElementTheme.Dark;

        var text = new SolidColorBrush(dark
            ? Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0xE6, 0x00, 0x00, 0x00));
        var muted = new SolidColorBrush(dark
            ? Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x80, 0x00, 0x00, 0x00));
        var surface = new SolidColorBrush(dark
            ? Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
        var chrome = new SolidColorBrush(dark
            ? Color.FromArgb(0x1A, 0x00, 0x00, 0x00)
            : Color.FromArgb(0x0F, 0x00, 0x00, 0x00));

        var root = new Border
        {
            Height = 170,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x12, 0x12, 0x12)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var title = new Grid { Background = chrome };
        title.Children.Add(new Ellipse
        {
            Width = 6,
            Height = 6,
            Fill = surface,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        });
        title.Children.Add(new Border
        {
            Width = 90,
            Height = 6,
            CornerRadius = new CornerRadius(3),
            Background = surface,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        title.Children.Add(new Ellipse
        {
            Width = 6,
            Height = 6,
            Fill = muted,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });
        grid.Children.Add(title);

        var side = new StackPanel
        {
            Padding = new Thickness(6, 8, 6, 0),
            Spacing = 6,
            Background = chrome,
        };
        for (var i = 0; i < 4; i++)
        {
            side.Children.Add(new Border
            {
                Height = 5,
                CornerRadius = new CornerRadius(2.5),
                Background = i == 1 ? text : muted,
                Margin = new Thickness(i == 1 ? 0 : 2, 0, 0, 0),
            });
        }
        Grid.SetRow(side, 1);
        grid.Children.Add(side);

        var content = new StackPanel
        {
            Padding = new Thickness(8),
            Spacing = 6,
        };
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        header.Children.Add(new TextBlock
        {
            Text = "Aa",
            FontSize = 12,
            Foreground = text,
            VerticalAlignment = VerticalAlignment.Center,
        });
        header.Children.Add(new Border
        {
            Width = 70,
            Height = 7,
            CornerRadius = new CornerRadius(3.5),
            Background = text,
            VerticalAlignment = VerticalAlignment.Center,
        });
        content.Children.Add(header);

        for (var i = 0; i < 3; i++)
        {
            var card = new Border
            {
                Height = 26,
                CornerRadius = new CornerRadius(5),
                Background = surface,
                Padding = new Thickness(6),
            };
            var lines = new StackPanel { Spacing = 4 };
            lines.Children.Add(new Border
            {
                Width = 60,
                Height = 4,
                CornerRadius = new CornerRadius(2),
                Background = text,
                HorizontalAlignment = HorizontalAlignment.Left,
            });
            lines.Children.Add(new Border
            {
                Width = 90,
                Height = 3,
                CornerRadius = new CornerRadius(1.5),
                Background = muted,
                HorizontalAlignment = HorizontalAlignment.Left,
            });
            card.Child = lines;
            content.Children.Add(card);
        }
        Grid.SetRow(content, 1);
        Grid.SetColumn(content, 1);
        grid.Children.Add(content);

        var player = new Grid
        {
            Background = chrome,
            Padding = new Thickness(8, 0, 8, 0),
        };
        player.Children.Add(new Border
        {
            Width = 16,
            Height = 16,
            CornerRadius = new CornerRadius(3),
            Background = surface,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var transport = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        for (var i = 0; i < 4; i++)
        {
            transport.Children.Add(new Ellipse
            {
                Width = i == 1 ? 9 : 5,
                Height = i == 1 ? 9 : 5,
                Fill = i == 1 ? text : muted,
            });
        }
        player.Children.Add(transport);
        player.Children.Add(new Border
        {
            Width = 22,
            Height = 5,
            CornerRadius = new CornerRadius(2.5),
            Background = muted,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetRow(player, 2);
        Grid.SetColumnSpan(player, 2);
        grid.Children.Add(player);

        root.Child = grid;

        return (root, color => root.Background = new SolidColorBrush(color));
    }
}
