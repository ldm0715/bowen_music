using Bodian.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Bodian.WinUI.Controls;

/// <summary>标记页签内容；只动画内容根节点，不遍历或实现列表中的离屏项。</summary>
public static class Motion
{
    public static readonly DependencyProperty SharedCoverKeyProperty = DependencyProperty.RegisterAttached(
        "SharedCoverKey", typeof(string), typeof(Motion), new PropertyMetadata(""));
    public static string GetSharedCoverKey(DependencyObject element) => (string)element.GetValue(SharedCoverKeyProperty);
    public static void SetSharedCoverKey(DependencyObject element, string value) => element.SetValue(SharedCoverKeyProperty, value);

    public static readonly DependencyProperty ClipToBoundsProperty = DependencyProperty.RegisterAttached(
        "ClipToBounds", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnClipChanged));
    public static bool GetClipToBounds(DependencyObject element) => (bool)element.GetValue(ClipToBoundsProperty);
    public static void SetClipToBounds(DependencyObject element, bool value) => element.SetValue(ClipToBoundsProperty, value);
    private static void OnClipChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not FrameworkElement element) return;
        if ((bool)args.NewValue)
        {
            element.SizeChanged += OnClipSizeChanged;
            ApplyClip(element);
        }
        else
        {
            element.SizeChanged -= OnClipSizeChanged;
            element.Clip = null;
        }
    }
    private static void OnClipSizeChanged(object sender, SizeChangedEventArgs args) => ApplyClip((FrameworkElement)sender);
    private static void ApplyClip(FrameworkElement element) => element.Clip = new RectangleGeometry
    {
        Rect = new Windows.Foundation.Rect(0, 0, Math.Max(0, element.ActualWidth), Math.Max(0, element.ActualHeight))
    };

    public static readonly DependencyProperty TabContentProperty = DependencyProperty.RegisterAttached(
        "TabContent", typeof(bool), typeof(Motion), new PropertyMetadata(false));

    public static bool GetTabContent(DependencyObject element) => (bool)element.GetValue(TabContentProperty);
    public static void SetTabContent(DependencyObject element, bool value) => element.SetValue(TabContentProperty, value);

    internal static void AnimateTabs(DependencyObject root, int direction)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is FrameworkElement element && GetTabContent(child))
            {
                if (element.Visibility == Visibility.Visible)
                    _ = AppMotion.EnterAsync(element, direction * 30, direction == 0 ? 12 : 0);
                else AppMotion.Reset(element);
                continue;
            }
            // 列表项不属于页面结构，跳过虚拟化的内容树。
            if (child is Microsoft.UI.Xaml.Controls.ItemsControl) continue;
            AnimateTabs(child, direction);
        }
    }
}
