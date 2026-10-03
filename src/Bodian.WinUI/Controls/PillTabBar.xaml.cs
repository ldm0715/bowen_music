using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Controls;

/// <summary>
/// 页内页签条：一组等宽的胶囊按钮，点击即切，选中项由背景与前景区分。
/// </summary>
/// <remarks>
/// <para>
/// <b>只有两个属性</b>：<see cref="ItemsSource"/>（一组页签名）与
/// <see cref="SelectedIndex"/>（双向）。需要「切到某个页签再做点什么」时，
/// 不要在这里加事件 —— 让 ViewModel 在那个属性上写 <c>OnSelectedIndexChanged</c> 分支，
/// 逻辑才留在能离屏测试的一侧。
/// </para>
/// <para>
/// 替换掉的是 <c>Pivot</c>：后者的默认模板带下划线指示条、滑动动画与可拖动表头，
/// 与外壳侧栏那套选中语言不是一套（见 <c>docs/ui-refresh.md</c> §1.3）。
/// </para>
/// </remarks>
public sealed partial class PillTabBar : UserControl
{
    /// <summary>
    /// 两个方向都在改选中项：页面改属性、控件改属性、以及控件内部把属性同步给列表。
    /// 用一个标志把「由我自己发起的变更」挡掉，否则会互相触发成环。
    /// </summary>
    private bool _syncing;

    public PillTabBar() => InitializeComponent();

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(object),
        typeof(PillTabBar),
        new PropertyMetadata(null, (sender, _) => ((PillTabBar)sender).ApplyItemsSource()));

    public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(
        nameof(SelectedIndex),
        typeof(int),
        typeof(PillTabBar),
        new PropertyMetadata(0, (sender, args) => ((PillTabBar)sender).ApplySelectedIndex((int)args.NewValue)));

    /// <summary>页签名，通常是一组字符串。换掉它会重建整条页签并重设选中项。</summary>
    public object? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>选中的页签下标。列表为空时是 <c>-1</c>。</summary>
    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    private void ApplyItemsSource()
    {
        _syncing = true;
        Tabs.ItemsSource = ItemsSource;

        // 换数据源会把选中项清成 -1，所以重建之后要用属性里的值补回来。
        Tabs.SelectedIndex = SelectedIndex;
        _syncing = false;
    }

    private void ApplySelectedIndex(int index)
    {
        // -1 是「列表还没有内容」时的中间态，等 ApplyItemsSource 补，不要在这里写回去。
        if (_syncing || index < 0 || Tabs.SelectedIndex == index)
        {
            return;
        }

        _syncing = true;
        Tabs.SelectedIndex = index;
        _syncing = false;
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        SelectedIndex = Tabs.SelectedIndex;
    }
}
