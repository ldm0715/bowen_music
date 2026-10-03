using Bodian.Core.Models;
using Bodian.WinUI.Playback;
using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

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

        foreach (var bang in ViewModel.Sections.SelectMany(section => section.Bangs))
        {
            bang.SetCurrent(current);
        }
    }

    private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue && args.Item is TrackRow row)
        {
            row.IsPointerOver = false;
            row.IsMenuOpen = false;
        }
    }

    private void OnPreviewRowPointerEntered(object sender, PointerRoutedEventArgs e) => SetPointerOver(sender, true);

    private void OnPreviewRowPointerExited(object sender, PointerRoutedEventArgs e) => SetPointerOver(sender, false);

    private static void SetPointerOver(object sender, bool value)
    {
        if (sender is FrameworkElement { Tag: TrackRow row })
        {
            row.IsPointerOver = value;
        }
    }

    /// <summary>「更多」→ 该榜的完整榜单。</summary>
    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: BangItemViewModel item })
        {
            Open(item);
        }
    }

    /// <summary>
    /// 点榜头（封面 / 名字那一行）也进详情。
    /// </summary>
    /// <remarks>
    /// 只让「更多」可点的话，用户点榜名会以为坏了 —— 那一行看起来就是可点的。
    /// </remarks>
    private void OnHeaderTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: BangItemViewModel item })
        {
            Open(item);
        }
    }

    /// <summary>
    /// 进榜详情。
    /// </summary>
    /// <remarks>
    /// <b>压栈（<c>Navigate</c>）而不是换根</b>：详情压在「排行榜」上面，侧栏该继续高亮排行榜。
    /// </remarks>
    private void Open(BangItemViewModel item) => _navigation.Navigate(_detailFactory(item.Bang));

    /// <summary>
    /// 点曲目行：播放它所在**那个榜**。
    /// </summary>
    /// <remarks>
    /// 队列是这一行所在的榜，不是整页 —— 「下一首」在榜内有效，与搜索页的行为一致。
    /// <para>
    /// 行上带的是 <see cref="TrackRow"/>，要反查它属于哪个榜。榜数不多（实测 20 个），
    /// 线性找足够。行对象是每行一个实例，所以 <c>Contains</c> 走的是引用相等，不会误判。
    /// </para>
    /// </remarks>
    private async void OnTrackTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: TrackRow row })
        {
            return;
        }

        var bang = ViewModel.Sections
            .SelectMany(section => section.Bangs)
            .FirstOrDefault(item => item.Tracks.Contains(row));

        if (bang is null)
        {
            return;
        }

        var index = bang.Tracks.IndexOf(row);

        if (index < 0)
        {
            return;
        }

        // 名次是榜单的展示概念，不进队列 —— 与榜详情页同一条规矩。
        await _coordinator.PlayFromAsync([.. bang.Tracks.Select(item => item.Source)], index);
    }
}
