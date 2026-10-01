using Bodian.Core.Models;
using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Views;

/// <summary>
/// 一个 AI 歌单（发现页「个性化歌单」里点标题进来）。
/// </summary>
/// <remarks>
/// <b>压在栈上的详情页，不是根</b>：侧栏该继续高亮「发现」。
/// </remarks>
public sealed partial class AiPlaylistPage : Page, INavigationAware
{
    private readonly INavigationService _navigation;

    public AiPlaylistPage(AiPlaylistViewModel viewModel, INavigationService navigation)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(navigation);

        ViewModel = viewModel;
        _navigation = navigation;

        InitializeComponent();
    }

    public AiPlaylistViewModel ViewModel { get; }

    public void OnNavigatedTo() => _ = ViewModel.EnsureLoadedAsync();

    /// <summary>不需要收尾：数据一次加载完，没有常驻订阅要摘。</summary>
    public void OnNavigatedFrom()
    {
    }

    /// <summary>大字标题有没有内容。给 <c>x:Bind</c> 用，直接返回 <see cref="Visibility"/>。</summary>
    private Visibility HasBigTitle =>
        string.IsNullOrWhiteSpace(ViewModel.BigTitle) ? Visibility.Collapsed : Visibility.Visible;

    private void OnBackClick(object sender, RoutedEventArgs e) => _navigation.GoBack();

    private void OnTrackInvoked(object? sender, Track track) => _ = ViewModel.PlayAsync(track);
}
