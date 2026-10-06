using Bodian.Core.Models;
using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Views;

/// <summary>
/// 「收藏的歌单」页。侧栏的一个根页。
/// </summary>
/// <remarks>
/// <b>歌单 ≠ 专辑</b>：与 <see cref="CollectedAlbumsPage"/> 是两回事，只是两者都读
/// <c>service/collect/4/list</c> 这条混合列表端点，这里取 <c>sourceType == 4</c> 的那一半。
/// </remarks>
public sealed partial class CollectedPlaylistsPage : Page, INavigationAware
{
    /// <summary>收藏来的都是公开歌单；<c>sourceType</c> 缺失时按这个兜底。</summary>
    private const int DefaultPlaylistSource = 4;

    private readonly Func<Playlist, int, PlaylistDetailPage> _playlistDetailFactory;
    private readonly INavigationService _navigation;

    public CollectedPlaylistsPage(
        CollectedPlaylistsViewModel viewModel,
        INavigationService navigation,
        Func<Playlist, int, PlaylistDetailPage> playlistDetailFactory)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(playlistDetailFactory);

        ViewModel = viewModel;
        _navigation = navigation;
        _playlistDetailFactory = playlistDetailFactory;

        InitializeComponent();
    }

    public CollectedPlaylistsViewModel ViewModel { get; }

    public void OnNavigatedTo() => _ = ViewModel.EnsureLoadedAsync();

    /// <summary>不需要收尾：列表是懒加载的，没有常驻订阅要摘。</summary>
    public void OnNavigatedFrom()
    {
    }

    /// <summary>点歌单行 → 歌单详情。压栈，侧栏继续高亮「收藏的歌单」。</summary>
    private void OnPlaylistInvoked(object? sender, Playlist playlist)
    {
        // 歌单自己的 sourceType 就是取曲目要填的 source；缺失（0）时按公开歌单兜底。
        var source = playlist.SourceType > 0 ? playlist.SourceType : DefaultPlaylistSource;
        _navigation.Navigate(_playlistDetailFactory(playlist, source));
    }

    /// <summary>
    /// 卡片网格里的点击。与列表行是同一件事，只是事件参数形状不同，转发给上面那个。
    /// </summary>
    private void OnPlaylistClick(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is Playlist playlist)
        {
            OnPlaylistInvoked(sender, playlist);
        }
    }
}
