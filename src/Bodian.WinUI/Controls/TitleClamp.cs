using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Bodian.WinUI.Controls;

/// <summary>
/// 给「歌名 + 角标」这一行里的歌名配宽度上限，角标因此不会被长歌名顶出列宽。
/// </summary>
/// <remarks>
/// <para>
/// 歌名与角标必须放在横向 <see cref="StackPanel"/> 里，角标才会紧贴歌名；
/// 而横向 StackPanel 按无限宽度测量子元素，歌名上的 <c>TextTrimming</c> 等于没写 ——
/// 歌名一长就原样占满，末尾的角标被挤出列宽后裁掉一截（「MV 的 V 少一块」就是这么来的）。
/// 所以歌名要自己带 <c>MaxWidth</c>，值为「这一行所在列的宽度 − 角标占位」。
/// </para>
/// <para>
/// <b>可用宽度只认行 Grid 里那一列的 <c>ColumnDefinition.ActualWidth</c>，不能拿父级的
/// <c>ActualWidth</c>。</b> 竖排 StackPanel 给子元素安排的宽度是
/// <c>max(自己的宽度, 子元素期望宽度)</c> —— 歌名过长时父级宽度被内容一起顶大，
/// 「父宽 − 角标宽」恰好等于歌名原宽，等于没裁。列宽由行宽决定、与内容无关，用它才稳。
/// </para>
/// <para>
/// 也不走 <c>x:Bind</c> 绑 <c>ActualWidth</c>：播放条那边实测过，<c>x:Bind</c> 对 <c>ActualWidth</c>
/// 的依赖属性回调在首轮测量（宽度还是 0）之后不再触发，绑定会停在下限上不动。
/// 这里改为挂行 Grid 的 <c>SizeChanged</c> 事件，绕开这件事，
/// 详见 <c>PlayerBar.xaml.cs</c> 里 <c>UpdateTitleWidth</c> 的说明。
/// </para>
/// <para>
/// 用法：把 <see cref="ReserveProperty"/> 挂在歌名 <c>TextBlock</c> 上，取值见
/// <c>Formats.BadgeReserve</c>。歌名所在的那一列必须是星号列（<c>*</c>）—— 见
/// <see cref="FindHostColumn"/>。
/// </para>
/// </remarks>
public static class TitleClamp
{
    /// <summary>歌名之后那些角标占掉的宽度。</summary>
    public static readonly DependencyProperty ReserveProperty = DependencyProperty.RegisterAttached(
        "Reserve", typeof(double), typeof(TitleClamp), new PropertyMetadata(0d, OnReserveChanged));

    public static double GetReserve(DependencyObject element) => (double)element.GetValue(ReserveProperty);

    public static void SetReserve(DependencyObject element, double value) => element.SetValue(ReserveProperty, value);

    /// <summary>每个目标元素一份的连接状态。挂在元素上，元素回收后跟着丢掉。</summary>
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(State), typeof(TitleClamp), new PropertyMetadata(null));

    private static void OnReserveChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not FrameworkElement target)
        {
            return;
        }

        var state = (State?)target.GetValue(StateProperty);

        if (state is null)
        {
            state = new State(target);
            target.SetValue(StateProperty, state);

            // 只在建状态时订阅一次：进出可视树会反复触发 Loaded/Unloaded，
            // 各自去挂、解宿主的事件，这里重复订阅会变成多次执行。
            target.Loaded += OnTargetLoaded;
            target.Unloaded += OnTargetUnloaded;
        }

        state.Reserve = (double)args.NewValue;

        // 容器去装另一行时 Reserve 会变（角标换了），而列宽没变、SizeChanged 不会来。
        Apply(state);

        if (target.IsLoaded)
        {
            Attach(state);
        }
    }

    private static void OnTargetLoaded(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).GetValue(StateProperty) is State state)
        {
            Attach(state);
        }
    }

    private static void OnTargetUnloaded(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).GetValue(StateProperty) is State state)
        {
            Detach(state);
        }
    }

    private static void Attach(State state)
    {
        if (state.Host is not null)
        {
            return;
        }

        var found = FindHostColumn(state.Target);

        if (found is null)
        {
            return;
        }

        state.Host = found.Value.Grid;
        state.Column = found.Value.Column;
        state.Handler = (_, _) => Apply(state);
        state.Host.SizeChanged += state.Handler;

        // 容器回收后重新进树时列宽早就定了，SizeChanged 不会补发，这里先算一次。
        Apply(state);
    }

    private static void Detach(State state)
    {
        if (state.Host is null)
        {
            return;
        }

        if (state.Handler is not null)
        {
            state.Host.SizeChanged -= state.Handler;
        }

        state.Host = null;
        state.Column = -1;
        state.Handler = null;
    }

    /// <summary>
    /// 找出歌名这一行占的列：往上走到第一个多列 <see cref="Grid"/>，取包着这条内容的那一列。
    /// </summary>
    /// <returns>找不到、或那一列不是星号列时返回 <c>null</c>（调用方据此不设上限）。</returns>
    /// <remarks>
    /// <b>只认星号列。</b> Auto 列的宽度反过来由内容决定 —— 拿它当上限，歌名缩短会让列跟着变窄、
    /// 下一轮算出的上限更小，一路缩到 0。榜单元数据、首页卡片那类「Auto 列 + 列上限」的写法
    /// 本来就自带 MaxWidth，不需要这个附加属性。
    /// </remarks>
    private static (Grid Grid, int Column)? FindHostColumn(FrameworkElement target)
    {
        FrameworkElement? child = target;

        for (var node = VisualTreeHelper.GetParent(child); node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is Grid { ColumnDefinitions.Count: > 0 } grid)
            {
                var column = Math.Clamp(Grid.GetColumn(child), 0, grid.ColumnDefinitions.Count - 1);

                return grid.ColumnDefinitions[column].Width.IsStar ? (grid, column) : null;
            }

            if (node is not FrameworkElement element)
            {
                return null;
            }

            child = element;
        }

        return null;
    }

    /// <summary>
    /// 把宽度上限刷到歌名上。列宽还没量到时直接返回，免得把 0 当成真实宽度、
    /// 把上限锁死在下限上；那种场次由宿主随后的 <c>SizeChanged</c> 补上
    /// （宽度从 0 变成真值会触发）。
    /// </summary>
    private static void Apply(State state)
    {
        if (state.Host is not { } grid
            || state.Column < 0
            || state.Column >= grid.ColumnDefinitions.Count)
        {
            return;
        }

        var available = grid.ColumnDefinitions[state.Column].ActualWidth;

        if (available <= 0)
        {
            return;
        }

        state.Target.MaxWidth = Math.Max(0, available - state.Reserve);
    }

    private sealed class State(FrameworkElement target)
    {
        public FrameworkElement Target { get; } = target;

        /// <summary>歌名所在那一列的宿主 Grid。列宽变了一定伴随它尺寸变化，挂它最省。</summary>
        public Grid? Host { get; set; }

        /// <summary>歌名那一行在 <see cref="Host"/> 里的列号。</summary>
        public int Column { get; set; } = -1;

        /// <summary>挂到宿主上的处理函数。留着是为了能解钩。</summary>
        public SizeChangedEventHandler? Handler { get; set; }

        public double Reserve { get; set; }
    }
}
