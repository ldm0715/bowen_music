using Bodian.Core.Models;
using Bodian.Core.Models.Home;
using Bodian.WinUI.Playback;
using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Views;

/// <summary>
/// 发现页。侧栏的一个根页。
/// </summary>
/// <remarks>
/// <para>
/// 内容按模块懒加载：首屏几个，滚到底再拉下一批。触发靠列表上的
/// <see cref="Bodian.WinUI.Controls.AutoPaging"/> —— 末尾条目的容器被实现，
/// 就说明用户已经接近底部。
/// </para>
/// <para>
/// 首屏不足一屏时会自动补拉，直到填满或没有更多。<b>这一步是必要的</b>：
/// 列表滚不动的话末尾条目不会再被实现，后面的模块就永远够不到了
/// （换成滚动触发之前，那个「加载更多」按钮正是这条逃生通道）。
/// </para>
/// </remarks>
public sealed partial class DiscoverPage : Page, INavigationAware
{
    private readonly PlaybackCoordinator _coordinator;
    private readonly INavigationService _navigation;
    private readonly Func<Playlist, int, PlaylistDetailPage> _playlistDetailFactory;
    private readonly Func<AiPlaylistRef, string, AiPlaylistPage> _aiPlaylistFactory;

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
    }

    public DiscoverViewModel ViewModel { get; }

    public void OnNavigatedTo() => _ = ViewModel.EnsureLoadedAsync();

    /// <summary>离开这一页时没有需要摘掉的东西：翻页订阅挂在列表自己身上。</summary>
    public void OnNavigatedFrom()
    {
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
}
