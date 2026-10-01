using Bodian.Core.Models;
using Bodian.WinUI.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Views;

/// <summary>
/// 搜索页。侧栏不列它，入口是内容区顶部的常驻搜索框。
/// </summary>
/// <remarks>
/// 页面本身不持有搜索状态 —— 关键词与结果都在单例的 <see cref="SearchViewModel"/> 上，
/// 所以这一页重建（例如从歌词页返回）不会丢结果。
/// </remarks>
public sealed partial class SearchPage : Page
{
    public SearchPage(SearchViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        ViewModel = viewModel;

        InitializeComponent();
    }

    public SearchViewModel ViewModel { get; }

    private void OnTrackInvoked(object? sender, Track track) => ViewModel.PlayCommand.Execute(track);
}
