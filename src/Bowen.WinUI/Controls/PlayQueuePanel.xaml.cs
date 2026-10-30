using System.Collections.Specialized;
using System.Numerics;
using Bowen.WinUI.Services;
using Bowen.WinUI.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Bowen.WinUI.Controls;

/// <summary>
/// 播放队列抽屉的内容。宿主是 <c>MainWindow</c> 第 1 行覆盖层上那个 380 宽的卡片，
/// 开合、滑入动画与清空确认框都归主窗口管。
/// </summary>
/// <remarks>
/// <para>
/// 面板做四件事：点行切歌、删单曲、清空、<b>拖动手柄调整顺序</b>。
/// </para>
/// <para>
/// <b>拖拽是手写的</b>（<c>CapturePointer</c> + <c>PointerMoved</c> + 捕获丢失收尾），与项目里其它拖拽
/// 同一套做法。不用 WinUI 内置的 <c>CanReorderItems</c>：它会直接改写绑定的 <c>Rows</c>
/// （Remove + Add），而权威数据在 <c>PlayQueue</c> 里，两边会掰开。理由见 <c>docs/play-queue.md</c> §12。
/// </para>
/// <para>
/// <b>拖起来的是另画的一张卡片（<c>DragGhost</c>），不是被拖那一行本身。</b>
/// 把它原地抬起来需要在 <c>ItemsStackPanel</c> 里把某个 <c>ListViewItem</c> 提到最上层，而那个面板
/// 认不认 <c>Canvas.ZIndex</c> 没有可靠依据；画在 ListView 之后的同级卡片则天然在上层。
/// 被拖那一行原地隐掉（<c>Opacity = 0</c>），位置由让位的行补上，于是列表里空出来的就是落点。
/// </para>
/// <para>
/// <b>手柄只在鼠标悬停时出现与生效</b>，触屏不参与拖动 —— 手指按下去不会先悬停，
/// 要从触屏拖得另做长按手势。这是有意的。
/// </para>
/// </remarks>
public sealed partial class PlayQueuePanel : UserControl
{
    /// <summary>拖动的开判位移（DIP）。照 <c>LyricsCanvasView</c> 的抖动阈值，免得手一抖就把队列重排了。</summary>
    private const double DragThreshold = 4;

    /// <summary>让位的行滑到新位置用的时长。</summary>
    private static readonly TimeSpan ShiftDuration = TimeSpan.FromMilliseconds(140);

    /// <summary>删除一行时，向右抽出并淡出的时长。</summary>
    private static readonly TimeSpan RemoveDuration = TimeSpan.FromMilliseconds(180);

    /// <summary>删除时这一行向右抽出的距离。</summary>
    private const float RemoveSlideDistance = 36;

    /// <summary>
    /// 打开时定位最多重试几轮。
    /// </summary>
    /// <remarks>
    /// <b>不是「可能失败」的补丁，而是「不确定在第几拍」的等待。</b> <c>Loaded</c> 与
    /// 「列表量出可滚高度」不一定是同一次布局的事，见 <see cref="Reveal(int)"/>。
    /// </remarks>
    private const int RevealRetries = 2;

    /// <summary>卡片浮起来的高度，<c>ThemeShadow</c> 靠它投影。</summary>
    private const float GhostLift = 32;

    /// <summary>拖动时指针进入列表上下边缘这么多 DIP 内开始自动滚动。</summary>
    private const double AutoScrollZone = 36;

    /// <summary>自动滚动每帧最多走多少 DIP（16 ms 一帧）。</summary>
    private const double AutoScrollMaxStep = 16;

    private readonly ThemeShadow _ghostShadow = new();

    /// <summary>
    /// 删除后等待容器被回收的还原回调。键是行容器；容器被复用（<c>DataContext</c> 变化）
    /// 或卸载时摘掉。用它代替在容器上直接挂匿名回调，避免同一容器反复删除时越挂越多。
    /// </summary>
    private readonly Dictionary<ListViewItem, (TypedEventHandler<FrameworkElement, DataContextChangedEventArgs> Changed, RoutedEventHandler Unloaded)> _reuseWaiters = new();

    private PlayQueueViewModel? _attached;
    private uint? _dragPointerId;
    private ListViewItem? _dragContainer;
    private PlayQueueRow? _dragRow;
    private int _dragFrom = -1;
    private int _slot = -1;
    private double _originY;
    private double _grabOffset;
    private double _pitch;
    private bool _dragging;
    private ScrollViewer? _scroll;
    private DispatcherQueueTimer? _autoScrollTimer;
    private double _autoScrollStep;
    private double _pointerY;
    private double _contentTop = double.NaN;

    public PlayQueuePanel()
    {
        InitializeComponent();

        DragGhost.Shadow = _ghostShadow;
        DragGhost.Translation = new Vector3(0, 0, GhostLift);

        // ★ 捕获挂在 ListView 上，**不挂在握把上**：握把住在会被回收的 ListViewItem 里，
        //   拖拽途中 Rows 一旦重建／容器回收，捕获在握把上会随元素复用而语义漂移。
        //   handledEventsToo 兜底那些已经被 ScrollViewer / 容器标成 Handled 的移动事件。
        RowList.AddHandler(PointerMovedEvent, new PointerEventHandler(OnListPointerMoved), true);
        RowList.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnListPointerReleased), true);
        RowList.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnListPointerCaptureLost), true);
        RowList.AddHandler(PointerCanceledEvent, new PointerEventHandler(OnListPointerCanceled), true);

        // ★ 拖动时列表会随自动滚动回收容器。容器上的位移/透明度是元素自身的属性，
        //   不跟着行数据走 —— 复用去装另一行时必须按新行重算，否则会串到别的行上。
        RowList.ContainerContentChanging += OnContainerContentChanging;

        // 行列先藏起来：打开时要把它滑到「正在播放」那一行，定位完成前露出来的就是列表顶部。
        AppMotion.Hide(RowList);

        Loaded += (_, _) =>
        {
            AttachRows();
            RevealCurrent();
        };
        Unloaded += (_, _) => DetachRows();
    }

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(PlayQueueViewModel), typeof(PlayQueuePanel),
        new PropertyMetadata(null, OnViewModelChanged));

    public PlayQueueViewModel ViewModel
    {
        get => (PlayQueueViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    /// <summary>
    /// 正被拖着的那一行，给浮起来那张卡片当数据源。没在拖时为 <c>null</c>。
    /// </summary>
    /// <remarks>
    /// 做成依赖属性是为了让 <c>DragGhost</c> 能用 <c>x:Bind</c> 直接绑上去 ——
    /// 卡片是个独立的元素，拿不到列表行容器里的 <c>DataContext</c>。
    /// </remarks>
    public PlayQueueRow? DraggedRow
    {
        get => (PlayQueueRow?)GetValue(DraggedRowProperty);
        set => SetValue(DraggedRowProperty, value);
    }

    public static readonly DependencyProperty DraggedRowProperty = DependencyProperty.Register(
        nameof(DraggedRow), typeof(PlayQueueRow), typeof(PlayQueuePanel), new PropertyMetadata(null));

    /// <summary>点了「清空」。确认框由宿主弹，面板自己不弹。</summary>
    public event EventHandler? ClearRequested;

    /// <summary>点了右上角的收起。抽屉的开合由主窗口管。</summary>
    public event EventHandler? CloseRequested;

    private static void OnViewModelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var panel = (PlayQueuePanel)sender;

        panel.Bindings.Update();
        panel.AttachRows();
    }

    /// <summary>
    /// 盯着行集合：它被整体重建时把拖拽收掉。
    /// </summary>
    /// <remarks>
    /// 拖拽途中队列被别处改掉会走到这里（最典型的是正好一首歌放完、协调器自动续播，
    /// 于是 <c>Changed</c> → 下一轮 <c>Refresh</c> 把 <c>Rows</c> 清空重填）。
    /// 不收掉的话，手里那个 <c>_dragFrom</c> 已经是个旧行号，落点会算到别处去。
    /// </remarks>
    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e) => EndDrag();

    private void AttachRows()
    {
        var viewModel = ViewModel;

        if (ReferenceEquals(_attached, viewModel))
        {
            return;
        }

        DetachRows();

        _attached = viewModel;

        if (viewModel is not null)
        {
            viewModel.Rows.CollectionChanged += OnRowsChanged;
        }
    }

    private void DetachRows()
    {
        if (_attached is null)
        {
            return;
        }

        _attached.Rows.CollectionChanged -= OnRowsChanged;
        _attached = null;
    }

    // ── 打开时定位 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 打开时把正在播放那一行摆到<b>面板可视区的竖直正中</b>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>只在刚拉开时做一次。</b> 两个宿主都是每次打开新建一个面板（<c>MainWindow.ToggleQueue</c>
    /// 与 <c>MiniPlayerWindow.EnsureQueueView</c>），所以挂在 <c>Loaded</c> 上就等于「每次打开都定位」。
    /// 面板开着的时候不跟着切歌滚 —— 队列是给人翻的，自动滚会把正在看的位置顶走。
    /// </para>
    /// <para>
    /// <b>先把布局赶出来再定位。</b> <c>Loaded</c> 与「列表量出可滚高度」不一定是同一拍
    /// （这一刻 <c>ExtentHeight</c> 还是 0），而排到 dispatcher 下一轮再动手的话，
    /// 列表会在这一轮里先按「顶部」画出来、下一轮才滑过去 —— 打开时看得见的那一下「闪」就是它。
    /// 行列因此在构造函数里就藏起来，定位到哪儿才在哪儿露面。
    /// </para>
    /// <para>
    /// 仍然量不到尺寸时排下一轮重试（重试是幂等的）；一路都量不到才退回「够到就行」
    /// （<c>ScrollIntoView</c>）：把这一行带进可视区，总比停在顶上强。
    /// </para>
    /// </remarks>
    private void RevealCurrent()
    {
        if (CurrentRow() is { } row && !CenterOn(row))
        {
            // <c>Loaded</c> 时列表还没量出可滚高度：把布局赶出来，让定位落在这一帧上。
            // 赶不出来也不怕 —— 行列还藏着，看不到「先画在顶上再跳」。
            UpdateLayout();
        }

        Reveal(RevealRetries);
    }

    private void Reveal(int attempts)
    {
        if (CurrentRow() is not { } row)
        {
            // 队列为空、或没有正在播放的行：没有可定位的东西，直接露面。
            ShowRows();
            return;
        }

        if (CenterOn(row))
        {
            ShowRows();
            return;
        }

        if (attempts > 0 && DispatcherQueue is { } dispatcher)
        {
            dispatcher.TryEnqueue(() => Reveal(attempts - 1));
            return;
        }

        // 兜底那次是滚过去的（会动画），等它滚完再露面 —— 否则又是「先画在顶上再跳」。
        RowList.ScrollIntoView(row);
        ShowRowsAfterFallbackScroll();
    }

    /// <summary>定位落位，露出行列表。</summary>
    private void ShowRows() => _ = AppMotion.RevealAsync(RowList);

    /// <summary>兜底滚动之后的露面：<c>ScrollIntoView</c> 自己会滚一段，让它先滚完。</summary>
    private void ShowRowsAfterFallbackScroll()
    {
        if (DispatcherQueue is not { } dispatcher)
        {
            ShowRows();
            return;
        }

        var timer = dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(260);
        timer.IsRepeating = false;
        timer.Tick += (sender, _) =>
        {
            sender.Stop();
            ShowRows();
        };
        timer.Start();
    }

    /// <summary>正在播放那一行；队列为空时为 <c>null</c>。</summary>
    private PlayQueueRow? CurrentRow()
    {
        if (ViewModel is not { } viewModel)
        {
            return null;
        }

        foreach (var row in viewModel.Rows)
        {
            if (row.IsCurrent)
            {
                return row;
            }
        }

        return null;
    }

    /// <summary>
    /// 把某一行摆到可视区竖直正中。量不到尺寸（还没布局完）时返回 <c>false</c>，
    /// 让调用方下一轮再试。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>偏移一次算准，不靠先滚一下再挪回去。</b> 早先的写法是 <c>ScrollIntoView(Leading)</c>
    /// 把行顶到最上面、再回退半个空档 —— 那要求 <c>ScrollIntoView</c> 当场生效，而它会动画、
    /// 也可能排进下一轮布局，时机一错落点就是随机的。（那个写法已经删掉。）
    /// </para>
    /// <para>
    /// <b>行等长，所以行距算得出来</b>：标题与歌手都是单行省略号、封面与按钮定宽定高，
    /// 于是「可滚内容总高 / 条数」就是每行占的高度（含行距）。再拿一个已实现的行当锚点 ——
    /// 它在内容坐标系里的位置已经含了列表的上内边距这类常量，不用去猜 ——
    /// 任意下标的行顶 = 锚点行顶 + 相差几行 × 行距。
    /// </para>
    /// <para>
    /// <b>目标偏移 = 行顶 - (可视高 - 行距) / 2</b>：这一行就贴在可视区的竖直正中。
    /// 越界由 <c>ChangeView</c> 钳住 —— 行落在队列头尾那半屏里时摆不到正中，
    /// 就停在能到的最中间，不会把列表拉出一段空白。
    /// </para>
    /// </remarks>
    private bool CenterOn(PlayQueueRow row)
    {
        if (ViewModel is not { } viewModel
            || FindDescendant<ScrollViewer>(RowList) is not { } scroll
            || RowList.ItemsPanelRoot is not ItemsStackPanel panel
            || viewModel.Rows.Count == 0
            || scroll.ViewportHeight <= 0)
        {
            return false;
        }

        var anchor = panel.FirstVisibleIndex;

        if (anchor < 0 || RowList.ContainerFromIndex(anchor) is not FrameworkElement anchorRow)
        {
            return false;
        }

        var pitch = scroll.ExtentHeight / viewModel.Rows.Count;

        if (!double.IsFinite(pitch) || pitch <= 0)
        {
            return false;
        }

        var anchorTop = anchorRow.TransformToVisual(panel).TransformPoint(default).Y;
        var rowTop = anchorTop + ((viewModel.Rows.IndexOf(row) - anchor) * pitch);

        scroll.ChangeView(null, rowTop - ((scroll.ViewportHeight - pitch) / 2), null, true);

        return true;
    }

    private static T? FindDescendant<T>(DependencyObject? node)
        where T : DependencyObject
    {
        if (node is null)
        {
            return null;
        }

        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is T match)
            {
                return match;
            }

            if (FindDescendant<T>(child) is { } deeper)
            {
                return deeper;
            }
        }

        return null;
    }

    // ── 悬停态 ──────────────────────────────────────────────────────────────

    private void OnRowPointerEntered(object sender, PointerRoutedEventArgs e) => SetPointerOver(sender, true);

    private void OnRowPointerExited(object sender, PointerRoutedEventArgs e) => SetPointerOver(sender, false);

    /// <summary>
    /// 悬停态写在**行数据对象**上，不写在容器上 —— 容器回收去装另一行时 <c>x:Bind</c> 自然重读，
    /// 不用做任何清理。同 <c>TrackListView</c>。
    /// </summary>
    private static void SetPointerOver(object sender, bool value)
    {
        if (sender is FrameworkElement { DataContext: PlayQueueRow row })
        {
            row.IsPointerOver = value;
        }
    }

    // ── 点行切歌 / 删除 / 清空 ──────────────────────────────────────────────

    private void OnRowClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PlayQueueRow { IsCurrent: false } row)
        {
            ViewModel.Play(row.Position);
        }
    }

    /// <remarks>
    /// <para>
    /// <b>退场与让位同时进行。</b> 被删的一行淡出并轻微上移；它下面已经实例化的行
    /// 用 <c>TranslationTransition</c> 同时上滑一整行的高度（与拖动排序同一套
    /// <see cref="SetShift"/>）。动画结束后才真正删行，其余行早就补到位，不会在删的一瞬间硬跳。
    /// </para>
    /// <para>
    /// <b>删行后立刻把让位位移归零。</b> 行集合少一条后布局会把下面的行整体上移一行；
    /// 把 <c>Translation</c> 无过渡地清零，视觉位置正好不变，容器也不会带着偏移被回收。
    /// 被删那行保持透明，等容器真的换了 <c>DataContext</c>（被复用）时再还原 ——
    /// 提前还原等于把旧行重新点亮，就是删除时那一下闪。框架的删除过渡已在 XAML 里关掉。
    /// </para>
    /// </remarks>
    private async void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PlayQueueRow row })
        {
            return;
        }

        var container = RowList.ContainerFromItem(row) as ListViewItem;
        var index = ViewModel.Rows.IndexOf(row);

        if (container is not null && index >= 0)
        {
            ShiftRowsUp(index, container.ActualHeight + container.Margin.Top + container.Margin.Bottom);
            await ExitRowAsync(container);
        }

        // 动画期间队列可能被别的入口改过，删之前重新读一次行号。
        var removeIndex = ViewModel.Rows.IndexOf(row);

        if (removeIndex < 0)
        {
            // 已经被别处删掉。布局已经补过位，把上滑的位移撤掉，
            // 容器交给回收回调处理（直接还原会让旧行闪一下）。
            ClearShifts();
            if (container is not null)
            {
                ResetWhenReused(container, row);
            }

            return;
        }

        if (container is not null)
        {
            ResetWhenReused(container, row);
        }

        ViewModel.RemoveNow(removeIndex);

        // 队列拒绝了这次删除（例如它已经是当前曲目），行还在：还原容器并把让位滑回去。
        if (container is not null && RowList.Items.Contains(row))
        {
            StopWaitingForReuse(container);
            ResetRowContainer(container);
            RestoreShifts();
            return;
        }

        // 删除成功：布局已经整体上移一行，位移无过渡地清零，视觉位置不变。
        ClearShifts();
    }

    /// <summary>被删行下方的行同时上滑一行，作为删除时的让位动画。</summary>
    private void ShiftRowsUp(int removedIndex, double pitch)
    {
        if (RowList.ItemsPanelRoot is not { } panel)
        {
            return;
        }

        foreach (var child in panel.Children)
        {
            if (child is FrameworkElement container && RowList.IndexFromContainer(container) > removedIndex)
            {
                SetShift(container, -pitch);
            }
        }
    }

    /// <summary>让位动画取消时把行滑回原位。</summary>
    private void RestoreShifts()
    {
        if (RowList.ItemsPanelRoot is not { } panel)
        {
            return;
        }

        foreach (var child in panel.Children)
        {
            if (child is FrameworkElement container)
            {
                SetShift(container, 0);
            }
        }
    }

    /// <summary>删行后把让位位移归零，且不播过渡 —— 布局已经替我们完成了这次位移。</summary>
    private void ClearShifts()
    {
        if (RowList.ItemsPanelRoot is not { } panel)
        {
            return;
        }

        foreach (var child in panel.Children)
        {
            if (child is FrameworkElement container)
            {
                ClearShift(container);
            }
        }
    }

    private static void ClearShift(FrameworkElement container)
    {
        // 先摘掉 TranslationTransition，否则归零自己也会播 140 ms，列表会先弹下去再回来。
        container.TranslationTransition = null;
        container.Translation = Vector3.Zero;
    }

    /// <summary>
    /// 被删的那一行向右抽出并淡出。
    /// </summary>
    /// <remarks>
    /// <b>这里只能用 <c>UIElement</c> 的过渡 API，不能走 <see cref="AppMotion"/>。</b>
    /// <c>AppMotion</c> 走 <c>ElementCompositionPreview</c>；同一个元素一旦进过那套 API，
    /// 再设 <c>TranslationTransition</c> 会被运行时拒绝（实测抛
    /// <c>UnauthorizedAccessException</c>，异步 void 里的异常直接把应用带崩）。
    /// 删除动画、下方让位、拖动排序因此统一用 <c>TranslationTransition</c> 这一套。
    /// </remarks>
    private static async Task ExitRowAsync(FrameworkElement container)
    {
        if (!AppMotion.IsEnabled)
        {
            container.TranslationTransition = null;
            container.OpacityTransition = null;
            container.Translation = new Vector3(RemoveSlideDistance, 0, 0);
            container.Opacity = 0;
            return;
        }

        container.TranslationTransition = new Vector3Transition { Duration = RemoveDuration };
        container.OpacityTransition = new ScalarTransition { Duration = RemoveDuration };
        container.Translation = new Vector3(RemoveSlideDistance, 0, 0);
        container.Opacity = 0;

        await Task.Delay(RemoveDuration);
    }

    /// <summary>把容器恢复到正常显示状态，并摘掉删除时挂的过渡。</summary>
    private static void ResetRowContainer(FrameworkElement container)
    {
        container.TranslationTransition = null;
        container.OpacityTransition = null;
        container.Translation = Vector3.Zero;
        container.Opacity = 1;
    }

    /// <summary>
    /// 容器被回收给另一行时还原合成状态。删除时容器是透明的，直接还原会让旧行闪一下，
    /// 不还原又会让下一行带着 0 的不透明度出现，所以等 <c>DataContext</c> 真的换掉再还原。
    /// </summary>
    private void ResetWhenReused(ListViewItem container, PlayQueueRow removedRow)
    {
        // 先摘掉上一次删除留下的回调（同一容器可能被连续复用）。
        StopWaitingForReuse(container);

        void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
        {
            if (ReferenceEquals(args.NewValue, removedRow))
            {
                return;
            }

            StopWaitingForReuse(container);
            ResetRowContainer(container);
        }

        void OnUnloaded(object sender, RoutedEventArgs e) => StopWaitingForReuse(container);

        _reuseWaiters[container] = (OnDataContextChanged, OnUnloaded);
        container.DataContextChanged += OnDataContextChanged;
        container.Unloaded += OnUnloaded;
    }

    /// <summary>摘掉 <see cref="ResetWhenReused"/> 挂的回调。</summary>
    private void StopWaitingForReuse(ListViewItem container)
    {
        if (!_reuseWaiters.Remove(container, out var waiters))
        {
            return;
        }

        container.DataContextChanged -= waiters.Changed;
        container.Unloaded -= waiters.Unloaded;
    }

    private void OnClearClick(object sender, RoutedEventArgs e) => ClearRequested?.Invoke(this, EventArgs.Empty);

    private void OnCloseClick(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    // ── 拖动排序 ────────────────────────────────────────────────────────────

    private void OnGripPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PlayQueueRow row })
        {
            return;
        }

        // 随机模式不给拖（CanReorder），正在播放那一行也不给拖 —— 与删除按钮置灰同一套判断，
        // 两者都由 PlayQueueRow 算好，面板不再复述一遍规则。
        if (!row.CanReorder || row.IsCurrent)
        {
            return;
        }

        // 手柄只在鼠标悬停时出现，触屏按压不进入这条路径。
        if (e.Pointer.PointerDeviceType != PointerDeviceType.Mouse)
        {
            return;
        }

        var point = e.GetCurrentPoint(RowHost);

        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (RowList.ContainerFromIndex(row.Position) is not ListViewItem container)
        {
            return;
        }

        var top = container.TransformToVisual(RowHost).TransformPoint(default).Y;

        _dragPointerId = e.Pointer.PointerId;
        _dragRow = row;
        _dragContainer = container;
        _dragFrom = row.Position;
        _slot = row.Position;
        _originY = point.Position.Y;
        _dragging = false;

        // 抓在卡片的哪个位置：起手在哪就一直在哪，卡片不会突然跳到指针正中。
        _grabOffset = _originY - top;

        // 一行占的高度（含行间距），让位时整行整行地挪。
        _pitch = container.ActualHeight + container.Margin.Top + container.Margin.Bottom;

        // 自动滚动与落点都按「滚动偏移 + 行距」算：先把滚动器与第 0 行在内容坐标里的
        // 位置量出来，之后指针不动、列表滚动时也能算对落点。
        _scroll = FindDescendant<ScrollViewer>(RowList);
        _contentTop = ContentTop();

        RowList.CapturePointer(e.Pointer);

        // ★ 不标 Handled 的话，ListViewItem 会把这次按下当成一次行点击，松手就切歌。
        e.Handled = true;
    }

    private void OnListPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragPointerId != e.Pointer.PointerId)
        {
            return;
        }

        var y = e.GetCurrentPoint(RowHost).Position.Y;
        _pointerY = y;

        // 位移不够就不算开始拖。
        if (!_dragging)
        {
            if (Math.Abs(y - _originY) < DragThreshold)
            {
                return;
            }

            Lift();
        }

        MoveGhost(y);

        ApplyShifts(SlotAt(y));

        // 拖到列表上下边缘附近时自动滚动；指针离开边缘区就停。
        UpdateAutoScroll();

        e.Handled = true;
    }

    private void OnListPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragPointerId != e.Pointer.PointerId)
        {
            return;
        }

        var from = _dragFrom;
        var slot = _slot;
        var moved = _dragging;

        // 先把数据改掉（它把 Refresh 排到 dispatcher 下一轮），再收掉视觉状态 ——
        // 两步都在本帧内跑完，重建落在渲染之前，所以看不到「先弹回原位、再重排」。
        if (moved && from >= 0 && slot >= 0)
        {
            // 槽位是「插到第 slot 行之前」，换算成移动完成后的最终下标。
            var to = slot > from ? slot - 1 : slot;

            if (to != from)
            {
                ViewModel?.Move(from, to);
            }
        }

        EndDrag();
        e.Handled = true;
    }

    private void OnListPointerCaptureLost(object sender, PointerRoutedEventArgs e) => EndDrag();

    private void OnListPointerCanceled(object sender, PointerRoutedEventArgs e) => EndDrag();

    /// <summary>把卡片拎起来：原行原地隐掉，卡片浮到列表之上。</summary>
    private void Lift()
    {
        if (_dragContainer is null || _dragRow is null)
        {
            return;
        }

        _dragging = true;

        DraggedRow = _dragRow;
        DragGhost.Height = _dragContainer.ActualHeight;
        DragGhost.Visibility = Visibility.Visible;

        // 原行连它自己的悬停底色一起隐掉；空出来的那一格由让位的行补上。
        _dragContainer.Opacity = 0;
    }

    /// <summary>让卡片跟着指针走。垂直方向钳在列表范围内，别飞出去。</summary>
    private void MoveGhost(double pointerY)
    {
        if (_dragContainer is null)
        {
            return;
        }

        var top = RowList.TransformToVisual(RowHost).TransformPoint(default).Y;
        var lowest = top + RowList.ActualHeight - DragGhost.Height;

        DragGhostShift.Y = Math.Clamp(pointerY - _grabOffset, top, Math.Max(top, lowest));
    }

    /// <summary>
    /// 让其余的行给落点腾地方。
    /// </summary>
    /// <remarks>
    /// 位移量就是 <c>PlayQueue.Remap</c> 那套：抽出 <c>from</c> 再插到 <c>to</c> 之后，
    /// 原来的第 i 行落到 <c>Remap(i, from, to)</c>，只可能是原地、上移一格或下移一格，
    /// 所以这里按「整行高度」给 ±1 格。算出来是 0 的那些行会被显式摆回 0，
    /// 于是落点又挪回去时它们会自己滑回原位。
    /// </remarks>
    private void ApplyShifts(int slot)
    {
        if (slot < 0 || slot == _slot)
        {
            return;
        }

        _slot = slot;

        if (RowList.ItemsPanelRoot is not { } panel)
        {
            return;
        }

        foreach (var child in panel.Children)
        {
            if (child is not FrameworkElement container)
            {
                continue;
            }

            var index = RowList.IndexFromContainer(container);

            if (index < 0)
            {
                continue;
            }

            SetShift(container, ShiftFor(index));
        }
    }

    /// <summary>这一行该让开多少：插到 <see cref="_slot"/> 之前时，区间内的行整体挪一格。</summary>
    private double ShiftFor(int index)
    {
        if (_dragFrom < 0 || _slot < 0 || index < 0 || index == _dragFrom)
        {
            return 0;
        }

        var to = _slot > _dragFrom ? _slot - 1 : _slot;

        return _dragFrom < to && index > _dragFrom && index <= to ? -_pitch
            : _dragFrom > to && index >= to && index < _dragFrom ? _pitch
            : 0;
    }

    /// <summary>把某一行整体平移 <paramref name="y"/> 像素。<c>TranslationTransition</c> 负责补间。</summary>
    private static void SetShift(FrameworkElement container, double y)
    {
        container.TranslationTransition ??= new Vector3Transition { Duration = ShiftDuration };
        container.Translation = new Vector3(0, (float)y, 0);
    }

    /// <summary>
    /// 收掉手势：清状态、收起卡片、把让位的行放回去。
    /// <b>幂等</b> —— 松开、捕获丢失、取消、队列被重建四条路都会走到这里。
    /// </summary>
    private void EndDrag()
    {
        // ★ 没在拖就一个动作都不做。ReleasePointerCaptures 是无差别放掉这个元素上所有捕获的，
        //   而队列被重建（OnRowsChanged）可能在用户正拖着滚动条时发生 ——
        //   那时候插一脚会把人家正在进行的滚动手势掐断。
        if (_dragPointerId is null)
        {
            return;
        }

        _dragPointerId = null;
        _dragging = false;
        _dragContainer = null;
        _dragRow = null;
        _dragFrom = -1;
        _slot = -1;

        StopAutoScroll();
        _scroll = null;
        _contentTop = double.NaN;

        DraggedRow = null;
        DragGhost.Visibility = Visibility.Collapsed;
        DragGhostShift.Y = 0;

        ResetVisuals();
        RowList.ReleasePointerCaptures();
    }

    /// <summary>
    /// 把已经实例化的行全部摆回原位。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>扫全部而不是只扫动过的那些</b>：容器是会被回收的，一个带着平移或透明度的容器
    /// 去装另一行时会把那副样子一起带过去。容器数量就是屏幕上那几行，扫一遍不值一提。
    /// </para>
    /// <para>
    /// <b>归零不能带过渡。</b> 松手时让位的行已经停在落点上了，接下来 <c>Rows.Move</c>
    /// 会把它们按新顺序放到同一个位置；这里若还播 140 ms 回零动画，就会和新布局叠成
    /// 一次明显的滑动。无过渡地清零，两者在同一帧里正好抵消。
    /// </para>
    /// </remarks>
    private void ResetVisuals()
    {

        if (RowList.ItemsPanelRoot is not { } panel)
        {
            return;
        }

        foreach (var child in panel.Children)
        {
            if (child is not FrameworkElement container)
            {
                continue;
            }

            ClearShift(container);
            container.OpacityTransition = null;
            container.Opacity = 1;
        }
    }

    /// <summary>
    /// 指针落在哪两行之间。返回值是「插到第几行**之前**」，即 <c>[0, Count]</c>；
    /// 算不出来（队列空、一行都没实例化）时返回 <c>-1</c>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>优先按内容坐标算，不看可视区。</b> 行等长，行距已知，于是把指针换算进内容坐标
    /// （<c>指针 - 列表顶 + 滚动偏移</c>）就能直接得到落点，超出可视区也不受影响。
    /// <b>自动滚动时必须用这条路</b>：滚动时指针没动、列表在动，可视区里的容器位置要等下一轮
    /// 布局才更新，拿它算落点会慢半拍；而滚动偏移是 <c>ChangeView</c> 当场就变的。
    /// </para>
    /// <para>
    /// 兜底：量不到滚动器或行距时，退回在可视区里逐行比中点（虚拟化下未实现的行跳过，
    /// 指针在可视区之外自然落到区间首行或「末行 + 1」）。
    /// </para>
    /// </remarks>
    private int SlotAt(double y)
    {
        var count = ViewModel?.Rows.Count ?? 0;

        if (count == 0)
        {
            return -1;
        }

        if (_scroll is not null && _pitch > 0 && double.IsFinite(_contentTop))
        {
            var top = RowList.TransformToVisual(RowHost).TransformPoint(default).Y;
            var contentY = y - top + _scroll.VerticalOffset;

            return Math.Clamp((int)Math.Round((contentY - _contentTop) / _pitch), 0, count);
        }

        var first = 0;
        var last = count - 1;

        if (RowList.ItemsPanelRoot is ItemsStackPanel { FirstVisibleIndex: >= 0 } panel)
        {
            first = Math.Max(0, panel.FirstVisibleIndex);
            last = Math.Min(count - 1, panel.LastVisibleIndex);
        }

        var reached = false;

        for (var i = first; i <= last; i++)
        {
            if (Bounds(i) is not { } bounds)
            {
                continue;
            }

            reached = true;

            if (y < bounds.Top + (bounds.Height / 2))
            {
                return i;
            }
        }

        return reached ? last + 1 : -1;
    }

    /// <summary>某一行在 <see cref="RowHost"/> 坐标系里的矩形；未实例化时为 <c>null</c>。</summary>
    private Rect? Bounds(int index)
        => RowList.ContainerFromIndex(index) is FrameworkElement container
            ? container.TransformToVisual(RowHost)
                .TransformBounds(new Rect(0, 0, container.ActualWidth, container.ActualHeight))
            : null;

    /// <summary>第 0 行在内容坐标里的顶边；量不到时返回 <c>NaN</c>。</summary>
    private double ContentTop()
    {
        if (RowList.ItemsPanelRoot is not ItemsStackPanel panel || _pitch <= 0)
        {
            return double.NaN;
        }

        var anchor = panel.FirstVisibleIndex;

        if (anchor < 0 || RowList.ContainerFromIndex(anchor) is not FrameworkElement container)
        {
            return double.NaN;
        }

        var top = container.TransformToVisual(panel).TransformPoint(default).Y;

        return top - (anchor * _pitch);
    }

    // ── 拖动时自动滚动 ──────────────────────────────────────────────────────

    /// <summary>
    /// 按指针离列表上下边缘的深度决定这一帧滚多少：越靠边越快，回到中间就停。
    /// </summary>
    private void UpdateAutoScroll()
    {
        if (!_dragging || _scroll is null)
        {
            StopAutoScroll();
            return;
        }

        var top = RowList.TransformToVisual(RowHost).TransformPoint(default).Y;
        var bottom = top + RowList.ActualHeight;
        var step = 0.0;

        if (_pointerY < top + AutoScrollZone)
        {
            step = -AutoScrollMaxStep * Math.Clamp((top + AutoScrollZone - _pointerY) / AutoScrollZone, 0, 1);
        }
        else if (_pointerY > bottom - AutoScrollZone)
        {
            step = AutoScrollMaxStep * Math.Clamp((_pointerY - (bottom - AutoScrollZone)) / AutoScrollZone, 0, 1);
        }

        _autoScrollStep = step;

        if (Math.Abs(step) < 0.5)
        {
            StopAutoScroll();
            return;
        }

        EnsureAutoScrollTimer();
    }

    private void EnsureAutoScrollTimer()
    {
        if (_autoScrollTimer is null)
        {
            if (DispatcherQueue is not { } dispatcher)
            {
                return;
            }

            _autoScrollTimer = dispatcher.CreateTimer();
            _autoScrollTimer.Interval = TimeSpan.FromMilliseconds(16);
            _autoScrollTimer.IsRepeating = true;
            _autoScrollTimer.Tick += OnAutoScrollTick;
        }

        if (!_autoScrollTimer.IsRunning)
        {
            _autoScrollTimer.Start();
        }
    }

    private void StopAutoScroll()
    {
        if (_autoScrollTimer?.IsRunning == true)
        {
            _autoScrollTimer.Stop();
        }

        _autoScrollStep = 0;
    }

    /// <summary>
    /// 自动滚动的一帧：滚一点，然后按新的偏移重算落点。
    /// </summary>
    /// <remarks>
    /// <b>落点必须跟着偏移重算。</b> 指针是静止的，滚的是列表 —— 指针下面换了一行，
    /// 让位的行也得跟着换。刚被回收出来的容器由
    /// <see cref="OnContainerContentChanging"/> 在准备时补上位移。
    /// </remarks>
    private void OnAutoScrollTick(DispatcherQueueTimer sender, object args)
    {
        if (!_dragging || _scroll is null || _dragPointerId is null)
        {
            StopAutoScroll();
            return;
        }

        var offset = _scroll.VerticalOffset;
        var target = Math.Clamp(offset + _autoScrollStep, 0, _scroll.ScrollableHeight);

        // 已经到顶/到底：停下来，别空转。指针再动时 UpdateAutoScroll 会重新拉起。
        if (Math.Abs(target - offset) < 0.01)
        {
            StopAutoScroll();
            return;
        }

        _scroll.ChangeView(null, target, null, true);

        var slot = SlotAt(_pointerY);

        if (slot >= 0 && slot != _slot)
        {
            ApplyShifts(slot);
        }
    }

    /// <summary>
    /// 容器被复用去装另一行时，按新行重放拖动状态。
    /// </summary>
    /// <remarks>
    /// <b>自动滚动会让行容器不停地回收。</b> 被藏起来的那一行、以及正在让位的行，
    /// 都会随滚动滚出可视区、被拿去装别的行 —— 容器上的 <c>Opacity</c> / <c>Translation</c>
    /// 是元素自身的属性，不跟着行数据走，不在这里重算就会串到别的行上。
    /// </remarks>
    private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer is not ListViewItem container)
        {
            return;
        }

        if (_dragging)
        {
            container.Opacity = ReferenceEquals(args.Item, _dragRow) ? 0 : 1;
            SetShift(container, ShiftFor(args.ItemIndex));
            return;
        }

        // 不在拖：把拖动期间留下、但滚出可视区没来得及还原的容器摆回去。
        // 删除动画自己的透明态由 ResetWhenReused 管（容器在 _reuseWaiters 里），不能在这里碰。
        if (!_reuseWaiters.ContainsKey(container))
        {
            ResetRowContainer(container);
        }
    }
}
