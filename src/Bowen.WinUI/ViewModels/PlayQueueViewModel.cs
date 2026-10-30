using System.Collections.ObjectModel;
using Bowen.Core.Models;
using Bowen.WinUI.Playback;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;

namespace Bowen.WinUI.ViewModels;

/// <summary>
/// 右侧播放队列抽屉的状态与动作。**单例**，与主窗口同寿命。
/// </summary>
/// <remarks>
/// <b>集合的变更尽量就地做，不整表重建。</b> 整表重建（清空再填满）会让 ListView 的滚动位置
/// 弹回顶部：点行切歌、删一首、拖动重排都会被写成「刷了一下、回到最上面」。
/// 只有「条目真的变了、且不是单删/单挪」时才重建（换页、批量增删、随机重洗）。
/// <para>
/// <b>三种就地路径</b>：条目没动只改 <c>IsCurrent</c> / <c>CanReorder</c>；
/// 少了一首就 <c>RemoveAt</c>；只挪了一首就 <c>Move</c>。
/// 增量同步要自己维护「哪一行对应哪个下标」的影子状态，但队列那边已经因为下标重映射踩过一遍，
/// 这里的判据只认引用比对，不做影子状态。
/// </para>
/// </remarks>
public sealed partial class PlayQueueViewModel : ObservableObject
{
    private readonly PlaybackCoordinator _coordinator;

    /// <summary>构造时所在的线程就是界面线程：这个 VM 由 DI 在装配主窗口时创建。</summary>
    private readonly DispatcherQueue? _dispatcher = DispatcherQueue.GetForCurrentThread();

    public PlayQueueViewModel(PlaybackCoordinator coordinator)
    {
        ArgumentNullException.ThrowIfNull(coordinator);

        _coordinator = coordinator;
        _coordinator.Queue.Changed += (_, _) => RefreshIfOpen();
    }

    public ObservableCollection<PlayQueueRow> Rows { get; } = [];

    [ObservableProperty]
    public partial bool IsEmpty { get; set; } = true;

    [ObservableProperty]
    public partial string CountText { get; set; } = "";

    /// <summary>
    /// 抽屉是不是开着。由界面（主窗口）置位，抽屉的显隐直接绑它。
    /// </summary>
    /// <remarks>
    /// <b>开着的时候才重建列表</b>：队列每次切歌都会触发 <c>Changed</c>，
    /// 而重建会清空再填满 <c>ObservableCollection</c>、让 <c>ListView</c> 整个重新测量 ——
    /// 抽屉没拉开就没人看，这份力气白花。
    /// </remarks>
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    partial void OnIsOpenChanged(bool value)
    {
        // 打开时补一次：关着的那段时间队列可能已经变过好几轮了。
        if (value)
        {
            Refresh();
        }
    }

    /// <summary>
    /// 小窗那条播放队列面板是不是开着。
    /// </summary>
    /// <remarks>
    /// <b>与 <see cref="IsOpen"/> 分开是必须的，不是一个布尔抄了两遍。</b>
    /// <see cref="IsOpen"/> 被主窗口的抽屉独占（抽屉的显隐直接绑它），而小窗与主窗口
    /// 可能同时开着：小窗的面板去改 <see cref="IsOpen"/> 会把主窗口的抽屉一起拉开；
    /// 反过来不改，<see cref="RefreshIfOpen"/> 会早早返回，小窗面板里显示的是陈旧队列。
    /// 两个开关共用同一份 <see cref="Rows"/>，所以两处看到的队列仍然一致。
    /// </remarks>
    [ObservableProperty]
    public partial bool IsMiniPlayerOpen { get; set; }

    partial void OnIsMiniPlayerOpenChanged(bool value)
    {
        // 理由同 OnIsOpenChanged：打开时补一次。
        if (value)
        {
            Refresh();
        }
    }

    /// <summary>跳到第 <paramref name="position"/> 首并开始播。</summary>
    public void Play(int position) => _ = _coordinator.PlayQueueItemAsync(position);

    /// <summary>从队列里删掉第 <paramref name="position"/> 首。</summary>
    public Task RemoveAsync(int position) => _coordinator.RemoveQueueItemAsync(position);

    /// <summary>删除入口的同步包装；界面要等退场动画时直接调 <see cref="RemoveAsync"/>。</summary>
    public void Remove(int position) => _ = RemoveAsync(position);

    /// <summary>
    /// 删除并立刻同步 <see cref="Rows"/>，供界面在退场动画结束后调用。
    /// </summary>
    /// <remarks>
    /// <b>与 <see cref="RemoveAsync"/> 的区别只是不等 dispatcher 下一轮。</b>
    /// 平时改条目要排下一轮，是因为队列很可能就在 ListView 的事件栈里被改；
    /// 这里调用方已经等完 170 ms 退场动画，不在那层事件栈里。同步删行才能让界面
    /// 在同一个 UI 回合里还原容器 —— 先还原再删，容器即使被回收也不会把透明度带走，
    /// 两者之间也没有布局/绘制把行闪出来。
    /// </remarks>
    public void RemoveNow(int position)
    {
        _ = _coordinator.RemoveQueueItemAsync(position);
        Refresh();
    }

    /// <summary>清空队列。确认框由界面负责，这里只管执行。</summary>
    public void Clear() => _coordinator.ClearQueue();

    /// <summary>
    /// 把第 <paramref name="from"/> 首挪到第 <paramref name="to"/> 位（挪完后的最终下标）。
    /// </summary>
    /// <remarks>
    /// 行号与行对象靠 <c>Changed → RefreshIfOpen</c> 自动同步，不用在这里维护 ——
    /// 那次同步排在 dispatcher 下一轮，正好落在触发它的那个指针事件处理之后。
    /// <see cref="Refresh"/> 认得「只挪了一行」这种变更，会就地 <c>Move</c>，不重建集合
    /// （重建会把滚动位置弹回顶部，见 <see cref="TryApplyMove"/>）。
    /// </remarks>
    public void Move(int from, int to) => _ = _coordinator.MoveQueueItem(from, to);

    private void RefreshIfOpen()
    {
        // 两个宿主任意一个开着就要重建 —— 见 IsMiniPlayerOpen 的说明。
        if (!IsOpen && !IsMiniPlayerOpen)
        {
            return;
        }

        // 队列很可能就是在面板自己的事件处理里被改的（点行切歌、点行尾的删除）。
        // 同步重建等于在 ItemsControl 处理事件的过程中把它脚下的集合抽掉，所以排到下一轮再重建。
        if (_dispatcher is null || !_dispatcher.TryEnqueue(Refresh))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        var queue = _coordinator.Queue;
        var current = queue.CurrentIndex;
        var canReorder = _coordinator.CanReorderQueue;

        // 只删了一行时也只删对应的行，保留其余容器与滚动位置。
        if (TryApplyRemoval(queue, current, canReorder))
        {
            return;
        }

        // 只挪了一行（拖动排序）时原地 Move，保留滚动位置。
        if (TryApplyMove(queue, current, canReorder))
        {
            return;
        }

        // 队列条目没动时只改行上的状态，不重建。
        // ★ 不能随手整表重建：集合一清空，ListView 的滚动位置就弹回顶部 ——
        //   点行切歌、一首放完自动续播、以及只切播放模式都会走到这里。
        //   切模式只影响 CanReorder（拖动条显隐），它现在是可通知属性，就地改就行。
        if (CanReuseRows(queue))
        {
            for (var i = 0; i < Rows.Count; i++)
            {
                Rows[i].IsCurrent = i == current;
                Rows[i].CanReorder = canReorder;
            }

            return;
        }

        Rows.Clear();

        for (var i = 0; i < queue.Count; i++)
        {
            Rows.Add(new PlayQueueRow
            {
                Source = queue.Items[i],
                Position = i,
                IsCurrent = i == current,
                CanReorder = canReorder,
            });
        }

        IsEmpty = Rows.Count == 0;
        CountText = Rows.Count == 0 ? "" : $"共 {Rows.Count} 首";
    }

    /// <summary>
    /// 队列只少了一首时，就地删掉对应那一行并更新后面的行号。
    /// </summary>
    /// <remarks>
    /// <b>不整表重建的原因与切歌相同：</b> 清空集合会让 ListView 的滚动位置弹回顶部，
    /// 用户删中间某首时会被直接甩回队首。这里先确认除这一行外其余引用都能按位移对上；
    /// 对不上（同时发生重排、清空等）才让 <see cref="Refresh"/> 走整表重建。
    /// </remarks>
    private bool TryApplyRemoval(PlayQueue queue, int current, bool canReorder)
    {
        if (Rows.Count != queue.Count + 1)
        {
            return false;
        }

        var removed = -1;

        for (var i = 0; i < queue.Count; i++)
        {
            if (!ReferenceEquals(Rows[i].Source, queue.Items[i]))
            {
                removed = i;
                break;
            }
        }

        if (removed < 0)
        {
            removed = Rows.Count - 1;
        }

        for (var i = removed; i < queue.Count; i++)
        {
            if (!ReferenceEquals(Rows[i + 1].Source, queue.Items[i]))
            {
                return false;
            }
        }

        Rows.RemoveAt(removed);

        for (var i = 0; i < Rows.Count; i++)
        {
            Rows[i].Position = i;
            Rows[i].IsCurrent = i == current;
            Rows[i].CanReorder = canReorder;
        }

        IsEmpty = Rows.Count == 0;
        CountText = Rows.Count == 0 ? "" : $"共 {Rows.Count} 首";
        return true;
    }

    /// <summary>
    /// 队列里只有一行换了位置时，就地 <c>Move</c>，不整表重建。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>拖动排序走这条。</b> 整表重建（清空再填满）会让 ListView 的滚动位置弹回顶部 ——
    /// 用户把一首歌拖到列表末尾、松手却被甩回队首。就地 <c>Move</c> 只发一次
    /// <c>NotifyCollectionChangedAction.Move</c>，条数不变，列表照旧停在原处。
    /// </para>
    /// <para>
    /// <b>判据与 <see cref="TryApplyRemoval"/> 同一套：只认引用。</b> 先找第一处对不上的下标，
    /// 用相邻那一格判断是往前往后挪，再把被挪的那行在两边的下标都找出来；
    /// 最后整表逐格验证「恰好是一次抽出再插入」的结果。对不上（同时增删、换页、重新洗牌）
    /// 就返回 <c>false</c>，让 <see cref="Refresh"/> 走整表重建。
    /// </para>
    /// </remarks>
    private bool TryApplyMove(PlayQueue queue, int current, bool canReorder)
    {
        if (Rows.Count == 0 || Rows.Count != queue.Count)
        {
            return false;
        }

        var first = -1;

        for (var i = 0; i < Rows.Count; i++)
        {
            if (!ReferenceEquals(Rows[i].Source, queue.Items[i]))
            {
                first = i;
                break;
            }
        }

        // 逐格引用都对得上：条目没动，交给 CanReuseRows 那条路。
        if (first < 0)
        {
            return false;
        }

        // 往后挪时，旧第 first 行让位给旧第 first+1 行；往前挪时反过来。
        var forward = first + 1 < Rows.Count
            && ReferenceEquals(Rows[first + 1].Source, queue.Items[first]);

        var from = forward ? first : IndexOfSource(Rows, queue.Items[first]);
        var to = forward ? IndexOfSource(queue.Items, Rows[first].Source) : first;

        if (from < 0 || to < 0 || from == to)
        {
            return false;
        }

        for (var i = 0; i < Rows.Count; i++)
        {
            var source = i == to ? from
                : from < to && i >= from && i < to ? i + 1
                : from > to && i > to && i <= from ? i - 1
                : i;

            if (!ReferenceEquals(Rows[source].Source, queue.Items[i]))
            {
                return false;
            }
        }

        Rows.Move(from, to);

        for (var i = 0; i < Rows.Count; i++)
        {
            Rows[i].Position = i;
            Rows[i].IsCurrent = i == current;
            Rows[i].CanReorder = canReorder;
        }

        return true;
    }

    private static int IndexOfSource(IReadOnlyList<PlayQueueRow> rows, Track source)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (ReferenceEquals(rows[i].Source, source))
            {
                return i;
            }
        }

        return -1;
    }

    private static int IndexOfSource(IReadOnlyList<Track> items, Track source)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (ReferenceEquals(items[i], source))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// 现有这些行还能不能接着用，不必重建。
    /// </summary>
    /// <remarks>
    /// <b>按下标逐个比引用。</b> 增删、换页会让某一格对不上，于是整表重建；
    /// 只动游标或只切播放模式的变更则每一格都对得上 —— 行号
    /// （<see cref="PlayQueueRow.Position"/>）没变，<c>CanReorder</c> 就地更新。
    /// 单删与单挪（拖动排序）在此之前已经由各自的路径处理掉，走不到这里。
    /// </remarks>
    private bool CanReuseRows(PlayQueue queue)
    {
        if (Rows.Count != queue.Count)
        {
            return false;
        }

        for (var i = 0; i < Rows.Count; i++)
        {
            if (!ReferenceEquals(Rows[i].Source, queue.Items[i]))
            {
                return false;
            }
        }

        return true;
    }
}
