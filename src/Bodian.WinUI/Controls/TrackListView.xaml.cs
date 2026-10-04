using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using Bodian.Core.Models;
using Bodian.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Bodian.WinUI.Controls;

/// <summary>
/// 曲目列表。搜索页、我喜欢的页、歌单详情页等共用。
/// </summary>
/// <remarks>
/// <para>
/// <b>点行只报 <see cref="Track"/>，不报下标。</b> 队列要的是「当前列表里的第几首」，
/// 而下标取决于页面用的是哪个集合（搜索结果还是歌单曲目），控件不该知道 ——
/// 页面拿到曲目后自己 <c>IndexOf</c>。
/// </para>
/// <para>
/// <b>对外契约没有变</b>：<see cref="ItemsSource"/> 收 <c>IEnumerable&lt;Track&gt;</c>，
/// 内部投影成 <see cref="TrackRow"/> 只是为了拿到序号与「正在播放」两个可绑定状态。
/// 调用方（6 个页面）一行都不用改。
/// </para>
/// </remarks>
public sealed partial class TrackListView : UserControl
{
    /// <summary>要显示的曲目集合。</summary>
    /// <remarks>
    /// 类型是 <see cref="object"/> 而不是 <c>IEnumerable&lt;Track&gt;</c>：
    /// <c>x:Bind</c> 到这里只做赋值，集合本身由控件消费，
    /// 收紧类型只会在 XAML 侧多一层转换。
    /// </remarks>
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(object),
            typeof(TrackListView),
            new PropertyMetadata(null, OnItemsSourceChanged));

    /// <summary>
    /// 「正在播放」的来源。不设的话在 <c>Loaded</c> 时回落到
    /// <c>Application.Current.Resources["BodianNowPlaying"]</c>。
    /// </summary>
    /// <remarks>
    /// <b>这是一处刻意的局部例外。</b> 控件的构造函数必须无参（XAML 实例化要求），
    /// 拿不到 DI 容器里的 <see cref="PlayerViewModel"/>。要让 6 个页面各自把它传进来，
    /// 就得改 6 份 XAML + 6 个 ViewModel；这里改为由宿主在 App 资源里放一份引用，
    /// 控件自己去取。显式传 <see cref="NowPlaying"/> 仍然优先。
    /// </remarks>
    public static readonly DependencyProperty NowPlayingProperty =
        DependencyProperty.Register(
            nameof(NowPlaying),
            typeof(PlayerViewModel),
            typeof(TrackListView),
            new PropertyMetadata(null));

    /// <summary>还有没有下一页。转给内部列表的 <see cref="AutoPaging.HasMoreProperty"/>。</summary>
    public static readonly DependencyProperty HasMoreProperty =
        DependencyProperty.Register(
            nameof(HasMore),
            typeof(bool),
            typeof(TrackListView),
            new PropertyMetadata(false));

    /// <summary>滚到末尾时执行。转给内部列表的 <see cref="AutoPaging.CommandProperty"/>。</summary>
    /// <remarks>
    /// 转发而不是让页面自己去 <see cref="AutoPaging"/> 上挂：页面只跟这个包装控件打交道，
    /// 不必知道它内部用的是哪个列表、也不必去猜视觉树结构。
    /// </remarks>
    public static readonly DependencyProperty LoadMoreCommandProperty =
        DependencyProperty.Register(
            nameof(LoadMoreCommand),
            typeof(ICommand),
            typeof(TrackListView),
            new PropertyMetadata(null));

    /// <summary>列表内容之后的附加内容，页面拿它放「没有更多了哦~」与失败重试。</summary>
    /// <remarks>
    /// <b>转给内部列表的 <c>Footer</c>，让它跟着列表一起滚。</b> 钉在页面底部的话，
    /// 提示与列表末尾之间会隔着一大片空白，看不出「是这个列表到底了」。
    /// </remarks>
    public static readonly DependencyProperty FooterProperty =
        DependencyProperty.Register(
            nameof(Footer),
            typeof(object),
            typeof(TrackListView),
            new PropertyMetadata(null, OnFooterChanged));

    /// <summary>
    /// 列表是不是处在多选态。置 <c>false</c> 会连选择一起清掉（「退出多选 = 取消选择」）。
    /// </summary>
    /// <remarks>
    /// 双向：工具栏写它进/出多选，页面也可以绑出去显示状态。
    /// </remarks>
    public static readonly DependencyProperty IsSelectionModeProperty =
        DependencyProperty.Register(
            nameof(IsSelectionMode),
            typeof(bool),
            typeof(TrackListView),
            new PropertyMetadata(false, OnIsSelectionModeChanged));

    /// <summary>已选条数。工具栏的「已选 N 首」绑它。</summary>
    /// <remarks>
    /// <b>对外只读靠 <c>private set</c>，不用 <c>RegisterReadOnly</c></b>：后者要的
    /// <c>DependencyPropertyKey</c> 这个 WinUI 版本里取不到（仓库里也没有先例）。
    /// <c>x:Bind</c> 的 OneWay 只读不写，这样够用。
    /// </remarks>
    public static readonly DependencyProperty SelectionCountProperty =
        DependencyProperty.Register(
            nameof(SelectionCount), typeof(int), typeof(TrackListView), new PropertyMetadata(0));

    /// <summary>已加载的行是不是全被选上了（列表为空时为 <c>false</c>）。「全选 / 取消全选」按钮据此切文案。</summary>
    public static readonly DependencyProperty IsAllSelectedProperty =
        DependencyProperty.Register(
            nameof(IsAllSelected), typeof(bool), typeof(TrackListView), new PropertyMetadata(false));

    private INotifyCollectionChanged? _observed;
    private PlayerViewModel? _nowPlaying;

    /// <summary>
    /// 选中的行。
    /// </summary>
    /// <remarks>
    /// <b>存行对象，不存曲目 id。</b> id 有两点不够：<c>Id &lt;= 0</c> 的行（搜索页的性能样本、
    /// 播放历史重建的 Track）根本没法建键；同一首歌在列表里出现两次时按 id 存会两行一起选中。
    /// 代价是列表重建后引用失效 —— 那正是「刷新要退出多选」这条约定的由来，见 <see cref="Rebuild"/>。
    /// </remarks>
    private readonly HashSet<TrackRow> _selected = [];

    public TrackListView()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>用户点了某一行。参数是被点的曲目。<b>多选态下不触发</b>，那时点行是勾选。</summary>
    public event EventHandler<Track>? TrackInvoked;

    /// <summary>选中项或选择模式变了。</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>投影出来的行。绑到内部 <c>ListView.ItemsSource</c>。</summary>
    public ObservableCollection<TrackRow> Rows { get; } = [];

    public object? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public PlayerViewModel? NowPlaying
    {
        get => (PlayerViewModel?)GetValue(NowPlayingProperty);
        set => SetValue(NowPlayingProperty, value);
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

    public bool IsSelectionMode
    {
        get => (bool)GetValue(IsSelectionModeProperty);
        set => SetValue(IsSelectionModeProperty, value);
    }

    public int SelectionCount
    {
        get => (int)GetValue(SelectionCountProperty);
        private set => SetValue(SelectionCountProperty, value);
    }

    public bool IsAllSelected
    {
        get => (bool)GetValue(IsAllSelectedProperty);
        private set => SetValue(IsAllSelectedProperty, value);
    }

    /// <summary>
    /// 当前列表里的全部曲目，按显示顺序。
    /// </summary>
    /// <remarks>
    /// 工具栏的「全部加入播放列表」用它 —— <b>只算已经加载进来的</b>，不再回头翻页拉全，
    /// 所以按钮是瞬时响应的；提示里会带上真实条数，不让用户以为加的是整个歌单。
    /// </remarks>
    public IReadOnlyList<Track> SourceTracks
    {
        get
        {
            var tracks = new List<Track>(Rows.Count);

            foreach (var row in Rows)
            {
                tracks.Add(row.Source);
            }

            return tracks;
        }
    }

    /// <summary>被勾上的曲目，<b>按列表顺序</b>而不是点击顺序 —— 加进歌单后看到的是列表原本的次序。</summary>
    public IReadOnlyList<Track> GetSelectedTracks()
    {
        var tracks = new List<Track>(_selected.Count);

        foreach (var row in Rows)
        {
            if (_selected.Contains(row))
            {
                tracks.Add(row.Source);
            }
        }

        return tracks;
    }

    /// <summary>全选<b>已加载</b>的行。之后滚出来的新行不会被自动选上，计数会跟着变大。</summary>
    public void SelectAll()
    {
        foreach (var row in Rows)
        {
            _selected.Add(row);
            row.IsSelected = true;
        }

        SyncSelectionState();
    }

    /// <summary>清空选择，<b>但不退出多选态</b>。</summary>
    public void ClearSelection()
    {
        if (_selected.Count == 0)
        {
            return;
        }

        _selected.Clear();

        foreach (var row in Rows)
        {
            row.IsSelected = false;
        }

        SyncSelectionState();
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((TrackListView)d).Observe(e.NewValue as INotifyCollectionChanged);

    // 直接推给内部列表，不走 x:Bind：Footer 属性元素与子内容的赋值顺序在 XAML 里没有保证，
    // OneTime 绑定时可能还没轮到 Footer。
    private static void OnFooterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((TrackListView)d).List.Footer = e.NewValue;

    private void Observe(INotifyCollectionChanged? source)
    {
        if (_observed is not null)
        {
            _observed.CollectionChanged -= OnSourceCollectionChanged;
        }

        _observed = source;

        if (_observed is not null)
        {
            _observed.CollectionChanged += OnSourceCollectionChanged;
        }

        Rebuild();
    }

    private void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // 只有「追加」能安全地增量处理：序号不变、滚动位置不丢。
        //
        // 其余动作（删除 / 移动 / 重置）都会让后面所有行的序号变掉，而序号是 init 属性，
        // 改不了 —— 所以整表重建。重建会重置滚动位置，但那几种情况本来就伴随列表大改，
        // 而滚动续加载走的是追加这条路径，不受影响。
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is not null && Rows.Count > 0)
        {
            foreach (var item in e.NewItems)
            {
                if (ToRow(item, Rows.Count + 1) is { } row)
                {
                    // 追加只初始化新行；每个 CollectionChanged 都扫描已加载的
                    // 全部曲目会让分页开销随总结果数平方增长。
                    row.IsCurrent = row.Source.Id == _nowPlaying?.CurrentTrackId;
                    row.IsSelectionMode = IsSelectionMode;
                    Rows.Add(row);
                }
            }

            // 行数变了，「是不是全选」可能跟着变（新行默认没选中）。
            SyncSelectionState();

            return;
        }

        Rebuild();
    }

    private void Rebuild()
    {
        // 重建会换掉全部 TrackRow，选择集合里存的旧引用随之失效 —— 直接丢掉。
        // 这也是「刷新前必须先退出多选」那条约定的由来：留着计数就是错的。
        _selected.Clear();
        Rows.Clear();

        if (ItemsSource is IEnumerable items)
        {
            var ordinal = 1;

            foreach (var item in items)
            {
                if (ToRow(item, ordinal) is { } row)
                {
                    // 重建时列表可能正处在多选态（换了 ItemsSource 但没退出），新行要跟上。
                    row.IsSelectionMode = IsSelectionMode;
                    Rows.Add(row);
                    ordinal++;
                }
            }
        }

        // 重建时每一行都是新对象，IsPointerOver 天然回到 false ——
        // 容器回收时 PointerExited 可能没送达，留下的悬停态就是这么清掉的。
        SyncCurrent();
        SyncSelectionState();
    }

    /// <summary>
    /// 把一个列表项投影成行。
    /// </summary>
    /// <remarks>
    /// 两种数据源都收：曲目列表按顺序编号；榜单条目用自带的 <c>Rank</c> 且不补零
    /// （「第 1 名」写成 <c>01</c> 是错的）。这样榜单详情页也能直接用这个控件，
    /// 而不必维护第二套行模板。
    /// </remarks>
    private static TrackRow? ToRow(object? item, int ordinal) => item switch
    {
        RankedTrack ranked => new TrackRow
        {
            Source = ranked.Track,
            Ordinal = ranked.Rank,
            PadOrdinal = false,
        },
        Track track => new TrackRow { Source = track, Ordinal = ordinal },
        _ => null,
    };

    // ── 正在播放 ────────────────────────────────────────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 显式指定的优先；没指定就回落到宿主放进 App 资源的那一份。
        var target = NowPlaying
            ?? Application.Current.Resources["BodianNowPlaying"] as PlayerViewModel;

        if (!ReferenceEquals(target, _nowPlaying))
        {
            if (_nowPlaying is not null)
            {
                _nowPlaying.PropertyChanged -= OnNowPlayingChanged;
            }

            _nowPlaying = target;

            if (_nowPlaying is not null)
            {
                _nowPlaying.PropertyChanged += OnNowPlayingChanged;
            }
        }

        SyncCurrent();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_nowPlaying is not null)
        {
            _nowPlaying.PropertyChanged -= OnNowPlayingChanged;
            _nowPlaying = null;
        }
    }

    private void OnNowPlayingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlayerViewModel.CurrentTrackId))
        {
            SyncCurrent();
        }
    }

    /// <summary>
    /// 把「当前是哪一首」刷到各行上。
    /// </summary>
    /// <remarks>
    /// 直接线性扫一遍，不去遍历可视化树找容器 —— 后者是 O(可见行数) 且要和虚拟化回收赛跑，
    /// 而这里 n 只是列表长度，且每次切歌才触发一次。
    /// </remarks>
    private void SyncCurrent()
    {
        var current = _nowPlaying?.CurrentTrackId;

        foreach (var row in Rows)
        {
            row.IsCurrent = current is not null && row.Source.Id == current;
        }
    }

    // ── 行交互 ──────────────────────────────────────────────────────────────

    private void OnRowPointerEntered(object sender, PointerRoutedEventArgs e) => SetPointerOver(sender, true);

    private void OnRowPointerExited(object sender, PointerRoutedEventArgs e) => SetPointerOver(sender, false);

    private static void SetPointerOver(object sender, bool value)
    {
        if (sender is FrameworkElement { DataContext: TrackRow row })
        {
            row.IsPointerOver = value;
        }
    }

    private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        // 回收时要连菜单状态一起清：容器接着会去装别的曲目，
        // 留着 IsMenuOpen 会让那一行的「更多」按钮一直显示。
        //
        // ★ 唯独**不要清 IsSelected**。它是行（数据对象）的状态，不是容器的 ——
        //   容器去装另一行时 x:Bind 自然重读那一行的值，本来就不会串。
        //   顺手清一下反而会把滚出视野的勾选全丢掉。
        if (args.InRecycleQueue && args.Item is TrackRow row)
        {
            row.IsPointerOver = false;
            row.IsMenuOpen = false;
        }
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not TrackRow row)
        {
            return;
        }

        // 多选态下点行是勾选，不是播放 —— 这里不抛 TrackInvoked，
        // 否则页面收到会走成「播这一首」，勾选变成误播。
        if (IsSelectionMode)
        {
            ToggleSelection(row);
            return;
        }

        TrackInvoked?.Invoke(this, row.Source);
    }

    // ── 选择 ────────────────────────────────────────────────────────────────

    private static void OnIsSelectionModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((TrackListView)d).ApplySelectionMode();

    /// <summary>
    /// 把多选态下发给每一行。进出多选都清空选择 —— 进多选从零开始，退多选本来就该丢掉。
    /// </summary>
    private void ApplySelectionMode()
    {
        var on = IsSelectionMode;

        _selected.Clear();

        foreach (var row in Rows)
        {
            row.IsSelectionMode = on;
            row.IsSelected = false;
        }

        SyncSelectionState();
    }

    private void ToggleSelection(TrackRow row)
    {
        if (!_selected.Remove(row))
        {
            _selected.Add(row);
        }

        row.IsSelected = _selected.Contains(row);
        SyncSelectionState();
    }

    /// <summary>把计数、「是不是全选」刷出去，并通知订阅方。</summary>
    private void SyncSelectionState()
    {
        SelectionCount = _selected.Count;
        IsAllSelected = Rows.Count > 0 && _selected.Count == Rows.Count;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}
