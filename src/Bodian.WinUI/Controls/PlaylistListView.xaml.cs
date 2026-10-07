using Bodian.WinUI.Services;
using System.Windows.Input;
using Bodian.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Controls;

/// <summary>
/// 歌单列表。收藏的歌单用。
/// </summary>
/// <remarks>
/// 与 <see cref="AlbumListView"/> 同形 —— 两者在侧栏里相邻，观感必须一致。
/// 点行进歌单详情（<c>PlaylistDetailPage</c>）。
/// </remarks>
public sealed partial class PlaylistListView : UserControl
{
    /// <summary>要显示的歌单集合。绑到内部列表的 <c>ItemsSource</c>。</summary>
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(object),
            typeof(PlaylistListView),
            new PropertyMetadata(null));

    /// <summary>还有没有下一页。转给内部列表的 <see cref="AutoPaging.HasMoreProperty"/>。</summary>
    public static readonly DependencyProperty HasMoreProperty =
        DependencyProperty.Register(
            nameof(HasMore),
            typeof(bool),
            typeof(PlaylistListView),
            new PropertyMetadata(false));

    /// <summary>滚到末尾时执行。转给内部列表的 <see cref="AutoPaging.CommandProperty"/>。</summary>
    public static readonly DependencyProperty LoadMoreCommandProperty =
        DependencyProperty.Register(
            nameof(LoadMoreCommand),
            typeof(ICommand),
            typeof(PlaylistListView),
            new PropertyMetadata(null));

    /// <summary>列表内容之后的附加内容，页面拿它放「没有更多了哦~」与失败重试。</summary>
    /// <remarks>转给内部列表的 <c>Footer</c>，让它跟着列表一起滚，而不是钉在页面底部。</remarks>
    public static readonly DependencyProperty FooterProperty =
        DependencyProperty.Register(
            nameof(Footer),
            typeof(object),
            typeof(PlaylistListView),
            new PropertyMetadata(null, OnFooterChanged));

    public PlaylistListView()
    {
        InitializeComponent();
    }

    /// <summary>用户点了某一行。参数是被点的歌单。</summary>
    public event EventHandler<Playlist>? PlaylistInvoked;

    public object? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public bool HasMore
    {
        get => (bool)GetValue(HasMoreProperty);
        set => SetValue(HasMoreProperty, value);
    }

    public ICommand? LoadMoreCommand
    {
        get => (ICommand?)GetValue(LoadMoreCommandProperty);
        set => SetValue(LoadMoreCommandProperty, value);
    }

    public object? Footer
    {
        get => GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }

    // 直接推给内部列表，不走 x:Bind：Footer 属性元素与子内容的赋值顺序在 XAML 里没有保证。
    private static void OnFooterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((PlaylistListView)d).List.Footer = e.NewValue;

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Playlist playlist)
        {
            CoverTransitionAnimator.PrepareFromClick(List.ContainerFromItem(playlist) as FrameworkElement, Formats.PlaylistCoverKey(playlist.Id));
            PlaylistInvoked?.Invoke(this, playlist);
        }
    }
}
