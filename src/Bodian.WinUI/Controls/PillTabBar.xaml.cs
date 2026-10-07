using System.Numerics;
using Bodian.WinUI.Services;
using Windows.Foundation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Controls;

/// <summary>
/// 页内页签条：按文字宽度排列的胶囊按钮，可选择自动换行。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ItemsSource"/> 提供页签名，<see cref="SelectedIndex"/> 双向同步选中项，
/// <see cref="WrapItems"/> 控制是否按当前内容宽度换行。需要「切到某个页签再做点什么」时，
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

    private Rect? _indicatorBounds;
    private int _indicatorIndex = -1;
    private int _selectionVersion;

    public PillTabBar()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateIndicator(false);
        Unloaded += (_, _) =>
        {
            _selectionVersion++;
            _indicatorBounds = null;
            _indicatorIndex = -1;
            AppMotion.Reset(SelectionIndicator);
        };
        Tabs.LayoutUpdated += (_, _) => UpdateIndicator(false);
    }

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(object),
        typeof(PillTabBar),
        new PropertyMetadata(null, (sender, _) => ((PillTabBar)sender).ApplyItemsSource()));

    public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(
        nameof(SelectedIndex),
        typeof(int),
        typeof(PillTabBar),
        new PropertyMetadata(0, (sender, args) => ((PillTabBar)sender).OnSelectedIndexChanged(
            (int)args.NewValue, (int)args.OldValue)));

    public static readonly DependencyProperty WrapItemsProperty = DependencyProperty.Register(
        nameof(WrapItems),
        typeof(bool),
        typeof(PillTabBar),
        new PropertyMetadata(false, (sender, _) => ((PillTabBar)sender).ApplyLayout()));

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

    /// <summary>开启后按可用宽度换行，禁用横向滚动。</summary>
    public bool WrapItems
    {
        get => (bool)GetValue(WrapItemsProperty);
        set => SetValue(WrapItemsProperty, value);
    }

    private void ApplyLayout()
    {
        Tabs.ItemsPanel = (ItemsPanelTemplate)Resources[WrapItems ? "PillWrapPanel" : "PillSingleRowPanel"];
        Tabs.HorizontalAlignment = WrapItems ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        ScrollViewer.SetHorizontalScrollBarVisibility(Tabs,
            WrapItems ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Hidden);
        ScrollViewer.SetHorizontalScrollMode(Tabs,
            WrapItems ? ScrollMode.Disabled : ScrollMode.Auto);
    }

    private void ApplyItemsSource()
    {
        _syncing = true;
        Tabs.ItemsSource = ItemsSource;

        // 换数据源会把选中项清成 -1，所以重建之后要用属性里的值补回来。
        Tabs.SelectedIndex = SelectedIndex;
        _syncing = false;
    }

    private void OnSelectedIndexChanged(int index, int previous)
    {
        ApplySelectedIndex(index);
        var version = ++_selectionVersion;
        if (!IsLoaded || _syncing) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!IsLoaded || version != _selectionVersion) return;
            UpdateIndicator(true);
            DependencyObject? ancestor = this;
            while (ancestor is not null && ancestor is not Page)
                ancestor = VisualTreeHelper.GetParent(ancestor);
            if (ancestor is not null) Motion.AnimateTabs(ancestor, Math.Sign(index - previous));
        });
    }

    private void UpdateIndicator(bool animate)
    {
        if (!IsLoaded || Tabs.ContainerFromIndex(Tabs.SelectedIndex) is not FrameworkElement container
            || container.ActualWidth <= 0 || container.ActualHeight <= 0)
        {
            SelectionIndicator.Visibility = Visibility.Collapsed;
            return;
        }
        var bounds = container.TransformToVisual(IndicatorLayer).TransformBounds(
            new Rect(0, 0, container.ActualWidth, container.ActualHeight));
        SelectionIndicator.Visibility = Visibility.Visible;
        if (_indicatorBounds == bounds) return;
        var previous = _indicatorBounds;
        animate |= _indicatorIndex != Tabs.SelectedIndex && previous is not null;
        _indicatorIndex = Tabs.SelectedIndex;
        _indicatorBounds = bounds;
        SelectionIndicator.Visibility = Visibility.Visible;
        SelectionIndicator.Width = bounds.Width;
        SelectionIndicator.Height = bounds.Height;
        Canvas.SetLeft(SelectionIndicator, bounds.X);
        Canvas.SetTop(SelectionIndicator, bounds.Y);
        if (animate && previous is { } old && old.Width > 0 && old.Height > 0)
        {
            _ = AppMotion.PlayAsync(SelectionIndicator,
                new Vector3((float)(old.X - bounds.X), (float)(old.Y - bounds.Y), 0), Vector3.Zero,
                1, 1, new Vector3((float)(old.Width / bounds.Width), (float)(old.Height / bounds.Height), 1),
                Vector3.One, AppMotion.Standard, topLeftOrigin: true);
        }
        else AppMotion.Reset(SelectionIndicator);
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
