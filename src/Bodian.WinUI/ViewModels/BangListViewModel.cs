using System.Collections.ObjectModel;
using System.ComponentModel;
using Bodian.Core.Api;
using Bodian.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 榜列表页里的一个榜。
/// </summary>
/// <remarks>
/// <b>曲目直接用首页带来的预览，不为每个榜单独发请求。</b>
/// 完整榜单（实测 100 首）在 <c>service/bang/{id}/musics</c>，由详情页去取。
/// <para>
/// 一开始的设计是「每个榜再请求一次 <c>rn=10</c> 拿前十首」，那要 20 次请求才填满一页 ——
/// 既违背本项目「别把服务当压测」的底线，又因为守卫写错而整个卡住。
/// 改用预览之后整页只要 1 次请求。
/// </para>
/// </remarks>
public sealed partial class BangItemViewModel : ObservableObject
{
    public BangItemViewModel(Bang bang)
    {
        ArgumentNullException.ThrowIfNull(bang);

        Bang = bang;

        // 卡片上只放前三名 —— 参考图就是三行，多出来的会把卡片撑高、各卡行距也不一致。
        // 接口每个榜本来就只带 5 首预览，第 4、5 首与完整 100 首都在详情页。
        var count = Math.Min(PreviewCount, bang.PreviewTracks.Count);
        for (var i = 0; i < count; i++)
        {
            // 名次就是位次：预览是从第 1 名开始的。
            // 名次不补零 —— 「第 1 名」写成 01 是错的（曲目列表才补零）。
            Tracks.Add(new TrackRow
            {
                Source = bang.PreviewTracks[i],
                Ordinal = i + 1,
                PadOrdinal = false,
            });
        }
    }

    /// <summary>卡片里显示的名次数。榜单预览实测 5 首，这里只取前三。</summary>
    private const int PreviewCount = 3;

    public Bang Bang { get; }

    /// <summary>
    /// 首页带来的预览里的前三首，带名次。
    /// </summary>
    /// <remarks>
    /// 用 <see cref="TrackRow"/> 而不是 <see cref="RankedTrack"/>：预览行也要显示
    /// 「正在播放」，而那是一个随外部状态变化的可绑定属性，record 装不下。
    /// 行模板与曲目列表共用同一套三态序号列。
    /// </remarks>
    public ObservableCollection<TrackRow> Tracks { get; } = [];

    /// <summary>
    /// 卡片上的三个名次位，供模板直接取用。
    /// </summary>
    /// <remarks>
    /// <b>为什么铺成三个具名属性，而不是在卡片里套一个 <c>ItemsControl</c></b>：
    /// 一页 22 张卡，每张卡里再放一个集合控件就是「嵌套的整组渲染」，
    /// 与 <c>ViewPerformanceContractTests</c> 把 <c>ItemsControl</c> 逐出这几个页面的理由相同。
    /// 卡片本来就只显示前三名，三个具名槽位既够用，也让固定卡高在结构上成立。
    /// <para>名次不足时对应槽位为 <c>null</c>，由 <see cref="Rank2Visibility"/> 一类收起。</para>
    /// </remarks>
    public TrackRow? Rank1 => Tracks.Count > 0 ? Tracks[0] : null;

    public TrackRow? Rank2 => Tracks.Count > 1 ? Tracks[1] : null;

    public TrackRow? Rank3 => Tracks.Count > 2 ? Tracks[2] : null;

    public Visibility Rank2Visibility => Formats.Visible(Rank2 is not null);

    public Visibility Rank3Visibility => Formats.Visible(Rank3 is not null);

    /// <summary>把「当前是哪一首」刷到预览行上。由页面在播放状态变化时调用。</summary>
    internal void SetCurrent(long? trackId)
    {
        foreach (var row in Tracks)
        {
            row.IsCurrent = trackId is not null && row.Source.Id == trackId;
        }
    }

    /// <summary>
    /// 指针是不是停在这张卡上。
    /// </summary>
    /// <remarks>
    /// <b>悬停底由卡片自己画</b>，不用 <c>GridViewItem</c> 自带的那层：
    /// 那层铺在整格上、又被卡片的不透明底色盖住，看不出效果（这个坑踩过）。
    /// 容器把它让出来（<c>Padding=0</c>），覆盖层贴着卡片边界，
    /// 于是它既不会被父容器裁掉，也不会漏到相邻卡片上。
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HoverOpacity))]
    public partial bool IsPointerOver { get; set; }

    /// <summary>悬停覆盖层的不透明度。0 或 1 之间切换，不做过渡。</summary>
    public double HoverOpacity => IsPointerOver ? 1 : 0;

    public string Name => Bang.Name;

    public Uri? CoverImage => Bang.CoverImage;

    /// <summary><c>09-30更新</c> 这类。缺了就不显示。</summary>
    public string UpdateText => Bang.UpdateText;
}

/// <summary>榜列表页里的一组（置顶位 / 热力榜 / …）。</summary>
/// <remarks>
/// <b>本身就是一个曲目集合</b>（继承 <see cref="ObservableCollection{T}"/>），
/// 因为 <c>GridView</c> 的分组要求每个组自己可枚举 —— 组名挂在 <see cref="Title"/> 上。
/// 分组靠 <c>CollectionViewSource.IsSourceGrouped</c>，见 <see cref="BangListViewModel.GroupedSections"/>。
/// <para>
/// <b>收起就是把自己清空、展开就是装回来</b>，不另做可见性标记。
/// 组空了不会被隐藏：<c>GroupStyle.HidesIfEmpty</c> 默认是 false，这是收起能成立的前提 ——
/// 表头必须留在原地，否则收起的组连点回去的地方都没有。
/// </para>
/// </remarks>
public sealed class BangSectionViewModel : ObservableCollection<BangItemViewModel>
{
    /// <summary>这一组里的全部榜。收起后仍留着，展开时原样装回。</summary>
    private readonly IReadOnlyList<BangItemViewModel> _all;

    private bool _isExpanded = true;

    public BangSectionViewModel(BangSection section)
        : base(Build(section))
    {
        _all = [.. this];
        Title = section.Title;
    }

    public string Title { get; }

    /// <summary>
    /// 这一组有几个榜，显示在表头右侧。
    /// </summary>
    /// <remarks>
    /// 取 <c>_all</c> 而不是当前集合：收起之后集合是空的，但表头该继续说自己有几个榜。
    /// 它是个定值，不随收起展开变，所以不需要通知。
    /// </remarks>
    public string CountText => $"{_all.Count} 个榜";

    /// <summary>是否展开。<b>由表头点击翻转</b>，见 <see cref="Toggle"/>。</summary>
    /// <remarks>
    /// 自己转发 <c>PropertyChanged</c> 而不是用 <c>[ObservableProperty]</c>：
    /// 那个源生成器要求基类是 <c>ObservableObject</c>，而这里必须继承
    /// <see cref="ObservableCollection{T}"/> 才能被当成分组。好在这个基类本来就有
    /// <c>OnPropertyChanged</c>。
    /// </remarks>
    public bool IsExpanded
    {
        get => _isExpanded;
        private set
        {
            if (_isExpanded == value)
            {
                return;
            }

            _isExpanded = value;
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsExpanded)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(IconGlyph)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(HeaderAutomationName)));
        }
    }

    /// <summary>表头左边的折叠箭头。展开时朝下、收起时朝右。</summary>
    public string IconGlyph => IsExpanded ? "\uE70D" : "\uE76C";

    /// <summary>
    /// 表头的无障碍名称。
    /// </summary>
    /// <remarks>
    /// 表头的内容是箭头 + 文字 + 数量，读屏念不出「点一下会怎样」，
    /// 所以显式给一个：既说清是哪一组，也说清当前是展开还是收起。
    /// </remarks>
    public string HeaderAutomationName => IsExpanded
        ? $"{Title}，已展开，点击收起"
        : $"{Title}，已收起，点击展开";

    public void Toggle()
    {
        if (IsExpanded)
        {
            Clear();
        }
        else
        {
            foreach (var bang in _all)
            {
                Add(bang);
            }
        }

        IsExpanded = !IsExpanded;
    }

    private static IReadOnlyList<BangItemViewModel> Build(BangSection section)
    {
        // 校验放在这里而不是构造函数体：base(...) 会先跑，那时 section 已经被解引用了。
        ArgumentNullException.ThrowIfNull(section);

        return [.. section.Bangs.Select(bang => new BangItemViewModel(bang))];
    }
}

/// <summary>
/// 排行榜页（侧栏的独立一项）。
/// </summary>
/// <remarks>
/// <para>
/// <b>整页只要一次请求</b>：<c>service/home/bangNew</c> 一次返回所有分组与每个榜的前几首预览。
/// 完整榜单在详情页取。所以这里**没有懒加载，也不分页**。
/// </para>
/// <para>
/// 预览的条数由服务端决定（实测 5 首），卡片上只显示前三名。要完整榜单就点整张卡进详情页。
/// </para>
/// <para>
/// 一页 22 个榜、每个榜一张卡，所以**不做分页**也就无所谓；
/// 卡片网格由 <c>GridView</c> 的 <c>ItemsWrapGrid</c> 回收，离屏卡片不建 XAML 子树。
/// </para>
/// </remarks>
public sealed partial class BangListViewModel : ObservableObject
{
    private readonly IBodianApi _api;
    private readonly ILogger<BangListViewModel> _logger;

    private bool _loaded;

    public BangListViewModel(IBodianApi api, ILogger<BangListViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(api);

        _api = api;
        _logger = logger ?? NullLogger<BangListViewModel>.Instance;

        // 在这里挂源，而不是等加载完：绑定是 OneTime 求值的，构造完就会读 GroupedSections.View，
        // 那时 Source 必须已经有值（没值的话 View 是 null，页面会空着）。之后往 Sections 里
        // 加项靠集合通知，不用再挂一次。
        _groupedSections.Source = Sections;
    }

    public ObservableCollection<BangSectionViewModel> Sections { get; } = [];

    private readonly CollectionViewSource _groupedSections = new() { IsSourceGrouped = true };

    /// <summary>
    /// 给 <c>GridView</c> 的分组源。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>分组必须走 <c>CollectionViewSource</c></b>：直接把 <see cref="Sections"/> 绑到
    /// <c>ItemsSource</c> 上，分组表头会被当成普通项塞进格子，而不是占满整行。
    /// 组本身可枚举是前提，见 <see cref="BangSectionViewModel"/>。
    /// </para>
    /// <para>
    /// <b>只能给 <c>View</c>，不能给 <c>CollectionViewSource</c> 本身。</b>
    /// UWP 时代两者都行，<b>WinUI 3 不行</b> —— 把 CVS 直接赋给 <c>ItemsSource</c> 会抛
    /// <c>ArgumentException: Value does not fall within the expected range</c>，
    /// 而且是在 XAML 绑定阶段抛，症状是「一点排行榜就闪退」。
    /// </para>
    /// </remarks>
    public ICollectionView GroupedSections => _groupedSections.View;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    /// <summary>首次进入时调。<b>已经加载过就什么都不做。</b></summary>
    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        IsBusy = true;
        StatusText = "正在加载排行榜…";

        try
        {
            var sections = await _api.GetBangSectionsAsync(cancellationToken).ConfigureAwait(true);

            Sections.Clear();
            foreach (var section in sections)
            {
                Sections.Add(new BangSectionViewModel(section));
            }

            var bangs = Sections.Sum(section => section.Count);

            StatusText = bangs == 0
                ? "这次没有取到榜单。"
                : $"{Sections.Count} 组、共 {bangs} 个榜";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载排行榜失败");

            // 失败要允许重试：把标志放回去，下次进来会再试一次。
            _loaded = false;
            StatusText = $"加载失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
