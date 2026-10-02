using Bodian.Core.Models;
using Bodian.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace Bodian.WinUI.Controls;

public sealed partial class SongCommentsPanel : UserControl
{
    public SongCommentsPanel(SongCommentsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ViewModel = viewModel;
        InitializeComponent();
    }

    public SongCommentsViewModel ViewModel { get; }
    public event EventHandler? CloseRequested;

    public void FocusCloseButton() => CloseButton.Focus(FocusState.Keyboard);
    private void OnCloseClick(object sender, RoutedEventArgs args) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private async void OnRecommendedClick(object sender, RoutedEventArgs args)
    {
        // ToggleButton 会自行反转 IsChecked；同一排序再点一次时保持选中。
        RecommendedButton.IsChecked = true;
        LatestButton.IsChecked = false;
        await ViewModel.ChangeSortAsync(SongCommentSort.Recommended);
        ScrollToTop();
    }

    private async void OnLatestClick(object sender, RoutedEventArgs args)
    {
        LatestButton.IsChecked = true;
        RecommendedButton.IsChecked = false;
        await ViewModel.ChangeSortAsync(SongCommentSort.Latest);
        ScrollToTop();
    }

    private async void OnEditorKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Enter
            && (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & CoreVirtualKeyStates.Down) != 0)
        {
            args.Handled = true;
            if (ViewModel.PublishCommand.CanExecute(null)) await ViewModel.PublishCommand.ExecuteAsync(null);
        }
    }

    private void ScrollToTop()
    {
        if (ViewModel.Items.Count > 0) CommentsList.ScrollIntoView(ViewModel.Items[0]);
    }
}
