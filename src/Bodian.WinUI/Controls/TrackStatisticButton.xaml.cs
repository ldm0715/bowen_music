using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Controls;

/// <summary>收藏 / 分享预留按钮；只显示图标与统计角标，不触发读写或分享动作。</summary>
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

    public string Glyph { get => (string)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string BadgeText { get => (string)GetValue(BadgeTextProperty); set => SetValue(BadgeTextProperty, value); }
    public bool HasBadgeCount { get => (bool)GetValue(HasBadgeCountProperty); set => SetValue(HasBadgeCountProperty, value); }
}
