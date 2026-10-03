using System.Collections.ObjectModel;
using Bodian.Core.Models.Home;
using Bodian.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Bodian.WinUI.Controls;

/// <summary>
/// 发现页里的一个模块：一排横向排列的卡片，靠两侧箭头翻页。
/// </summary>
/// <remarks>
/// <b>它只是个视图，不认识门面。</b> 点卡片只把 <see cref="HomeCard"/> 抛给页面，
/// 由页面决定「曲目就播、歌单就进详情」—— 控件不该知道导航与播放的存在。
///
/// <b>卡片区不滚动。</b> 列表只装「当前一页放得下的那些」，内容宽度永远不超过视口，
/// <c>ScrollViewer</c> 就没有可滚的量 —— 鼠标滚轮不会被吞掉，会照常冒泡去滚页面。
/// 分页算式在 <see cref="CardStripPaging"/>。
/// </remarks>
public sealed partial class HomeSectionView : UserControl
{
    /// <summary>单曲模块里一列放几首。</summary>
    private const int TracksPerColumn = 3;

    /// <summary>要显示的那一组。绑到 XAML 的 <c>Section</c>。</summary>
    public static readonly DependencyProperty SectionProperty =
        DependencyProperty.Register(
            nameof(Section),
            typeof(HomeSection),
            typeof(HomeSectionView),
            new PropertyMetadata(null, OnSectionChanged));

    /// <summary>当前这一页真正显示的项。列表只装这一页，所以它永远不比视口宽。</summary>
    private readonly ObservableCollection<object> _pageItems = [];

    /// <summary>这一组的全部项。单曲模块在这里已经按每列几首切好。</summary>
    private IReadOnlyList<object> _items = [];

    private int _pageSize;              // 一页放得下几项（0 = 还没量到宽度）
    private int _pageIndex;             // 当前第几页（0 起）
    private double _measuredWidth = -1; // 上次算过的宽度，用来挡掉多余的重排
    private bool _hovered;              // 鼠标在不在这一组上
    private bool _hasPreviousPage;
    private bool _hasNextPage;

    public HomeSectionView()
    {
        InitializeComponent();
        // 宽度要挂载后才量得到，而 Section 通常先于宽度到位，所以两个入口都要接。
        Loaded += (_, _) => SyncPage(resetToFirst: false);
    }

    /// <summary>用户点了某张卡片或某一行曲目。参数都是那张卡片的 <see cref="HomeCard"/>。</summary>
    public event EventHandler<HomeCard>? CardInvoked;

    public HomeSection? Section
    {
        get => (HomeSection?)GetValue(SectionProperty);
        set => SetValue(SectionProperty, value);
    }

    /// <summary>列表的数据源。同一实例，所以 <c>x:Bind</c> 的 OneTime 就够，翻页不必重建绑定。</summary>
    private ObservableCollection<object> PageItems => _pageItems;

    /// <summary>
    /// 一步的横向宽度：这一组卡片的宽度 + 卡片两侧让出的空白 + 容器间隔。
    /// </summary>
    /// <remarks>
    /// 四种排法的卡片宽度各不相同（见 Tokens.xaml 里 SizeHome* 那一族），
    /// 所以步长得跟着 <see cref="HomeSection.Layout"/> 走。
    /// ★ 卡片让出的空白也要算进来：容器比卡片宽出来的就是那两段（悬停底铺在上面）。
    /// </remarks>
    private double Step => Token(Section?.Layout switch
    {
        HomeSectionLayout.TrackColumns => "SizeHomeTrackColumn",
        HomeSectionLayout.PlaylistPreview => "SizeHomePreviewCard",
        HomeSectionLayout.PlaylistMosaic => "SizeHomeMosaicCard",
        _ => "SizeHomeCoverCard",
    }) + 2 * Token("SpaceHomeCardMargin") + Token("SpaceHomeCardGap");

    /// <summary>
    /// 卡片区真正能放卡片的宽度。
    /// </summary>
    /// <remarks>
    /// 列表左右各留了给翻页按钮的一段（<c>SpaceHomePagerMargin</c>），
    /// 算式必须把它们减掉，否则最后一页会多算一张、探进按钮那段留白里。
    /// </remarks>
    private double AvailableWidth => Math.Max(0, StripHost.ActualWidth - 2 * Token("SpaceHomePagerMargin"));

    private static double Token(string key) => Application.Current.Resources[key] switch
    {
        double scalar => scalar,
        Thickness thickness => thickness.Right,
        _ => 0,
    };

    private void OnStripSizeChanged(object sender, SizeChangedEventArgs e) => SyncPage(resetToFirst: false);

    private void OnPreviousClick(object sender, RoutedEventArgs e) => TurnPage(-1);

    private void OnNextClick(object sender, RoutedEventArgs e) => TurnPage(1);

    /// <summary>
    /// 按当前宽度重算一页放几项，并把页码钉到原来那项附近。
    /// </summary>
    /// <param name="resetToFirst">换组时为真：回到第一页，并丢掉上一组留下的宽度缓存。</param>
    private void SyncPage(bool resetToFirst)
    {
        if (resetToFirst)
        {
            // 容器会被回收复用给别的 Section，状态必须在这里复位。
            _pageIndex = 0;
            _pageSize = 0;
            _measuredWidth = -1;
        }

        var width = AvailableWidth;
        if (width <= 0) return; // 还没量到宽度，等 SizeChanged

        var step = Step;
        var size = CardStripPaging.PageSize(width, step);

        // 宽度与页大小都没变就退出：否则填页引起的重排会再触发一次 SizeChanged，
        // 变成「填页 → 重排 → 再填页」的连环。
        if (size == _pageSize && width == _measuredWidth) return;

        // 变宽变窄时钉住「当前页的第一项」：它可能被挤进别的页，但得还在眼前。
        var anchor = _pageSize <= 0 ? -1 : _pageIndex * _pageSize;
        var page = CardStripPaging.Resolve(width, step, _items.Count, anchor);

        _measuredWidth = width;
        _pageSize = size;
        _pageIndex = page.PageIndex;

        ApplyPage(page);
    }

    /// <summary>
    /// 翻一页。
    /// </summary>
    /// <remarks>
    /// 越界的兜底靠 <see cref="CardStripPaging.Resolve"/> 的夹取：已经到头时算出来的还是当前页，
    /// 于是直接返回。正常路径上按钮本来就已经藏起来了。
    /// </remarks>
    private void TurnPage(int delta)
    {
        var page = CardStripPaging.Resolve(AvailableWidth, Step, _items.Count, (_pageIndex + delta) * _pageSize);
        if (page.PageIndex == _pageIndex) return;

        _pageIndex = page.PageIndex;
        ApplyPage(page);
    }

    private void ApplyPage(CardStripPage page)
    {
        _pageItems.Clear();

        // 多装一项：右边那一项会被视口裁掉一截，露出下一张的一角 ——
        // 一眼看得出「右边还有」，比干干净净地截断更像一个可以翻页的列表。
        var shown = page.HasNext ? page.Count + 1 : page.Count;
        for (var i = page.FirstIndex; i < page.FirstIndex + shown && i < _items.Count; i++)
        {
            _pageItems.Add(_items[i]);
        }

        _hasPreviousPage = page.HasPrevious;
        _hasNextPage = page.HasNext;
        UpdatePagerVisibility();
    }

    /// <summary>
    /// 箭头只在鼠标停在这一组上时才出现。
    /// </summary>
    /// <remarks>
    /// 平时让卡片自己说话。只有一页、或者已经到头的那个方向，照旧不显示 ——
    /// 点不动的箭头比没有更让人困惑。
    /// </remarks>
    private void UpdatePagerVisibility()
    {
        PreviousButton.Visibility = Formats.Visible(_hovered && _hasPreviousPage);
        NextButton.Visibility = Formats.Visible(_hovered && _hasNextPage);
    }

    private void OnStripPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _hovered = true;
        UpdatePagerVisibility();
    }

    private void OnStripPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _hovered = false;
        UpdatePagerVisibility();
    }

    /// <summary>单曲行亮起悬停底色。底色是行模板里的第一层，切换它的不透明度即可。</summary>
    private void OnHoverPointerEntered(object sender, PointerRoutedEventArgs e) => SetHoverOpacity(sender, 1);

    private void OnHoverPointerExited(object sender, PointerRoutedEventArgs e) => SetHoverOpacity(sender, 0);

    private static void SetHoverOpacity(object sender, double opacity)
    {
        if (sender is Panel panel && panel.Children.Count > 0 && panel.Children[0] is Border hover)
        {
            hover.Opacity = opacity;
        }
    }

    /// <summary>
    /// 把这一组的卡片整理成列表要装的项。
    /// </summary>
    /// <remarks>
    /// 只有单曲模块需要加工：接口给的是一整片曲目，「每列几首」是排版决定，
    /// 先在这里切成列（<see cref="HomeTrackColumn"/>）。其余三种排法一项就是一张卡片。
    /// </remarks>
    private IReadOnlyList<object> BuildItems()
    {
        var cards = Section?.Cards ?? [];

        if (Section?.Layout != HomeSectionLayout.TrackColumns) return [.. cards.Cast<object>()];

        var columns = new List<object>();
        for (var i = 0; i < cards.Count; i += TracksPerColumn)
        {
            columns.Add(new HomeTrackColumn([.. cards.Skip(i).Take(TracksPerColumn)]));
        }

        return columns;
    }

    /// <summary>
    /// 按排法选卡片模板。
    /// </summary>
    /// <remarks>
    /// 同一模块里卡片形态是一致的，所以选模板这件事由这一组的 Layout 一处决定，
    /// 不必再写一个按项类型分派的 <c>DataTemplateSelector</c>。
    /// </remarks>
    private void ApplyItemTemplate()
    {
        ItemsList.ItemTemplate = Section?.Layout switch
        {
            HomeSectionLayout.PlaylistMosaic => (DataTemplate)Resources["MosaicCardTemplate"],
            HomeSectionLayout.PlaylistPreview => (DataTemplate)Resources["PreviewCardTemplate"],
            HomeSectionLayout.TrackColumns => (DataTemplate)Resources["TrackColumnTemplate"],
            _ => (DataTemplate)Resources["CoverCardTemplate"],
        };
    }

    private static void OnSectionChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not HomeSectionView view) return;

        view._items = view.BuildItems();
        view.ApplyItemTemplate();

        // 换了一组就回第一页。这里不能等宽度：控件不可见时宽度一直是 0，
        // 不复位的话下次量到宽度会用上一组的页号当锚点。
        view.SyncPage(resetToFirst: true);
    }

    /// <summary>点整张卡片（歌单卡）。单曲列不走这里 —— 那边是整列一个项。</summary>
    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is HomeCard card)
        {
            CardInvoked?.Invoke(this, card);
        }
    }

    /// <summary>点单曲列里的某一行。要播的是这一行，不是整列。</summary>
    private void OnTrackTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: HomeCard card })
        {
            CardInvoked?.Invoke(this, card);
        }
    }
}
