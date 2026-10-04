using Bodian.Core.Models;
using Bodian.WinUI.Playback;
using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Bodian.WinUI.Views;

/// <summary>
/// 排行榜页。侧栏的一个根页。
/// </summary>
/// <remarks>
/// <b>整页只有一次请求</b>（<c>service/home/bangNew</c>），没有懒加载也不分页 ——
/// 首页一次就把所有分组与每个榜的前几首预览都带回来了。
/// 完整榜单按榜点进 <see cref="BangDetailPage"/> 取。
/// </remarks>
public sealed partial class BangListPage : Page, INavigationAware
{
    private readonly Func<Bang, BangDetailPage> _detailFactory;
    private readonly INavigationService _navigation;
    private readonly PlaybackCoordinator _coordinator;

    public BangListPage(
        BangListViewModel viewModel,
        INavigationService navigation,
        Func<Bang, BangDetailPage> detailFactory,
        PlaybackCoordinator coordinator)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(detailFactory);
        ArgumentNullException.ThrowIfNull(coordinator);

        ViewModel = viewModel;
        _navigation = navigation;
        _detailFactory = detailFactory;
        _coordinator = coordinator;

        InitializeComponent();
    }

    public BangListViewModel ViewModel { get; }

    public void OnNavigatedTo()
    {
        // 订阅在这里而不是 ViewModel 的构造函数里：本页的 ViewModel 是 transient，
        // 而协调器是单例 —— 在构造函数里订阅会让单例一直持有已离开的页面。
        _coordinator.Started += OnPlaybackStarted;
        _coordinator.Blocked += OnPlaybackBlocked;

        _ = LoadAndSyncAsync();
    }

    public void OnNavigatedFrom()
    {
        _coordinator.Started -= OnPlaybackStarted;
        _coordinator.Blocked -= OnPlaybackBlocked;
    }

    private async Task LoadAndSyncAsync()
    {
        await ViewModel.EnsureLoadedAsync().ConfigureAwait(true);

        // 加载完立刻对一次：进页面时可能已经有一首在播了。
        SyncCurrent();
    }

    private void OnPlaybackStarted(object? sender, PlaybackStartedEventArgs e) => SyncCurrent();

    private void OnPlaybackBlocked(object? sender, PlaybackBlockedEventArgs e) => SyncCurrent();

    /// <summary>把「当前是哪一首」刷到所有预览行上。</summary>
    private void SyncCurrent()
    {
        var current = _coordinator.CurrentTrack?.Id;

        // 分组本身就是榜的集合（见 BangSectionViewModel），不用再取一层属性。
        foreach (var bang in ViewModel.Sections.SelectMany(section => section))
        {
            bang.SetCurrent(current);
        }
    }

    /// <summary>
    /// 指针进出卡片。悬停底是卡片自己画的那一层，这里只负责把状态翻过来。
    /// </summary>
    /// <remarks>
    /// 状态放在 <see cref="BangItemViewModel"/> 上而不是容器上：
    /// 容器那层悬停底铺在整格、还会被卡片的不透明底色盖住，见
    /// <see cref="BangItemViewModel.IsPointerOver"/> 的说明。
    /// </remarks>
    private void OnCardPointerEntered(object sender, PointerRoutedEventArgs e) => SetPointerOver(sender, true);

    private void OnCardPointerExited(object sender, PointerRoutedEventArgs e) => SetPointerOver(sender, false);

    private static void SetPointerOver(object sender, bool value)
    {
        if (sender is FrameworkElement { Tag: BangItemViewModel item })
        {
            item.IsPointerOver = value;
        }
    }

    /// <summary>
    /// 点卡片任意非曲目处 → 进该榜详情。
    /// </summary>
    /// <remarks>
    /// 整张卡都可点，因为整张卡看起来就是可点的；只让某一角可点会让人以为坏了。
    /// </remarks>
    private void OnCardTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: BangItemViewModel item })
        {
            Open(item);
        }
    }

    /// <summary>
    /// 点分组表头 → 收起 / 展开这一组。
    /// </summary>
    /// <remarks>
    /// 收起就是把该组清空（见 <see cref="BangSectionViewModel.Toggle"/>）。
    /// 组清空后表头仍在原地 —— <c>GroupStyle.HidesIfEmpty</c> 默认 false，
    /// 这是「收起了还点得回来」的前提。
    /// <para>
    /// <b>表头被钉在顶部时点不动</b>，见 <see cref="IsHeaderPinned"/>。
    /// </para>
    /// <para>
    /// 展开后要重刷一次「正在播放」：装回来的行是新的绑定目标，
    /// 而 <see cref="BangItemViewModel.SetCurrent"/> 只在播放状态变化时被调用。
    /// </para>
    /// </remarks>
    private void OnSectionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: BangSectionViewModel section } header)
        {
            return;
        }

        if (IsHeaderPinned(header))
        {
            return;
        }

        section.Toggle();
        SyncCurrent();
    }

    /// <summary>
    /// 这个表头是不是正被钉在列表顶部。
    /// </summary>
    /// <remarks>
    /// <b>钉住时不许收起</b>：那一组的卡片正显示在下面，一收起面板立刻重排，
    /// 钉的位置会在「钉这一组」和「钉下一组」之间来回切，看起来就是一阵抽搐。
    /// 想收这一组，先滚一下让它离开钉住的位置。
    /// <para>
    /// 两个条件缺一不可：列表确实滚过，且表头贴在视口顶端。
    /// 少了前一条，滚到最顶上时的第一个表头也会被判成钉住 —— 那时收起明明没有任何问题。
    /// 表头的位置由面板负责移动（钉住就是把它挪到顶部），所以取它当下的变换坐标即可。
    /// </para>
    /// </remarks>
    private bool IsHeaderPinned(FrameworkElement header)
    {
        if (FindAncestor<GridViewHeaderItem>(header) is not { } item)
        {
            return false;
        }

        if (FindDescendant<ScrollViewer>(SectionList) is not { } scrollViewer || scrollViewer.VerticalOffset <= 0)
        {
            return false;
        }

        // 容差 2px：钉住时它贴着顶端，但不保证正好是 0。
        var top = item.TransformToVisual(SectionList).TransformPoint(new Point(0, 0)).Y;
        return Math.Abs(top) < 2;
    }

    private static T? FindAncestor<T>(DependencyObject? node)
        where T : DependencyObject
    {
        for (var current = VisualTreeHelper.GetParent(node); current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }

    private static T? FindDescendant<T>(DependencyObject? node)
        where T : DependencyObject
    {
        if (node is null)
        {
            return null;
        }

        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is T match)
            {
                return match;
            }

            if (FindDescendant<T>(child) is { } deeper)
            {
                return deeper;
            }
        }

        return null;
    }

    /// <summary>
    /// 进榜详情。
    /// </summary>
    /// <remarks>
    /// <b>压栈（<c>Navigate</c>）而不是换根</b>：详情压在「排行榜」上面，侧栏该继续高亮排行榜。
    /// </remarks>
    private void Open(BangItemViewModel item) => _navigation.Navigate(_detailFactory(item.Bang));

    // 卡片上的曲目行不接点击 —— 卡片只有「进详情」一个动作。
    // 以前这里有一条「点一行就播这一首」，已按使用者要求删掉：想听就先进榜详情页。
}
