using Bodian.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Controls;

/// <summary>
/// 曲目列表。搜索页、我喜欢的页、歌单详情页共用。
/// </summary>
/// <remarks>
/// <b>点行只报 <see cref="Track"/>，不报下标。</b> 队列要的是「当前列表里的第几首」，
/// 而下标取决于页面用的是哪个集合（搜索结果还是歌单曲目），控件不该知道 ——
/// 页面拿到曲目后自己 <c>IndexOf</c>。
/// </remarks>
public sealed partial class TrackListView : UserControl
{
    /// <summary>要显示的曲目集合。绑到 <c>ListView.ItemsSource</c>。</summary>
    /// <remarks>
    /// 类型是 <see cref="object"/> 而不是 <c>IEnumerable&lt;Track&gt;</c>：
    /// <c>x:Bind</c> 到这里只做赋值，集合本身由 <c>ListView</c> 消费，
    /// 收紧类型只会在 XAML 侧多一层转换。
    /// </remarks>
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(object),
            typeof(TrackListView),
            new PropertyMetadata(null));

    public TrackListView()
    {
        InitializeComponent();
    }

    /// <summary>用户点了某一行。参数是被点的曲目。</summary>
    public event EventHandler<Track>? TrackInvoked;

    public object? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Track track)
        {
            TrackInvoked?.Invoke(this, track);
        }
    }
}
