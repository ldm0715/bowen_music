using Bodian.Core.Models;
using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation.Collections;

namespace Bodian.WinUI.Views;

public sealed partial class ArtistDetailPage : Page, INavigationAware, INavigationIdentity
{
    private readonly INavigationService _navigation;
    private readonly Func<Album, AlbumDetailPage> _albumFactory;
    public ArtistDetailPage(ArtistDetailViewModel viewModel, INavigationService navigation, Func<Album, AlbumDetailPage> albumFactory)
    {
        ViewModel = viewModel;
        _navigation = navigation;
        _albumFactory = albumFactory;
        InitializeComponent();
    }
    public ArtistDetailViewModel ViewModel { get; }
    public object NavigationIdentity => ("artist", ViewModel.Artist.Id);
    public void OnNavigatedTo() => _ = ViewModel.EnsureLoadedAsync();
    public void OnNavigatedFrom() { }
    private void OnTrackInvoked(object? sender, Track track) => _ = ViewModel.PlayAsync(track);
    private void OnAlbumClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Album album) _navigation.Navigate(_albumFactory(album));
    }

    /// <summary>
    /// 关注 / 取消关注。
    /// </summary>
    /// <remarks>
    /// <b>取关先弹一次确认</b>，关注直接做 —— 两边代价不对称，误触取关是「把攒起来的关系拿掉」。
    /// 弹窗放页面而不是 ViewModel：那是界面决策（要不要弹、按钮怎么摆），
    /// 且 <c>XamlRoot</c> 也拿不到 ViewModel 里去（同 <c>RecentPage</c> 的清空记录）。
    /// </remarks>
    private async void OnFollowClick(object sender, RoutedEventArgs e)
    {
        var followed = ViewModel.IsFollowed == true;

        if (followed)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "取消关注？",
                Content = "会不再关注这位歌手，之后可以再关注回来。",
                PrimaryButtonText = "取消关注",
                CloseButtonText = "再想想",

                // 默认落在「再想想」上，与清空播放记录、取消收藏同一档。
                DefaultButton = ContentDialogButton.Close,
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }
        }

        await ViewModel.SetFollowedAsync(!followed);
    }

    /// <summary>卡片本体想要多宽。列数由它推出来，实际宽度再按可用空间等分。</summary>
    /// <remarks>不是硬性尺寸 —— 可用宽度除不尽时卡片会跟着变宽变窄，保证整行铺满。</remarks>
    private const double AlbumCardTargetWidth = 150;

    /// <summary>
    /// 单元格里，卡片之外还占掉多少宽度：两侧的容器内边距，加上底色之间的缝隙。
    /// </summary>
    /// <remarks>
    /// 与容器样式里的 <c>Padding="8"</c> 和 <c>Margin="0,0,4,4"</c> 同源：
    /// 8 + 8 是内边距，4 是缝隙。三者任一改动，这里都要跟着改，否则卡片宽度算错、
    /// 封面就不方了（封面是「撑满剩余」，不是被设定的）。
    /// </remarks>
    private const double AlbumCardInset = 8 + 8 + 4;

    /// <summary>容器内边距，与样式里的 <c>Padding</c> 保持一致。</summary>
    private const double AlbumCardPadding = 8;

    /// <summary>底色之间的缝隙，与样式里的 <c>Margin</c> 保持一致。</summary>
    private const double AlbumCardCellGap = 4;

    /// <summary>封面与文字之间的间隔，与 XAML 里卡片 Grid 的 <c>RowSpacing</c> 保持一致。</summary>
    private const double AlbumCardRowSpacing = 10;

    /// <summary>
    /// 封面底下那块文字区的高度，与 XAML 里 <c>SizeAlbumCardText</c> 同一份。
    /// </summary>
    /// <remarks>
    /// <b>必须留够。</b> <c>ItemsWrapGrid</c> 给所有条目用同一个高度，超出就被裁掉 ——
    /// 之前「长标题把发行时间挤没了」就是因为高度由第一个条目定死、后面的内容更长。
    /// <para>
    /// <b>也不能和 XAML 里各写一份。</b> 封面是「撑满剩余行高」，只有这个值两处一致，
    /// 封面那行剩下的才正好等于卡片宽、才是正方形。所以从资源里取。
    /// </para>
    /// </remarks>
    private static double AlbumCardTextHeight => (double)Application.Current.Resources["SizeAlbumCardText"];

    // 三个入口都接上，缺一个都会让卡片在某条路径上退回「按内容自适应」：
    //   Loaded       —— 首次展开
    //   SizeChanged  —— 窗口或侧栏宽度变化
    //   VectorChanged—— 条目到位。专辑是切到页签才加载的，Loaded 时列表还是空的，
    //                  那时 ItemsPanelRoot 尚未建出来，只有这一个能补上。
    private void OnAlbumGridLoaded(object sender, RoutedEventArgs e)
    {
        AlbumGrid.Items.VectorChanged += OnAlbumItemsChanged;
        UpdateAlbumCardMetrics();
    }

    private void OnAlbumGridUnloaded(object sender, RoutedEventArgs e) =>
        AlbumGrid.Items.VectorChanged -= OnAlbumItemsChanged;

    private void OnAlbumGridSizeChanged(object sender, SizeChangedEventArgs e) => UpdateAlbumCardMetrics();

    private void OnAlbumItemsChanged(IObservableVector<object> sender, IVectorChangedEventArgs args) =>
        UpdateAlbumCardMetrics();

    /// <summary>
    /// 按当前可用宽度重算单元格尺寸，让每一行正好铺满。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 列数取 <c>round(可用宽度 / 目标列宽)</c>，单元格宽度取 <c>可用宽度 / 列数</c>：
    /// 整行总宽正好等于可用宽度，不会在右边剩一条不足一格的空档。
    /// 卡片自身宽度是单元格减去 <see cref="AlbumCardGap"/>，那段差值就是卡片间距。
    /// </para>
    /// <para>
    /// <b>高度是加出来的，每一项都不能漏</b>：卡片高 = 封面 + 卡片内的行间隔 + 文字区；
    /// 单元格高 = 卡片高 + 上下内边距 + 底色之间的缝隙。
    /// 封面那行是「撑满剩余」，所以少算一项封面就不方了 ——
    /// <b>封面不是被设定的，是剩下多少就是多少。</b>
    /// </para>
    /// </remarks>
    private void UpdateAlbumCardMetrics()
    {
        if (AlbumGrid.ItemsPanelRoot is not ItemsWrapGrid panel)
        {
            return;
        }

        var available = AlbumGrid.ActualWidth;

        if (available <= 0)
        {
            return;
        }

        var columns = Math.Max(1, (int)Math.Round(available / (AlbumCardTargetWidth + AlbumCardInset)));
        var cell = available / columns;
        var card = cell - AlbumCardInset;
        var height = card
            + AlbumCardRowSpacing
            + AlbumCardTextHeight
            + (2 * AlbumCardPadding)
            + AlbumCardCellGap;

        // ★★ 值没变就一个都不写，这一句不能省。
        //
        // 给 ItemsWrapGrid 设 ItemWidth/ItemHeight 会让它整块重新布局，而重新布局会重新实现
        // 末尾那个容器 —— 自动翻页正是挂在「末尾容器被实现」上的（见 Controls/AutoPaging.cs）。
        // 于是在「条目集合变了」的回调里无条件重设尺寸，就成了连环：
        //     加一条 → 重设尺寸 → 重新布局 → 触发翻页 → 又加几条 → 重设尺寸 → …
        // 症状是列表自己一页一页往下拉，看着就是「加载了两遍」甚至停不下来。
        //
        // 尺寸只在窗口宽度真的变了的时候才变，所以这个比较能挡掉全部多余的重新布局。
        // 未设过时 ItemWidth 是 NaN，与任何值都不相等，第一次照样写得进去。
        if (panel.ItemWidth == cell && panel.ItemHeight == height)
        {
            return;
        }

        panel.ItemWidth = cell;
        panel.ItemHeight = height;
    }
}
