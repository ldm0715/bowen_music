using Bodian.WinUI.Services;
using System.Windows.Input;
using Bodian.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Controls;

/// <summary>
/// 专辑列表。已购专辑与收藏的专辑共用。
/// </summary>
/// <remarks>
/// 点行进专辑详情（<c>AlbumDetailPage</c>）。
/// </remarks>
public sealed partial class AlbumListView : UserControl
{
    /// <summary>要显示的专辑集合。绑到 <c>ListView.ItemsSource</c>。</summary>
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(object),
            typeof(AlbumListView),
            new PropertyMetadata(null));

    /// <summary>还有没有下一页。转给内部列表的 <see cref="AutoPaging.HasMoreProperty"/>。</summary>
    public static readonly DependencyProperty HasMoreProperty =
        DependencyProperty.Register(
            nameof(HasMore),
            typeof(bool),
            typeof(AlbumListView),
            new PropertyMetadata(false));

    /// <summary>滚到末尾时执行。转给内部列表的 <see cref="AutoPaging.CommandProperty"/>。</summary>
    public static readonly DependencyProperty LoadMoreCommandProperty =
        DependencyProperty.Register(
            nameof(LoadMoreCommand),
            typeof(ICommand),
            typeof(AlbumListView),
            new PropertyMetadata(null));

    /// <summary>列表内容之后的附加内容，页面拿它放「没有更多了哦~」与失败重试。</summary>
    /// <remarks>
    /// 转给内部列表的 <c>Footer</c>，让它跟着列表一起滚，而不是钉在页面底部。
    /// </remarks>
    public static readonly DependencyProperty FooterProperty =
        DependencyProperty.Register(
            nameof(Footer),
            typeof(object),
            typeof(AlbumListView),
            new PropertyMetadata(null, OnFooterChanged));

    public AlbumListView()
    {
        InitializeComponent();
    }

    /// <summary>用户点了某一行。参数是被点的专辑。</summary>
    public event EventHandler<Album>? AlbumInvoked;

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
        => ((AlbumListView)d).List.Footer = e.NewValue;

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Album album)
        {
            CoverTransitionAnimator.PrepareFromClick(List.ContainerFromItem(album) as FrameworkElement, Formats.AlbumCoverKey(album.Id));
            AlbumInvoked?.Invoke(this, album);
        }
    }
}
