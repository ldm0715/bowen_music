using Bodian.Core.Models;
using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Views;

/// <summary>
/// 自建歌单详情页。侧栏里每个歌单各是一个根。
/// </summary>
/// <remarks>
/// <b>实现 <see cref="INavigationIdentity"/> 是必须的。</b> 每个歌单详情都是同一个类型，
/// 按类型判等会让「点第二个歌单」被当成「已经是这个页面」而静默不切换。
/// </remarks>
public sealed partial class PlaylistDetailPage : Page, INavigationAware, INavigationIdentity
{
    public PlaylistDetailPage(PlaylistDetailViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        ViewModel = viewModel;

        InitializeComponent();
    }

    public PlaylistDetailViewModel ViewModel { get; }

    /// <summary>
    /// 身份带上 <c>source</c>：同一个 id 在不同 source 下是不同的歌单
    /// （自建是 5、发现页里的公开歌单是 4），只按 id 判等会让两者互相顶掉。
    /// </summary>
    public object NavigationIdentity => ("playlist", ViewModel.Playlist.Id, ViewModel.Source);

    public void OnNavigatedTo() => _ = ViewModel.EnsureLoadedAsync();

    /// <summary>不需要收尾：取曲目是懒加载的，没有常驻订阅要摘。</summary>
    public void OnNavigatedFrom()
    {
    }

    private void OnTrackInvoked(object? sender, Track track) => ViewModel.PlayCommand.Execute(track);
}
