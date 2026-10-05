using System.Collections.ObjectModel;
using Bodian.WinUI.Playback;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 右侧播放队列抽屉的状态与动作。**单例**，与主窗口同寿命。
/// </summary>
/// <remarks>
/// <b>整表重建，不做增量同步。</b> 队列变动不频繁（换页、增删、切歌），
/// 而增量同步要自己维护一套「哪一行对应哪个下标」的影子状态 ——
/// 队列那边刚因为下标重映射踩过一遍，这里没必要再来一次。
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
    public void Remove(int position) => _ = _coordinator.RemoveQueueItemAsync(position);

    /// <summary>清空队列。确认框由界面负责，这里只管执行。</summary>
    public void Clear() => _coordinator.ClearQueue();

    /// <summary>
    /// 把第 <paramref name="from"/> 首挪到第 <paramref name="to"/> 位（挪完后的最终下标）。
    /// </summary>
    /// <remarks>
    /// 行号与行对象靠 <c>Changed → RefreshIfOpen</c> 自动重建，不用在这里同步维护 ——
    /// 那次重建排在 dispatcher 下一轮，正好落在触发它的那个指针事件处理之后。
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
}
