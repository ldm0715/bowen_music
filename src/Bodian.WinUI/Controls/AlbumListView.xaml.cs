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

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Album album)
        {
            AlbumInvoked?.Invoke(this, album);
        }
    }
}
