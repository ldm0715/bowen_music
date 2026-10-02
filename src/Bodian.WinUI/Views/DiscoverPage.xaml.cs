using Bodian.Core.Models;
using Bodian.Core.Models.Home;
using Bodian.WinUI.Playback;
using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Bodian.WinUI.Views;

/// <summary>
/// 发现页。侧栏的一个根页。
/// </summary>
/// <remarks>
/// <para>
/// 内容按模块懒加载：首屏几个，滚到底再拉下一批。触发靠 <see cref="FeedList"/> 内部那个
/// <c>ScrollViewer</c> 的 <c>ViewChanged</c> —— 这是 WinUI 里最直接的做法，
/// 不必为每个模块去算它在视口里的位置。
/// </para>
/// <para>
/// 页面底部另留一个「加载更多」按钮当兜底：滚动检测依赖内部 ScrollViewer 被真正创建出来，
/// 内容不足一屏时它可能不触发。
/// </para>
/// </remarks>
public sealed partial class DiscoverPage : Page, INavigationAware
{
    private readonly PlaybackCoordinator _coordinator;
    private readonly INavigationService _navigation;
    private readonly Func<Playlist, int, PlaylistDetailPage> _playlistDetailFactory;
    private readonly Func<AiPlaylistRef, string, AiPlaylistPage> _aiPlaylistFactory;

    private ScrollViewer? _scroll;

    public DiscoverPage(
        DiscoverViewModel viewModel,
        PlaybackCoordinator coordinator,
        INavigationService navigation,
        Func<Playlist, int, PlaylistDetailPage> playlistDetailFactory,
        Func<AiPlaylistRef, string, AiPlaylistPage> aiPlaylistFactory)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(playlistDetailFactory);
        ArgumentNullException.ThrowIfNull(aiPlaylistFactory);

        ViewModel = viewModel;
        _coordinator = coordinator;
        _navigation = navigation;
        _playlistDetailFactory = playlistDetailFactory;
        _aiPlaylistFactory = aiPlaylistFactory;

        InitializeComponent();

        // 内部 ScrollViewer 要等内容加载完才存在，所以先挂 Loaded。
        FeedList.Loaded += OnFeedListLoaded;
        FeedList.Unloaded += OnFeedListUnloaded;
    }

    public DiscoverViewModel ViewModel { get; }

    public void OnNavigatedTo() => _ = ViewModel.EnsureLoadedAsync();

    /// <summary>摘掉滚动订阅，避免页面不在时还在触发加载。</summary>
    public void OnNavigatedFrom() => DetachScroll();

    private void OnFeedListLoaded(object sender, RoutedEventArgs e)
    {
        DetachScroll();

        _scroll = FindScrollViewer(FeedList);

        if (_scroll is not null)
        {
            _scroll.ViewChanged += OnScrollViewChanged;
        }
    }

    private void OnFeedListUnloaded(object sender, RoutedEventArgs e) => DetachScroll();

    private void DetachScroll()
    {
        if (_scroll is not null)
        {
            _scroll.ViewChanged -= OnScrollViewChanged;
            _scroll = null;
        }
    }

    /// <summary>
    /// 滚到接近底部时拉下一批。
    /// </summary>
    /// <remarks>
    /// 留 200 像素的提前量：等真正滚到底才发请求的话，用户会先看到一段空白。
    /// </remarks>
    private void OnScrollViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_scroll is null || !ViewModel.HasMore || ViewModel.IsBusy)
        {
            return;
        }

        var remaining = _scroll.ScrollableHeight - _scroll.VerticalOffset;

        if (remaining <= 200)
        {
            _ = ViewModel.LoadMoreAsync();
        }
    }

    /// <summary>
    /// 点某一组的标题。
    /// </summary>
    /// <remarks>
    /// 「个性化歌单」与「你的主题歌单」的组都可点：每组其实是一个完整歌单，
    /// 模块里给的 3 首只是预览，点进去能取到完整的（实测都是 30 首）。
    /// <para>
    /// <b>用压栈（<c>Navigate</c>）不是换根</b>：AI 歌单页压在「发现」上面，侧栏该继续高亮「发现」。
    /// </para>
    /// </remarks>
    private void OnSectionInvoked(object? sender, HomeSection section)
    {
        if (section.Ai is not { } ai)
        {
            return;
        }

        _navigation.Navigate(_aiPlaylistFactory(ai, section.Title));
    }

    /// <summary>点卡片：曲目就播，歌单就进详情。</summary>
    private async void OnCardInvoked(object? sender, HomeCard card)
    {
        if (card.Track is { } track)
        {
            // 队列就是这一张卡片所在的组 —— 发现页的卡片来自不同模块，
            // 没有一个「整个页面的列表」可以当队列，所以单曲成队。
            await _coordinator.PlayFromAsync([track], 0);
            return;
        }

        if (card.Playlist is { } playlist)
        {
            // ★ 公开歌单的 source 不是账号歌单的 5：填错的话曲目列表会直接是空的
            //   （服务端只回空、不报错）。歌单对象原样带着服务端给的 sourceType（实测是 13）。
            //   缺失时（0）退到 4 —— 那是文档里公开集合的默认值，但**未实测**。
            var source = playlist.SourceType > 0 ? playlist.SourceType : 4;

            _navigation.NavigateRoot(_playlistDetailFactory(playlist, source));
        }
    }

    /// <summary>
    /// 找 <paramref name="element"/> 模板里的那个 <see cref="ScrollViewer"/>。
    /// </summary>
    /// <remarks>
    /// <c>ListView</c> 把它的滚动容器藏在模板里，没有公开属性可取，
    /// 只能走视觉树。找不到时返回 <c>null</c> —— 那样只是懒加载不触发，
    /// 底部那个「加载更多」按钮仍然可用。
    /// </remarks>
    private static ScrollViewer? FindScrollViewer(DependencyObject element)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            var child = VisualTreeHelper.GetChild(element, i);

            if (child is ScrollViewer viewer)
            {
                return viewer;
            }

            if (FindScrollViewer(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
