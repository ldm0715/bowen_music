using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Controls;

/// <summary>
/// 滚到列表末尾自动翻下一页。
/// </summary>
/// <remarks>
/// <para>
/// 挂在 <see cref="ListViewBase"/> 上（<c>ListView</c> 与 <c>GridView</c> 都算）。
/// 列表的薄包装控件（<see cref="TrackListView"/>、<see cref="AlbumListView"/>）
/// 各转发两个依赖属性，页面直接绑它们即可。
/// </para>
/// <para>
/// <b>触发靠「末尾条目的容器被实现」，不走 <c>ScrollViewer.ViewChanged</c>。</b>
/// 后者有三个硬伤：只在滚动或缩放改变视口时触发，内容变长不触发；
/// 列表始终不足一屏时偏移量恒为 0，事件永远不来，会卡死在第一页；
/// 而且它得先拿到模板里的 <c>ScrollViewer</c>，折叠着的列表（评论面板的回复列表）
/// 根本不展开模板，找都找不到。
/// </para>
/// <para>
/// 容器被实现说明用户已经接近底部。列表默认的 <c>CacheLength=0.5</c> 让末尾条目
/// 提前约半个视口实现 —— 等真滚到底再发请求的话，用户会先看到一段空白。
/// </para>
/// <para>
/// <b>内容不足一屏时会连环补页，直到填满或没有更多。</b> 这是有意的：否则短列表
/// 根本无法滚动，末尾容器不会再实现，后面的数据就永远够不到了。循环天然收敛 ——
/// 内容超过视口半个高度后末尾条目不再实现，事件也就不再来。
/// </para>
/// <para>
/// 重复触发由 <see cref="ICommand.CanExecute"/>（异步命令执行期间返回 false）
/// 与各 ViewModel 内部的忙闲判断挡住，这里不额外加锁。
/// </para>
/// </remarks>
public static class AutoPaging
{
    /// <summary>滚到末尾时执行。绑 ViewModel 的 <c>LoadMoreCommand</c>。</summary>
    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.RegisterAttached(
            "Command",
            typeof(ICommand),
            typeof(AutoPaging),
            new PropertyMetadata(null, OnPropertyChanged));

    /// <summary>还有没有下一页。为 <c>false</c> 时不触发。</summary>
    public static readonly DependencyProperty HasMoreProperty =
        DependencyProperty.RegisterAttached(
            "HasMore",
            typeof(bool),
            typeof(AutoPaging),
            new PropertyMetadata(false, OnPropertyChanged));

    public static void SetCommand(DependencyObject element, ICommand? value) =>
        element.SetValue(CommandProperty, value);

    public static ICommand? GetCommand(DependencyObject element) =>
        (ICommand?)element.GetValue(CommandProperty);

    public static void SetHasMore(DependencyObject element, bool value) =>
        element.SetValue(HasMoreProperty, value);

    public static bool GetHasMore(DependencyObject element) =>
        (bool)element.GetValue(HasMoreProperty);

    /// <summary>
    /// 两个属性任一变化都评估一次。
    /// </summary>
    /// <remarks>
    /// <b>不能只认 <see cref="HasMoreProperty"/>。</b> XAML 里属性的赋值顺序不保证，
    /// 「还有更多」可能先于命令到达；那时评估会发现没有命令而什么都不做，
    /// 之后再没有事件送上门来。
    /// </remarks>
    private static void OnPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListViewBase list)
        {
            return;
        }

        Hook.Ensure(list).Evaluate();
    }

    /// <summary>
    /// 挂在一个列表上的行为。用 <see cref="ConditionalWeakTable{TKey,TValue}"/> 保证只挂一次。
    /// </summary>
    private sealed class Hook
    {
        private static readonly ConditionalWeakTable<ListViewBase, Hook> Hooks = [];

        private readonly ListViewBase _list;
        private readonly ILogger _logger;

        private Hook(ListViewBase list)
        {
            _list = list;
            _logger = (Application.Current.Resources["BodianLoggerFactory"] as ILoggerFactory
                ?? NullLoggerFactory.Instance).CreateLogger<Hook>();

            list.ContainerContentChanging += OnContainerContentChanging;
            list.Loaded += OnLoaded;
        }

        public static Hook Ensure(ListViewBase list) => Hooks.GetValue(list, static l => new Hook(l));

        private void OnLoaded(object sender, RoutedEventArgs e) => Evaluate();

        private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            // 回收时 args.ItemIndex 指的是「刚被收走的那一项」，与末尾无关。
            if (args.InRecycleQueue)
            {
                return;
            }

            // 先做 O(1) 过滤：只有末尾条目的容器被实现才可能与翻页有关。
            if (args.ItemIndex != _list.Items.Count - 1)
            {
                return;
            }

            Load();
        }

        /// <summary>
        /// 末尾容器当前已实现的话，判断一次要不要拉下一页。
        /// </summary>
        /// <remarks>
        /// 用在「没有新容器事件可等」的场合：页面切回来（列表本来就短，容器早已实现）、
        /// 「还有更多」重新变为真（重新加载之后）。
        /// <para>
        /// <b>这个「末尾是否已实现」的判断不能省。</b> 只看有没有更多就拉的话，
        /// 长列表刚加载完第一页、用户还在顶部时就会立刻拉第二页，等于自动把整个列表拉完。
        /// </para>
        /// </remarks>
        public void Evaluate()
        {
            if (_list.Items.Count == 0)
            {
                return;
            }

            // 没实现的条目拿不到容器，正好当作「末尾还在屏幕外」。
            if (_list.ContainerFromIndex(_list.Items.Count - 1) is null)
            {
                return;
            }

            Load();
        }

        private void Load()
        {
            if (!GetHasMore(_list) || GetCommand(_list) is not { } command || !command.CanExecute(null))
            {
                return;
            }

            try
            {
                command.Execute(null);
            }
            catch (Exception ex)
            {
                // 各 ViewModel 自己会记录失败原因；这里只保证异常不逃到 UI 线程上
                // —— 自动翻页触发得频繁，一次未处理异常就是闪退。
                _logger.LogWarning(ex, "自动翻页失败，列表 {Name}", _list.Name);
            }
        }
    }
}
