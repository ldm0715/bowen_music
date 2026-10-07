using Bodian.WinUI.Services;
using Bodian.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Controls;

/// <summary>
/// 侧栏面板底部「创建的歌单」那一段的列表。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="PlaylistListView"/> 是两份：那边 48 封面、带分页、整页用（收起态那个浮层用的是它）；
/// 这边 32 封面单行、跟着导航栈高亮、高度由宿主给的约束决定并自己滚。不合并的理由见 XAML 头注释。
/// </para>
/// <para>
/// <b>高度由宿主给</b>（外壳设这一段的 <c>Height</c>），控件内部只负责
/// 「吃掉剩下的空间并内滚」，不自己算高度。
/// </para>
/// </remarks>
public sealed partial class SidebarPlaylistList : UserControl
{
    /// <summary>要显示的歌单集合。绑到内部列表的 <c>ItemsSource</c>。</summary>
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(object),
            typeof(SidebarPlaylistList),
            new PropertyMetadata(null));

    /// <summary>拉取失败的原因。非空时显示重试行，并把它挂在这一行的提示上。</summary>
    public static readonly DependencyProperty ErrorTextProperty =
        DependencyProperty.Register(
            nameof(ErrorText),
            typeof(string),
            typeof(SidebarPlaylistList),
            new PropertyMetadata(null, OnErrorTextChanged));

    public SidebarPlaylistList()
    {
        InitializeComponent();
    }

    /// <summary>用户点了某一行。参数是被点的歌单。</summary>
    public event EventHandler<Playlist>? PlaylistInvoked;

    /// <summary>用户点了「歌单加载失败，点击重试」。</summary>
    public event EventHandler? RetryRequested;

    public object? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public string? ErrorText
    {
        get => (string?)GetValue(ErrorTextProperty);
        set => SetValue(ErrorTextProperty, value);
    }

    /// <summary>
    /// 按歌单 id 高亮；传 <c>null</c> 清除。外壳在 <c>SyncSelection</c> 里推下来。
    /// </summary>
    /// <remarks>
    /// 用方法不用依赖属性：高亮是导航栈的函数，单向由外壳推下来，不需要回写那套。
    /// 点击时列表自己会先把高亮移过去，随后这次推送通常是无操作。
    /// </remarks>
    public void SelectPlaylist(long? playlistId)
    {
        var target = playlistId is { } id && List.ItemsSource is IEnumerable<Playlist> items
            ? items.FirstOrDefault(playlist => playlist.Id == id)
            : null;

        List.SelectedItem = target;

        // 选中的行可能在列表外（滚下去过），滚回来看得见。
        if (target is not null)
        {
            List.ScrollIntoView(target);
        }
    }

    private static void OnErrorTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (SidebarPlaylistList)d;
        var error = e.NewValue as string;

        control.RetryRow.Visibility = string.IsNullOrEmpty(error) ? Visibility.Collapsed : Visibility.Visible;
        ToolTipService.SetToolTip(control.RetryRow, error);
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Playlist playlist)
        {
            CoverTransitionAnimator.PrepareFromClick(List.ContainerFromItem(playlist) as FrameworkElement, Formats.PlaylistCoverKey(playlist.Id));
            PlaylistInvoked?.Invoke(this, playlist);
        }
    }

    private void OnRetryClick(object sender, RoutedEventArgs e) => RetryRequested?.Invoke(this, EventArgs.Empty);
}
