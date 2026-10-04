using Bodian.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Bodian.WinUI.Controls;

/// <summary>MV 画质档位的彩色胶囊：流畅灰 / 标清蓝 / 高清金。</summary>
/// <remarks>
/// 与 <see cref="AudioQualityChip"/> 并列的两份（档位枚举不同），但写法与配色同源 ——
/// 画质菜单的入口与弹层里的每一行都用它，观感与音质那个选择器一致。
/// </remarks>
public sealed partial class MvQualityChip : UserControl
{
    public MvQualityChip() => InitializeComponent();

    private static void OnDisplayChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((MvQualityChip)sender).Bindings.Update();

    public static readonly DependencyProperty QualityProperty = DependencyProperty.Register(
        nameof(Quality), typeof(MvQuality), typeof(MvQualityChip),
        new PropertyMetadata(MvQuality.High, OnChipChanged));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(MvQualityChip), new PropertyMetadata("", OnDisplayChanged));

    public static readonly DependencyProperty IsCurrentProperty = DependencyProperty.Register(
        nameof(IsCurrent), typeof(bool), typeof(MvQualityChip), new PropertyMetadata(true, OnChipChanged));

    /// <summary>要显示的档位。</summary>
    public MvQuality Quality
    {
        get => (MvQuality)GetValue(QualityProperty);
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
        => ((MvQualityChip)sender).Apply();

    /// <summary>
    /// 档位与当前态一起应用：三张底选一张显示，非当前档整体收暗。
    /// 透明度在代码里设而不是绑一个计算属性 —— 计算属性不会发通知，
    /// 绑成 OneWay 会招来 XAML 编译器的 WMC1506。
    /// </summary>
    private void Apply()
    {
        LowChip.Visibility = Formats.Visible(Quality == MvQuality.Low);
        StandardChip.Visibility = Formats.Visible(Quality == MvQuality.Standard);
        HighChip.Visibility = Formats.Visible(Quality == MvQuality.High);
        ChipRoot.Opacity = IsCurrent ? 1 : 0.45;
        Bindings.Update();
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs args) => HoverOverlay.Opacity = 1;

    private void OnPointerExited(object sender, PointerRoutedEventArgs args) => HoverOverlay.Opacity = 0;
}
