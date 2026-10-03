using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Controls;

/// <summary>
/// 曲目的统计入口：图标 + 数量角标，可绑定命令。
/// </summary>
/// <remarks>
/// 图标有「未激活 / 已激活」两态：<see cref="Glyph"/> 与 <see cref="ActiveGlyph"/>，由
/// <see cref="IsActive"/> 切换。分享按钮不分状态，不传 <see cref="ActiveGlyph"/> 即可。
/// </remarks>
public sealed partial class TrackStatisticButton : UserControl
{
    public TrackStatisticButton() => InitializeComponent();

    private static void OnDisplayChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((TrackStatisticButton)sender).Bindings.Update();

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(TrackStatisticButton), new PropertyMetadata("", OnDisplayChanged));
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(TrackStatisticButton), new PropertyMetadata("", OnDisplayChanged));
    public static readonly DependencyProperty BadgeTextProperty = DependencyProperty.Register(
        nameof(BadgeText), typeof(string), typeof(TrackStatisticButton), new PropertyMetadata("", OnDisplayChanged));
    public static readonly DependencyProperty HasBadgeCountProperty = DependencyProperty.Register(
        nameof(HasBadgeCount), typeof(bool), typeof(TrackStatisticButton), new PropertyMetadata(false, OnDisplayChanged));

    public static readonly DependencyProperty ActiveGlyphProperty = DependencyProperty.Register(
        nameof(ActiveGlyph), typeof(string), typeof(TrackStatisticButton), new PropertyMetadata("", OnDisplayChanged));
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(TrackStatisticButton), new PropertyMetadata(false, OnDisplayChanged));
    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
        nameof(Command), typeof(ICommand), typeof(TrackStatisticButton), new PropertyMetadata(null, OnDisplayChanged));
    public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.Register(
        nameof(CommandParameter), typeof(object), typeof(TrackStatisticButton), new PropertyMetadata(null, OnDisplayChanged));

    public string Glyph { get => (string)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string BadgeText { get => (string)GetValue(BadgeTextProperty); set => SetValue(BadgeTextProperty, value); }
    public bool HasBadgeCount { get => (bool)GetValue(HasBadgeCountProperty); set => SetValue(HasBadgeCountProperty, value); }

    /// <summary>已激活时的图标；留空则两态长得一样。</summary>
    public string ActiveGlyph { get => (string)GetValue(ActiveGlyphProperty); set => SetValue(ActiveGlyphProperty, value); }

    /// <summary>已激活状态。喜欢按钮绑的是「是否已喜欢」。</summary>
    public bool IsActive { get => (bool)GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }

    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
    public object? CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }
}
