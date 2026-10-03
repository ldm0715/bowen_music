using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Bodian.WinUI.Controls;

/// <summary>
/// 按行摆放、放不下就换行，**每个子项按自己的内容定宽**。
/// </summary>
/// <remarks>
/// <para>
/// WinUI 自带的换行容器（<c>ItemsWrapGrid</c> / <c>VariableSizedWrapGrid</c>）给一屏里的
/// 所有条目用**同一个格子尺寸**，由第一个条目定下来 —— 标签 chip 里出现一个长词，
/// 其余的要么被裁、要么留一大片空。
/// </para>
/// <para>
/// 间距不做成 <see cref="Thickness"/>：这里只有「同行的间隔」与「行间的间隔」两种，
/// 用两个数就够，也省得调用方去想哪条边是干什么的。
/// </para>
/// </remarks>
public sealed class WrapPanel : Panel
{
    public static readonly DependencyProperty HorizontalSpacingProperty = DependencyProperty.Register(
        nameof(HorizontalSpacing), typeof(double), typeof(WrapPanel), new PropertyMetadata(0d, OnSpacingChanged));

    public static readonly DependencyProperty VerticalSpacingProperty = DependencyProperty.Register(
        nameof(VerticalSpacing), typeof(double), typeof(WrapPanel), new PropertyMetadata(0d, OnSpacingChanged));

    public double HorizontalSpacing
    {
        get => (double)GetValue(HorizontalSpacingProperty);
        set => SetValue(HorizontalSpacingProperty, value);
    }

    public double VerticalSpacing
    {
        get => (double)GetValue(VerticalSpacingProperty);
        set => SetValue(VerticalSpacingProperty, value);
    }

    private static void OnSpacingChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((WrapPanel)sender).InvalidateMeasure();

    protected override Size MeasureOverride(Size availableSize)
    {
        // 横向不给滚动时宽度是有限的；真拿到无穷（外层没约束）就退化成一行，不至于除零。
        var maxWidth = availableSize.Width;

        var lineWidth = 0d;
        var lineHeight = 0d;
        var totalWidth = 0d;
        var totalHeight = 0d;

        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var desired = child.DesiredSize;

            if (lineWidth > 0 && lineWidth + HorizontalSpacing + desired.Width > maxWidth)
            {
                totalWidth = Math.Max(totalWidth, lineWidth);
                totalHeight += lineHeight + VerticalSpacing;
                lineWidth = 0;
                lineHeight = 0;
            }

            lineWidth += lineWidth > 0 ? HorizontalSpacing + desired.Width : desired.Width;
            lineHeight = Math.Max(lineHeight, desired.Height);
        }

        totalWidth = Math.Max(totalWidth, lineWidth);
        totalHeight += lineHeight;

        return new Size(totalWidth, totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0d;
        var y = 0d;
        var lineHeight = 0d;

        foreach (var child in Children)
        {
            var desired = child.DesiredSize;

            if (x > 0 && x + HorizontalSpacing + desired.Width > finalSize.Width)
            {
                x = 0;
                y += lineHeight + VerticalSpacing;
                lineHeight = 0;
            }

            if (x > 0)
            {
                x += HorizontalSpacing;
            }

            child.Arrange(new Rect(x, y, desired.Width, desired.Height));

            x += desired.Width;
            lineHeight = Math.Max(lineHeight, desired.Height);
        }

        return finalSize;
    }
}
