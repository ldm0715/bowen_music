using Bodian.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Bodian.WinUI.Controls;

/// <summary>音质档位的彩色胶囊：标准灰 / HQ 蓝 / SQ 金。</summary>
/// <remarks>
/// 播放条与歌词页的音质按钮、音质弹层里的每一行都用它，三处观感天然一致。
/// 配色见 <c>Themes/Quality.xaml</c>，与会员徽标同属「档位色不是主题色面」那一类。
/// </remarks>
public sealed partial class AudioQualityChip : UserControl
{
    public AudioQualityChip() => InitializeComponent();

    private static void OnDisplayChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((AudioQualityChip)sender).Bindings.Update();

    public static readonly DependencyProperty QualityProperty = DependencyProperty.Register(
        nameof(Quality), typeof(AudioQuality?), typeof(AudioQualityChip),
        new PropertyMetadata(null, OnChipChanged));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(AudioQualityChip), new PropertyMetadata("", OnDisplayChanged));

    public static readonly DependencyProperty IsCurrentProperty = DependencyProperty.Register(
        nameof(IsCurrent), typeof(bool), typeof(AudioQualityChip), new PropertyMetadata(true, OnChipChanged));

    /// <summary>要显示的档位。为空（还没取到音源）时只出文字不出胶囊底。</summary>
    public AudioQuality? Quality
    {
        get => (AudioQuality?)GetValue(QualityProperty);
        set => SetValue(QualityProperty, value);
    }

    /// <summary>胶囊里的文字，通常是档位名。</summary>
    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>是否当前档。非当前档淡一档，一眼看出正在用哪一档。</summary>
    public bool IsCurrent
    {
        get => (bool)GetValue(IsCurrentProperty);
        set => SetValue(IsCurrentProperty, value);
    }

    private static void OnChipChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((AudioQualityChip)sender).Apply();

    /// <summary>
    /// 档位与当前态一起应用：三张底选一张显示，非当前档整体收暗。
    /// 透明度在代码里设而不是绑一个计算属性 —— 计算属性不会发通知，
    /// 绑成 OneWay 会招来 XAML 编译器的 WMC1506。
    /// </summary>
    private void Apply()
    {
        StandardChip.Visibility = Formats.Visible(Quality == AudioQuality.Standard);
        HighChip.Visibility = Formats.Visible(Quality == AudioQuality.High);
        LosslessChip.Visibility = Formats.Visible(Quality == AudioQuality.Lossless);
        ChipRoot.Opacity = IsCurrent ? 1 : 0.45;
        Bindings.Update();
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs args) => HoverOverlay.Opacity = 1;

    private void OnPointerExited(object sender, PointerRoutedEventArgs args) => HoverOverlay.Opacity = 0;
}
