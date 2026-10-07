using System.Numerics;
using Bodian.WinUI.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Bodian.WinUI.Controls;

/// <summary>
/// 让一块浮动 <see cref="Border"/> 在列表的选中项之间滑动，充当「选中底」。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么不能靠列表自己画。</b> 选中底是每个项各画各的背景（<c>NavigationViewItem</c> /
/// <c>ListViewItem</c> 模板里的 LayoutRoot），各画各的就跨不了项；<c>NavigationView</c> 自带的
/// 指示条是一根 3px 竖条，模板里那条动画只处理面板收展时的位移，并不知道别的项在哪。
/// 所以要让高亮跟着选中项滑，只能自绘一层。
/// </para>
/// <para>
/// <b>用法。</b> 把一块 <see cref="Border"/> 放进 <see cref="Canvas"/> —— 该 Canvas 必须排在列表
/// <b>之前</b>（也就是画在列表下面），项的文字才压在高亮块之上 —— 再把它挂到列表上：
/// <c>&lt;Border controls:SelectionPill.Owner="{x:Bind Rail}" ... /&gt;</c>。
/// 底色与圆角由那块 Border 自己写，位置和滑动由这里负责。
/// </para>
/// <para>
/// <b>调用方还要做两件事。</b> 一是把列表自带的选中底压成透明（<c>NavigationViewItemBackgroundSelected*</c> /
/// <c>ListViewItemBackgroundSelected*</c>，写在列表自己的 <c>Resources</c> 里），否则会出现
/// 「旧项瞬间亮着 + 高亮块慢慢追上」的双高亮；底色只是从项上挪到了这一层，取同一个色号即可。
/// 二是这一层要跟着宿主一起隐藏（例如进歌词页时整个外壳藏起来）。
/// </para>
/// <para>
/// 底色建议用 <c>SelectionPillBrush</c>：它对浅色/深色给的是与导航项选中底一致的色号、
/// 高对比度给 <c>Transparent</c> —— 那一档保留系统自带的选中底，比自绘更可靠。
/// </para>
/// </remarks>
public static class SelectionPill
{
    /// <summary>这块浮动 Border 跟随的列表。<c>NavigationView</c> 与 <c>ListViewBase</c> 都收。</summary>
    public static readonly DependencyProperty OwnerProperty = DependencyProperty.RegisterAttached(
        "Owner", typeof(DependencyObject), typeof(SelectionPill), new PropertyMetadata(null, OnOwnerChanged));

    public static DependencyObject? GetOwner(DependencyObject element) => element.GetValue(OwnerProperty) as DependencyObject;

    public static void SetOwner(DependencyObject element, DependencyObject? value) => element.SetValue(OwnerProperty, value);

    /// <summary>每块高亮一份连接状态，挂在 Border 自己身上。</summary>
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(Pill), typeof(SelectionPill), new PropertyMetadata(null));

    /// <summary>诊断开关；打开后记录每次落位的判定依据，用来定位「块在跳却不滑」。</summary>
    private static readonly bool Diagnostics = Environment.GetEnvironmentVariable("BODIAN_LYRICS_DIAGNOSTICS") == "1";

    private static ILogger? _logger;

    private static ILogger Logger => _logger ??= (Application.Current.Resources["BodianLoggerFactory"] as ILoggerFactory
        ?? NullLoggerFactory.Instance).CreateLogger(nameof(SelectionPill));

    private static void OnOwnerChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        if (sender.GetValue(StateProperty) is Pill previous)
        {
            previous.Detach();
        }

        if (args.NewValue is not DependencyObject owner)
        {
            sender.SetValue(StateProperty, null);
            return;
        }

        var pill = new Pill(element, owner);
        sender.SetValue(StateProperty, pill);
        pill.Attach();
    }

    /// <summary>导航项的选中底不画在容器上，而是内缩 <c>NavigationViewItemButtonMargin</c> 那一圈。</summary>
    private static Thickness NavigationItemInset =>
        Application.Current.Resources.TryGetValue("NavigationViewItemButtonMargin", out var margin)
            && margin is Thickness value
                ? value
                : new Thickness(4, 2, 4, 2);

    /// <summary>两个矩形是不是同一个位置。0.5px 容差，吸收变换带来的亚像素抖动。</summary>
    private static bool BoxesMatch(Rect a, Rect b) =>
        Math.Abs(a.X - b.X) < 0.5
        && Math.Abs(a.Y - b.Y) < 0.5
        && Math.Abs(a.Width - b.Width) < 0.5
        && Math.Abs(a.Height - b.Height) < 0.5;

    private sealed class Pill(FrameworkElement element, DependencyObject owner)
    {
        /// <summary>当前记着的目标矩形。null = 没有选中项，块收起。</summary>
        private Rect? _bounds;

        /// <summary>上面那个矩形对应的项。用它区分「换了项」与「只是布局把项挪了几像素」。</summary>
        private object? _item;

        /// <summary>列表的滚动宿主。滚动是合成位移，不产生布局，LayoutUpdated 覆盖不到。</summary>
        private ScrollViewer? _scroll;

        /// <summary>宿主列表本身。等它的 Loaded，并挂它的 LayoutUpdated。</summary>
        private FrameworkElement? _host;

        public void Attach()
        {
            // 等**宿主**的 Loaded，不等这块 Border 自己的：Border 初值是 Collapsed，
            // 折叠元素的 Loaded 时机不能指望，一等不到就永远不订阅、永远不测量。
            // PillTabBar 等的是控件自己的 Loaded，同理。
            if (owner is not FrameworkElement host)
            {
                return;
            }

            _host = host;
            host.Loaded += OnHostLoaded;
            host.Unloaded += OnHostUnloaded;

            if (host.IsLoaded)
            {
                OnLoaded();
            }
        }

        public void Detach()
        {
            if (_host is not { } host)
            {
                return;
            }

            host.Loaded -= OnHostLoaded;
            host.Unloaded -= OnHostUnloaded;
            _host = null;
            OnUnloaded();
        }

        private void OnHostLoaded(object sender, RoutedEventArgs e) => OnLoaded();

        private void OnHostUnloaded(object sender, RoutedEventArgs e) => OnUnloaded();

        private void OnLoaded()
        {
            switch (owner)
            {
                case NavigationView navigation:
                    navigation.SelectionChanged += OnNavigationSelectionChanged;
                    break;
                case Selector selector:
                    selector.SelectionChanged += OnSelectorSelectionChanged;
                    break;
            }

            if (_host is { } host)
            {
                // 窗口尺寸、列表收展、页脚高度变化都会让项挪位置：那些场次直接落位、不滑。
                host.LayoutUpdated += OnOwnerLayoutUpdated;
            }

            Sync();
        }

        private void OnUnloaded()
        {
            switch (owner)
            {
                case NavigationView navigation:
                    navigation.SelectionChanged -= OnNavigationSelectionChanged;
                    break;
                case Selector selector:
                    selector.SelectionChanged -= OnSelectorSelectionChanged;
                    break;
            }

            if (_host is { } host)
            {
                host.LayoutUpdated -= OnOwnerLayoutUpdated;
            }

            if (_scroll is not null)
            {
                _scroll.ViewChanged -= OnScrollViewChanged;
                _scroll = null;
            }
        }

        private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args) =>
            Sync();

        private void OnSelectorSelectionChanged(object sender, SelectionChangedEventArgs args) => Sync();

        private void OnOwnerLayoutUpdated(object? sender, object args) => Sync();

        private void OnScrollViewChanged(object? sender, ScrollViewerViewChangedEventArgs args) => Sync();

        /// <summary>
        /// 把高亮挪到当前选中项上。**目标项换了就滑，没换就直接落位。**
        /// </summary>
        /// <remarks>
        /// <para>
        /// 这里刻意**不区分调用方**（选中变化 / 布局变化 / 滚动都走同一个方法）：
        /// 点列表项时，ListView 会先把选中项滚入视野、更新选中态，中间那次由
        /// <c>LayoutUpdated</c> 或 <c>ViewChanged</c> 触发的落位会先把块摆到新位置；
        /// 若按「调用方是不是选中变化」来决定滑不滑，那次非选中变化调用会把动画机会吃掉，
        /// 等选中事件再到时矩形已经相等，动画就没了 —— 表现是块瞬移。
        /// 按「目标项是否变化」判定则谁来都一样：先到的那次负责滑。
        /// </para>
        /// <para>
        /// 窗口缩放、列表收展、滚动这些场次目标项没变，自然直接落位。
        /// 动画跑在合成位移上，<c>Canvas.Left/Top</c> 记的始终是目标值 —— 所以它即使在动画途中
        /// 被 <c>LayoutUpdated</c> 再叫一次也没有副作用：量出来还是同一个矩形，直接返回。
        /// 关掉系统动画时 <see cref="AppMotion"/> 立刻结束，表现就是直接落位，不用另写分支。
        /// </para>
        /// </remarks>
        private void Sync()
        {
            if (!TryMeasure(out var bounds))
            {
                // 已经是收起态就走人：布局每轮都来，别每次都 Reset 一遍。
                if (_bounds is null)
                {
                    return;
                }

                _bounds = null;
                _item = null;
                element.Visibility = Visibility.Collapsed;
                AppMotion.Reset(element);
                return;
            }

            element.Visibility = Visibility.Visible;

            if (_bounds is { } current && BoxesMatch(current, bounds))
            {
                return;
            }

            var previous = _bounds;
            var item = SelectedItem;
            var changed = !ReferenceEquals(_item, item);
            var slide = changed && previous is { Width: > 0, Height: > 0 };

            _item = item;
            _bounds = bounds;

            element.Width = bounds.Width;
            element.Height = bounds.Height;
            Canvas.SetLeft(element, bounds.X);
            Canvas.SetTop(element, bounds.Y);

            if (Diagnostics)
            {
                Logger.LogInformation(
                    "选中底：slide={Slide} itemChanged={Changed} loaded={Loaded} enabled={Enabled} previous={Previous} bounds={Bounds}",
                    slide, changed, element.IsLoaded, AppMotion.IsEnabled, previous, bounds);
            }

            if (slide && previous is { } old)
            {
                _ = AppMotion.PlayAsync(
                    element,
                    new Vector3((float)(old.X - bounds.X), (float)(old.Y - bounds.Y), 0),
                    Vector3.Zero,
                    1,
                    1,
                    new Vector3((float)(old.Width / bounds.Width), (float)(old.Height / bounds.Height), 1),
                    Vector3.One,
                    AppMotion.Standard,
                    topLeftOrigin: true);
                return;
            }

            // 不滑的场次要先清掉上一轮残余的位移，否则块会顶着旧偏移出现在新位置。
            AppMotion.Reset(element);
        }

        private object? SelectedItem => owner switch
        {
            NavigationView navigation => navigation.SelectedItem,
            Selector selector => selector.SelectedItem,
            _ => null,
        };

        /// <summary>
        /// 量出当前选中项那块底色应占的矩形（这块 Border 所在 Canvas 的坐标系）。
        /// </summary>
        /// <returns>没有选中项、容器还没实现出来、或量到空矩形时返回 false。</returns>
        private bool TryMeasure(out Rect bounds)
        {
            bounds = default;

            // 坐标系取**视觉**父级（那块 Canvas）而不是逻辑 Parent：逻辑 Parent 在模板与
            // 虚拟化场景下不保证有值，取不到就等于整块永不显示。
            if (SelectedItem is not { } item || VisualTreeHelper.GetParent(element) is not UIElement layer)
            {
                return false;
            }

            var container = owner switch
            {
                // 菜单项直接就是 NavigationViewItem 实例，容器就是它本身。
                NavigationView navigation => navigation.ContainerFromMenuItem(item) as FrameworkElement,

                // 列表项要给的是项容器；被虚拟化在外时为 null（那时本来也看不见）。
                Selector selector => selector.ContainerFromItem(item) as FrameworkElement,
                _ => null,
            };

            if (container is null || container.ActualWidth <= 0 || container.ActualHeight <= 0)
            {
                return false;
            }

            HookScrollHost(container);

            var full = container.TransformToVisual(layer)
                .TransformBounds(new Rect(0, 0, container.ActualWidth, container.ActualHeight));

            // 导航项的底色内缩一圈（模板把 NavigationViewItemButtonMargin 加在里面那层上），
            // 列表项不用：容器自己的矩形就是它画底色的范围。
            var inset = owner is NavigationView ? NavigationItemInset : default;
            bounds = new Rect(
                full.X + inset.Left,
                full.Y + inset.Top,
                full.Width - inset.Left - inset.Right,
                full.Height - inset.Top - inset.Bottom);

            return bounds.Width > 0 && bounds.Height > 0;
        }

        /// <summary>
        /// 项滚出可视区时这一层不该把块留在外面。从容器往上找第一个 <see cref="ScrollViewer"/>，
        /// 接它的 <c>ViewChanged</c> 重新落位 —— 滚动是合成位移，<c>LayoutUpdated</c> 不会来。
        /// </summary>
        private void HookScrollHost(DependencyObject container)
        {
            for (var node = container; node is not null; node = VisualTreeHelper.GetParent(node))
            {
                if (node is not ScrollViewer host)
                {
                    continue;
                }

                if (ReferenceEquals(host, _scroll))
                {
                    return;
                }

                if (_scroll is not null)
                {
                    _scroll.ViewChanged -= OnScrollViewChanged;
                }

                _scroll = host;
                host.ViewChanged += OnScrollViewChanged;
                return;
            }
        }
    }
}
