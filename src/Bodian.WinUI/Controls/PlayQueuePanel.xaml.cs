using System.Collections.Specialized;
using System.Numerics;
using Bodian.WinUI.ViewModels;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Bodian.WinUI.Controls;

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

    /// <summary>卡片浮起来的高度，<c>ThemeShadow</c> 靠它投影。</summary>
    private const float GhostLift = 32;

    private readonly ThemeShadow _ghostShadow = new();

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

        Loaded += (_, _) => AttachRows();
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

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PlayQueueRow row })
        {
            ViewModel.Remove(row.Position);
        }
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

        var slot = SlotAt(y);

        if (slot != _slot)
        {
            _slot = slot;
            ApplyShifts(slot);
        }

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
        if (RowList.ItemsPanelRoot is not { } panel)
        {
            return;
        }

        var to = slot > _dragFrom ? slot - 1 : slot;

        foreach (var child in panel.Children)
        {
            if (child is not FrameworkElement container)
            {
                continue;
            }

            var index = RowList.IndexFromContainer(container);

            if (index < 0 || index == _dragFrom)
            {
                continue;
            }

            var offset = _dragFrom < to && index > _dragFrom && index <= to ? -_pitch
                : _dragFrom > to && index >= to && index < _dragFrom ? _pitch
                : 0;

            SetShift(container, offset);
        }

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
    /// <b>扫全部而不是只扫动过的那些</b>：容器是会被回收的，一个带着平移或透明度的容器
    /// 去装另一行时会把那副样子一起带过去。容器数量就是屏幕上那几行，扫一遍不值一提。
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

            container.Translation = Vector3.Zero;
            container.Opacity = 1;
        }
    }

    /// <summary>
    /// 指针落在哪两行之间。返回值是「插到第几行**之前**」，即 <c>[0, Count]</c>；
    /// 算不出来（队列空、一行都没实例化）时返回 <c>-1</c>。
    /// </summary>
    /// <remarks>
    /// <b>不直接遍历全表问 <c>ContainerFromIndex</c></b>：虚拟化下未实现的行会返回 <c>null</c>。
    /// 先拿 <see cref="ItemsStackPanel"/> 的可视区间把范围收窄，区间内 null 的照旧跳过。
    /// 已实现的行是连续的，所以指针在可视区内必有边界行把它夹住；指针在可视区之外会自然落到
    /// 区间首行或「末行 + 1」，正是该有的钳制。<b>不做自动滚动</b>。
    /// </remarks>
    private int SlotAt(double y)
    {
        var count = ViewModel?.Rows.Count ?? 0;

        if (count == 0)
        {
            return -1;
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
}
