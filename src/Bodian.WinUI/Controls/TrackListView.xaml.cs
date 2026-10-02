using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
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

    private INotifyCollectionChanged? _observed;
    private PlayerViewModel? _nowPlaying;

    public TrackListView()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>用户点了某一行。参数是被点的曲目。</summary>
    public event EventHandler<Track>? TrackInvoked;

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

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((TrackListView)d).Observe(e.NewValue as INotifyCollectionChanged);

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
        // 而「加载更多」走的是追加这条路径，不受影响。
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is not null && Rows.Count > 0)
        {
            foreach (var item in e.NewItems)
            {
                if (ToRow(item, Rows.Count + 1) is { } row)
                {
                    // 追加只初始化新行；每个 CollectionChanged 都扫描已加载的
                    // 全部曲目会让分页开销随总结果数平方增长。
                    row.IsCurrent = row.Source.Id == _nowPlaying?.CurrentTrackId;
                    Rows.Add(row);
                }
            }

            return;
        }

        Rebuild();
    }

    private void Rebuild()
    {
        Rows.Clear();

        if (ItemsSource is IEnumerable items)
        {
            var ordinal = 1;

            foreach (var item in items)
            {
                if (ToRow(item, ordinal) is { } row)
                {
                    Rows.Add(row);
                    ordinal++;
                }
            }
        }

        // 重建时每一行都是新对象，IsPointerOver 天然回到 false ——
        // 容器回收时 PointerExited 可能没送达，留下的悬停态就是这么清掉的。
        SyncCurrent();
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
        if (args.InRecycleQueue && args.Item is TrackRow row) row.IsPointerOver = false;
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TrackRow row)
        {
            TrackInvoked?.Invoke(this, row.Source);
        }
    }
}
