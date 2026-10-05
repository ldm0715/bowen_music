using Bodian.Core.Models;
using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Views;

/// <summary>
/// 「最近播放」页。侧栏的一个根页。
/// </summary>
public sealed partial class RecentPage : Page, INavigationAware
{
    public RecentPage(RecentViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        ViewModel = viewModel;

        InitializeComponent();
    }

    public RecentViewModel ViewModel { get; }

    /// <summary>每次进入都重读，不做「只加载一次」—— 刚听完一首切回来必须能看到它。</summary>
    public void OnNavigatedTo() => _ = ViewModel.LoadAsync();

    /// <summary>不需要收尾：数据是本地文件，没有常驻订阅要摘。</summary>
    public void OnNavigatedFrom()
    {
    }

    private void OnTrackInvoked(object? sender, Track track) => ViewModel.PlayCommand.Execute(track);

    /// <summary>
    /// 清空记录，<b>先问一次</b>。
    /// </summary>
    /// <remarks>
    /// 确认对话框放在页面而不是 <c>RecentViewModel</c>：那是一个界面决策（要不要弹、
    /// 按钮怎么摆），而 <c>XamlRoot</c> 这类东西也拿不到 ViewModel 里去。
    /// 命令本身不重复确认。
    /// </remarks>
    private async void OnClearClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,

            // 代码构造的 ContentDialog 不在可视树里，不显式给主题就永远跟随系统 ——
            // 应用内切成深色时它还是一块白板。
            RequestedTheme = ActualTheme,
            Title = "清空播放记录？",
            Content = "只清掉本机这一份「最近播放」，不动账号里的任何数据，也无法撤销。",
            PrimaryButtonText = "清空",
            CloseButtonText = "取消",

            // 默认落在「取消」上：这个按钮挨着页面右上角，误触的代价是不可撤销的。
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await ViewModel.ClearCommand.ExecuteAsync(null);
    }
}
