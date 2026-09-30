using System.ComponentModel;
using Bodian.WinUI.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Controls;

/// <summary>
/// 歌词面板：盖在内容区上的一块歌词，跟着播放滚动。
/// </summary>
/// <remarks>
/// <para>
/// <b>刻意做成面板而不是页面。</b> 页面要走导航，而这个应用还没有返回栈 ——
/// 返回搜索页会新建一个 <c>SearchViewModel</c>，把搜索结果全丢掉。盖一层就完全没有这个问题。
/// </para>
/// <para>
/// 自动滚动只在<b>当前行真的换了</b>的时候做。每一帧都滚会把用户手动翻页的浏览拽回去。
/// </para>
/// </remarks>
public sealed partial class LyricsPanel : UserControl
{
    public LyricsPanel(LyricsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        ViewModel = viewModel;

        InitializeComponent();

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public LyricsViewModel ViewModel { get; }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LyricsViewModel.CurrentIndex) || ViewModel.CurrentIndex < 0)
        {
            return;
        }

        LinesList.ScrollIntoView(ViewModel.Lines[ViewModel.CurrentIndex]);
    }

    private void OnLineClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is LyricLineView line)
        {
            ViewModel.SeekToLineCommand.Execute(line);
        }
    }
}
