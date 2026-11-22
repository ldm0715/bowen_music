using System.Collections.Specialized;
using System.ComponentModel;
using Bowen.Core.Models;
using Bowen.WinUI.ViewModels;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;

namespace Bowen.WinUI.Controls;

/// <summary>评论图片的右键动作。</summary>
public enum CommentImageAction
{
    Copy,
    Save,
}

/// <summary>评论图片右键请求：图片地址与要执行的动作。</summary>
public sealed record CommentImageRequest(Uri ImageUri, CommentImageAction Action);

public sealed partial class SongCommentsPanel : UserControl
{
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _scrollResetTimer;
    private int _scrollResetTicks;
    private bool _resetScroll;
    private readonly MenuFlyout _imageMenu;
    private Uri? _contextImageUri;

    public SongCommentsPanel(SongCommentsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ViewModel = viewModel;
        InitializeComponent();
        // 同一首歌重新打开不重载列表，位置自然保留；换歌时把新列表退回顶部。
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.Items.CollectionChanged += OnCommentsChanged;
        _imageMenu = BuildImageMenu();
    }

    public SongCommentsViewModel ViewModel { get; }
    public event EventHandler? CloseRequested;

    /// <summary>请求分享某条评论；由宿主渲染卡片。</summary>
    public event EventHandler<SongCommentRow>? ShareRequested;

    /// <summary>请求复制或保存评论图片。</summary>
    public event EventHandler<CommentImageRequest>? ImageActionRequested;

    private MenuFlyout BuildImageMenu()
    {
        var menu = new MenuFlyout();
        var copy = new MenuFlyoutItem { Text = "复制图片", Icon = IconGeometry.CreatePathIcon("IconCopy") };
        copy.Click += (_, _) => RequestImageAction(CommentImageAction.Copy);
        var save = new MenuFlyoutItem { Text = "另存为…", Icon = IconGeometry.CreatePathIcon("IconDownload") };
        save.Click += (_, _) => RequestImageAction(CommentImageAction.Save);
        menu.Items.Add(copy);
        menu.Items.Add(save);
        return menu;
    }

    private void RequestImageAction(CommentImageAction action)
    {
        if (_contextImageUri is { } uri) ImageActionRequested?.Invoke(this, new CommentImageRequest(uri, action));
    }

    private void OnCommentImageContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (sender is not FrameworkElement { DataContext: SongCommentRow { ImageUri: { } uri } } element) return;
        _contextImageUri = uri;
        var options = new FlyoutShowOptions();
        if (args.TryGetPosition(element, out var position)) options.Position = position;
        _imageMenu.Hide();
        _imageMenu.ShowAt(element, options);
        args.Handled = true;
    }

    private void OnCommentShareClick(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.DataContext is SongCommentRow row) ShareRequested?.Invoke(this, row);
    }

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
        if (sender is TextBlock block) ScheduleContentToggleUpdate(block);
    }

    private void OnCommentContentLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is TextBlock block) ScheduleContentToggleUpdate(block);
    }

    private void OnCommentContentDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (sender is TextBlock block) ScheduleContentToggleUpdate(block);
    }

    /// <summary>
    /// 延迟到布局结束后，按实际宽度量一遍完整高度，再直接设置“更多”按钮。
    /// </summary>
    /// <remarks>
    /// 不能只靠 <c>SizeChanged</c>：虚拟化列表回收容器后，新内容可能与旧内容同高，
    /// 事件不触发，按钮就会一直保持模板默认的 Collapsed。
    /// </remarks>
    private void ScheduleContentToggleUpdate(TextBlock block)
    {
        block.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => UpdateContentToggle(block));
    }

    private void UpdateContentToggle(TextBlock block)
    {
        if (block.DataContext is not SongCommentRow row || block.ActualWidth <= 0) return;

        var measured = Measure(block);
        var need = block.IsTextTrimmed || measured.NeedsExpansion;
        row.IsContentTrimmed = need;

        var button = (VisualTreeHelper.GetParent(block) as Panel)?
            .Children.OfType<HyperlinkButton>().FirstOrDefault();
        if (button is not null)
        {
            // 直接设显隐，绕开虚拟化回收后可能不刷新的绑定。
            button.Visibility = row.ShowContentToggle ? Visibility.Visible : Visibility.Collapsed;
        }

        if (button is { Visibility: Visibility.Visible })
        {
            // Visibility 从 Collapsed 变 Visible 后，虚拟化列表项不一定重新测量，
            // 结果是按钮/后续内容被旧高度裁掉。逐层失效到 ListViewItem。
            button.InvalidateMeasure();
            for (DependencyObject? current = VisualTreeHelper.GetParent(button);
                 current is not null;
                 current = VisualTreeHelper.GetParent(current))
            {
                if (current is UIElement element) element.InvalidateMeasure();
                if (current is ListViewItem item)
                {
                    item.InvalidateArrange();
                    item.UpdateLayout();
                    break;
                }
            }
        }

    }

    /// <summary>按同一宽度和字体量出完整高度，判断是否会超过当前折叠高度。</summary>
    private static bool NeedsExpansion(TextBlock block) => Measure(block).NeedsExpansion;

    /// <summary>量算并返回宽度 / 折叠高度 / 完整高度。</summary>
    private static (double Width, double Collapsed, double Full, bool NeedsExpansion) Measure(TextBlock block)
    {
        try
        {
            var device = CanvasDevice.GetSharedDevice();
            using var format = new CanvasTextFormat
            {
                FontFamily = block.FontFamily.Source,
                FontSize = (float)block.FontSize,
                FontWeight = block.FontWeight,
                FontStyle = block.FontStyle,
                WordWrapping = CanvasWordWrapping.Wrap,
            };

            // XAML 的 LineHeight 是固定行高；0 表示按字体自然行高，交给 Canvas 默认值。
            if (block.LineHeight > 0)
            {
                format.LineSpacing = (float)block.LineHeight;
                format.LineSpacingMode = CanvasLineSpacingMode.Uniform;
            }

            using var layout = new CanvasTextLayout(
                device, block.Text ?? "", format, (float)block.ActualWidth, 0);

            var collapsed = block.ActualHeight - block.Padding.Top - block.Padding.Bottom;
            var full = layout.LayoutBounds.Height;
            return (block.ActualWidth, collapsed, full, full > collapsed + 1);
        }
        catch
        {
            return (block.ActualWidth, block.ActualHeight, 0, false);
        }
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
