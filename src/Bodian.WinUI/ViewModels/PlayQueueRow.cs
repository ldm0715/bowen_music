using Bodian.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 播放列表面板里的一行。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么不复用 <see cref="TrackRow"/></b>：那个是给曲目列表用的，带着悬停态、
/// 行尾「更多」菜单的显隐，还要一个「第几名」的三态序号 —— 队列面板一样都用不上，
/// 而它缺的恰恰是这里最要紧的东西：<b>行号</b>（删除要按行号定位）与「能不能删」。
/// </para>
/// <para>
/// 封面、时长这些显示字段仍然走 <c>Formats</c>，不另造一套格式化。
/// </para>
/// </remarks>
public sealed partial class PlayQueueRow : ObservableObject
{
    /// <summary>这一行对应的原始曲目。<b>不给 XAML 用</b>，理由同 <see cref="TrackRow"/>：类型信息生成器要给它生成无参构造。</summary>
    internal Track Source { get; init; } = null!;

    /// <summary>在队列里的位置，从 0 起。删除、切歌与拖动都按它定位。</summary>
    internal int Position { get; init; }

    /// <summary>
    /// 这一行能不能被拖动重排。
    /// </summary>
    /// <remarks>
    /// 快照自队列模式（<c>PlayQueue.CanReorder</c>），与 <see cref="Position"/> 一样随
    /// <c>PlayQueueViewModel.Refresh</c> 重建。模式切换本身会抛 <c>Changed</c>，所以随机模式一开、
    /// 手柄自动消失，不用额外订阅。**随机模式下不能拖**的理由见 <c>PlayQueue.CanReorder</c>。
    /// </remarks>
    internal bool CanReorder { get; init; }

    public string OrdinalText => (Position + 1).ToString("D2", System.Globalization.CultureInfo.InvariantCulture);

    public string Title => Source.Title;

    public string ArtistText => Source.ArtistText;

    public Uri? CoverImage => Source.CoverImage;

    public string DurationText => Formats.Duration(Source.Duration);

    /// <summary>这一行是不是正在播放的那首。起伏条自己按它管显隐。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OrdinalVisibility))]
    [NotifyPropertyChangedFor(nameof(DragHandleVisibility))]
    [NotifyPropertyChangedFor(nameof(CanRemove))]
    public partial bool IsCurrent { get; set; }

    /// <summary>鼠标是不是在这一行上。由行模板的 <c>PointerEntered</c> / <c>PointerExited</c> 驱动，同 <see cref="TrackRow"/>。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OrdinalVisibility))]
    [NotifyPropertyChangedFor(nameof(DragHandleVisibility))]
    public partial bool IsPointerOver { get; set; }

    /// <summary>
    /// 这一行正被拖着。
    /// </summary>
    /// <remarks>
    /// <b>为真时它在列表里是隐形的</b>：拖起来的是另画的一张卡片（<c>PlayQueuePanel.DragGhost</c>），
    /// 这一行留着只会和卡片重影。空出来的那一格由让位的行补上，于是列表里空着的就是落点。
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RowOpacity))]
    public partial bool IsDragging { get; set; }

    /// <summary>
    /// 序号那一格的三态互斥：正在播放 → 起伏条（<c>PlayingBars</c> 自己按 <c>IsPlaying</c> 管显隐）；
    /// 悬停且能拖 → 拖动条；其余 → 序号。
    /// </summary>
    /// <remarks>
    /// <b>三条必须落在同一个位置上</b>（一律 Center）：早先 <c>TrackListView</c> 那边序号是右对齐的，
    /// 和居中的播放键差半个列宽，鼠标一进一出整列就横向跳一下。
    /// </remarks>
    public Visibility OrdinalVisibility => Formats.Visible(!IsCurrent && !ShowDragHandle);

    /// <summary>拖动条要不要出现。正在播放那一行不给拖 —— 与「删除按钮置灰」同理，也免得跟起伏条抢同一格。</summary>
    public Visibility DragHandleVisibility => Formats.Visible(ShowDragHandle);

    /// <summary>被拖那一行在列表里彻底隐掉（卡片替它显形），见 <see cref="IsDragging"/>。</summary>
    public double RowOpacity => IsDragging ? 0 : 1;

    private bool ShowDragHandle => CanReorder && !IsCurrent && IsPointerOver;

    /// <summary>
    /// 正在播放的那一首不给删。
    /// </summary>
    /// <remarks>
    /// 删掉当前曲目之后「要不要自动切下一首」没有不别扭的答案，所以干脆把这一行的删除按钮置灰；
    /// 想跳过它就点下一首。界面这样定，<c>PlayQueue.RemoveItem</c> 就永远不会删到游标位置。
    /// </remarks>
    public bool CanRemove => !IsCurrent;
}
