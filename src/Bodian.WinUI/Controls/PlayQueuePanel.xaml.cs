using Bodian.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Controls;

/// <summary>播放条上「播放列表」按钮弹出的面板。宿主是 <c>PlayerBar</c> 里那个 <c>PlaylistFlyout</c>。</summary>
public sealed partial class PlayQueuePanel : UserControl
{
    public PlayQueuePanel() => InitializeComponent();

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(PlayQueueViewModel), typeof(PlayQueuePanel),
        new PropertyMetadata(null, (sender, _) => ((PlayQueuePanel)sender).Bindings.Update()));

    public PlayQueueViewModel ViewModel
    {
        get => (PlayQueueViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    /// <summary>点了「清空」。确认框由宿主弹，面板自己不弹。</summary>
    public event EventHandler? ClearRequested;

    /// <summary>点了右上角的收起。抽屉的开合由主窗口管。</summary>
    public event EventHandler? CloseRequested;

    private void OnRowClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PlayQueueRow { IsCurrent: false } row)
        {
            ViewModel.Play(row.Position);
        }
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PlayQueueRow row })
        {
            ViewModel.Remove(row.Position);
        }
    }

    private void OnClearClick(object sender, RoutedEventArgs e) => ClearRequested?.Invoke(this, EventArgs.Empty);

    private void OnCloseClick(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
}
