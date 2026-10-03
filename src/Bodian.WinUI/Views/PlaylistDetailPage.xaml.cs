using Bodian.Core.Models;
using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
using Microsoft.UI.Xaml;
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

    /// <summary>曲目列表与收藏态一起拉。</summary>
    public void OnNavigatedTo() => _ = ViewModel.EnsureDetailLoadedAsync();

    /// <summary>不需要收尾：取曲目是懒加载的，没有常驻订阅要摘。</summary>
    public void OnNavigatedFrom()
    {
    }

    private void OnTrackInvoked(object? sender, Track track) => ViewModel.PlayCommand.Execute(track);

    /// <summary>
    /// 收藏 / 取消收藏。
    /// </summary>
    /// <remarks>
    /// <b>取消收藏先弹一次确认</b>，收藏直接做 —— 取消是「把一个已经攒起来的东西拿掉」，
    /// 误触的代价不对称。确认框放在页面而不是 ViewModel：那是界面决策，
    /// 且 <c>XamlRoot</c> 也拿不到 ViewModel 里去（同 <c>RecentPage</c> 的清空记录）。
    /// </remarks>
    private async void OnCollectClick(object sender, RoutedEventArgs e)
    {
        var collected = ViewModel.IsCollected == true;

        if (collected)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "取消收藏？",
                Content = "会把这个歌单从你的收藏里移除，之后可以再收藏回来。",
                PrimaryButtonText = "取消收藏",
                CloseButtonText = "再想想",

                // 默认落在「再想想」上，与清空播放记录同一档：误触不该真的把东西删掉。
                DefaultButton = ContentDialogButton.Close,
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }
        }

        await ViewModel.SetCollectedAsync(!collected);
    }

    /// <summary>
    /// 「编辑」。**接口还没实测**（<c>PUT service/playlist</c>，连它的 <c>id</c> 键都是推断的），
    /// 所以这一项先只摆个入口。
    /// </summary>
    private void OnEditPlaylistClick(object sender, RoutedEventArgs e) => ViewModel.NotifyEditUnavailable();

    /// <summary>
    /// 删除这个歌单。
    /// </summary>
    /// <remarks>
    /// <b>必须先确认</b>：歌单连里面的曲目一起没，没有回收站。
    /// 与取消收藏同一档 —— 默认按钮落在「取消」上，误触不该真把东西删掉。
    /// </remarks>
    private async void OnDeletePlaylistClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,

            // 页面当前的实际主题就是应用内主题，转手给弹层。
            // 代码构造的 ContentDialog 不在可视树里，不显式给就永远跟随系统。
            RequestedTheme = ActualTheme,
            Title = $"删除「{ViewModel.Playlist.Name}」？",
            Content = "歌单和里面的曲目会一起删掉，不能恢复。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await ViewModel.DeleteAsync();
    }
}
