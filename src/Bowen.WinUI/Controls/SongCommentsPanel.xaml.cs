using System.Collections.Specialized;
using System.ComponentModel;
using Bowen.Core.Models;
using Bowen.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;

namespace Bowen.WinUI.Controls;

public sealed partial class SongCommentsPanel : UserControl
{
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _scrollResetTimer;
    private int _scrollResetTicks;
    private bool _resetScroll;

    public SongCommentsPanel(SongCommentsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ViewModel = viewModel;
        InitializeComponent();
        // 同一首歌重新打开不重载列表，位置自然保留；换歌时把新列表退回顶部。
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.Items.CollectionChanged += OnCommentsChanged;
    }

    public SongCommentsViewModel ViewModel { get; }
    public event EventHandler? CloseRequested;

    /// <summary>
    /// 把焦点移到关闭按钮。
    /// </summary>
    /// <remarks>
    /// <b>默认 Programmatic</b>：焦点框是给键盘用户的，鼠标点击之后也用 Keyboard 移焦点，
    /// 关闭按钮上会留下一圈白边，看着像选中态没清掉。键盘路径由调用方显式传 Keyboard。
    /// </remarks>
    public void FocusCloseButton(FocusState focusState = FocusState.Programmatic)
        => CloseButton.Focus(focusState);
    private void OnCloseClick(object sender, RoutedEventArgs args) => CloseRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// 把「正文是否被行数上限截断」从模板回写到行上，供「更多」按钮决定是否露面。
    /// </summary>
    /// <remarks>
    /// 只在 <c>TextBlock</c> 自己量完之后才知道结果，所以挂 SizeChanged 而不是绑属性。
    /// 容器回收复用时若新旧内容尺寸恰好一致、这个事件不触发，行上留的会是上一次的值 ——
    /// 那种情况下「更多」的显隐可能与内容不符，点开一次就会借布局刷新纠正。
    /// </remarks>
    private void OnCommentContentSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (sender is TextBlock { DataContext: SongCommentRow row } block) row.IsContentTrimmed = block.IsTextTrimmed;
    }

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

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SongCommentsViewModel.MusicId)) _resetScroll = true;
    }

    /// <summary>换歌后的第一批评论进列表时把位置退回顶部。</summary>
    /// <remarks>
    /// 列表实例跨歌复用，旧偏移会留到新列表上；一次归零不够，虚拟化面板还会按旧锚点
    /// 再落位一次，所以在布局稳定前连续纠正几拍。
    /// </remarks>
    private void OnCommentsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.Action != NotifyCollectionChangedAction.Add || !_resetScroll) return;
        _resetScroll = false;
        ScrollToTop();
    }

    private void ScrollToTop()
    {
        if (_scrollResetTimer is null)
        {
            _scrollResetTimer = DispatcherQueue.CreateTimer();
            _scrollResetTimer.Interval = TimeSpan.FromMilliseconds(40);
            _scrollResetTimer.IsRepeating = true;
            _scrollResetTimer.Tick += OnScrollResetTick;
        }
        _scrollResetTicks = 0;
        _scrollResetTimer.Stop();
        _scrollResetTimer.Start();
        ApplyScrollReset();
    }

    private void OnScrollResetTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        ApplyScrollReset();
        if (++_scrollResetTicks >= 8) sender.Stop();
    }

    private void ApplyScrollReset()
    {
        CommentsList.UpdateLayout();
        if (FindDescendant<ScrollViewer>(CommentsList) is { } scroll)
            scroll.ChangeView(null, 0, null, disableAnimation: true);
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } found) return found;
        }
        return null;
    }
}
