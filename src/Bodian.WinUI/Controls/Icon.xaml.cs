using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Bodian.WinUI.Controls;

/// <summary>画一颗 <c>Themes/Icons.xaml</c> 里的图标。</summary>
/// <remarks>
/// <para>
/// 全应用的图标都走这里，不再用 <c>FontIcon</c> 渲染系统字体字形 ——
/// 字体字形的墨水大小与位置由字体决定、逐颗不同，是「对不齐、调不了大小」的根源。
/// </para>
/// <para>
/// <see cref="Data"/> 收的是**路径文本**（<c>{StaticResource IconX}</c>），不是
/// <see cref="Geometry"/>。资源字典里的 Geometry 赋不进来，见 <see cref="IconGeometry"/>。
/// </para>
/// </remarks>
public sealed partial class Icon : UserControl
{
    public Icon()
    {
        InitializeComponent();

        Apply();
    }

    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(string), typeof(Icon), new PropertyMetadata("", OnDisplayChanged));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(Icon), new PropertyMetadata(16d, OnDisplayChanged));

    private static void OnDisplayChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((Icon)sender).Apply();

    /// <summary>
    /// 要画的**路径文本**，来自 <c>Themes/Icons.xaml</c>：<c>Data="{StaticResource IconHeart}"</c>。
    /// </summary>
    /// <remarks>
    /// 是字符串而不是 <see cref="Geometry"/> —— 见 <see cref="IconGeometry"/> 上的说明：
    /// 资源字典里的 Geometry 赋不进 Geometry 属性，会直接让应用打不开。
    /// </remarks>
    public string Data
    {
        get => (string)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    /// <summary>视觉边长。路径一律按 24 单位画，这里缩放到这个尺寸。</summary>
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    private void Apply()
    {
        Scaler.Width = Scaler.Height = Size;
        Glyph.Data = IconGeometry.From(Data);
    }
}
