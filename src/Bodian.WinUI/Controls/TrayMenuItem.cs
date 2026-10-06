using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Bodian.WinUI.Controls;

/// <summary>
/// 能放任意内容的菜单项：<see cref="MenuFlyoutItem"/> 加一个 <see cref="Body"/> 属性。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么必须派生一个类型。</b> <c>MenuFlyoutItemBase</c> 的基类是 <c>Control</c>，
/// <b>不是 <c>ContentControl</c></b> —— 也就是说它**根本没有 <c>Content</c> 属性**
/// （<c>Content</c> 是 <c>ContentControl</c> 引入的），框架模板里也只画
/// <c>{TemplateBinding Text}</c>。所以往 <c>MenuFlyoutItem</c> 里塞内容会在编译期就报
/// <c>WMC0011: Unknown member 'Content'</c>；只改模板同样救不了，模板绑不到一个不存在的属性。
/// </para>
/// <para>
/// 自己加一个能装任意内容的属性，是绕开这个限制的最小代价：菜单行为（点击转发命令、
/// 键盘导航、失焦关闭）全部继承自 <see cref="MenuFlyoutItem"/>，只把「这一项画什么」
/// 换成自写模板。托盘菜单的「三个图标并排一行」正是靠它才成立。
/// </para>
/// <para>
/// 属性刻意叫 <see cref="Body"/> 而不是 <c>Content</c>：后者在 WinUI 里已经有很确定的
/// 「<c>ContentControl</c> 的内容」含义，而这里既不是 <c>ContentControl</c>、
/// 语义也只是「这一项画什么」。
/// </para>
/// </remarks>
public sealed class TrayMenuItem : MenuFlyoutItem
{
    public static readonly DependencyProperty BodyProperty = DependencyProperty.Register(
        nameof(Body), typeof(object), typeof(TrayMenuItem), new PropertyMetadata(null));

    public TrayMenuItem()
    {
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // SecondWindow 会另建 MenuFlyout，未复制原菜单的 PresenterStyle。
        // 在实际承载这些项的 presenter 上解除最小宽度，展开菜单才会按文字收缩。
        for (var ancestor = VisualTreeHelper.GetParent(this);
             ancestor is not null;
             ancestor = VisualTreeHelper.GetParent(ancestor))
        {
            if (ancestor is MenuFlyoutPresenter presenter)
            {
                presenter.MinWidth = 0;
                break;
            }
        }
    }

    /// <summary>这一项要画的内容。</summary>
    public object? Body
    {
        get => GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }
}
