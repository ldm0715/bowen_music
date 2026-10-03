using System.Globalization;
using Bodian.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 曲目列表里的一行。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么要有这一层包装</b>：<c>ListView</c> 的 <c>DataTemplate</c> 拿不到 item 的下标，
/// 所以要显示「第几首」，序号必须以数据的形式跟着走 —— 仓库里已有的 <c>RankedTrack</c>
/// 出于同一个理由存在。此外「正在播放」高亮也需要一个随外部状态变化的可绑定属性。
/// </para>
/// <para>
/// <b>为什么显示字段是铺开的、而不是直接公开一个 Track 属性</b>：
/// XAML 类型信息生成器会为数据类型的每个公开属性生成 <c>new 该类型()</c>
/// （生成代码里能看到 <c>Activate_48_Track() { return new Track(); }</c>）。
/// 而 <see cref="Track"/> 是带 <c>required</c> 成员的领域模型，不允许空构造 ——
/// 公开它会让整个项目编不过。所以原始曲目留成 <c>internal</c>，显示用到的字段铺开。
/// </para>
/// <para>
/// 属性一律是普通的 <c>get; set;</c>，不用 <c>required</c> / <c>init</c>：
/// 生成器要给它生成 setter，<c>init</c> 会让那行生成代码编不过（CS8852）。
/// </para>
/// <para>
/// 序号列是三态互斥的：<b>正在播放 → 起伏条；鼠标悬停 → 播放键；其余 → 序号</b>。
/// 前两者由本类算，绑定方不必自己拼条件；起伏条自己管显隐（见 <c>Controls/PlayingBars</c>）。
/// </para>
/// <para>
/// 行尾的「更多」按钮同样由本类算：<b>悬停或菜单开着时可见</b>，见 <see cref="MoreVisibility"/>。
/// </para>
/// </remarks>
public sealed partial class TrackRow : ObservableObject
{
    /// <summary>这一行对应的原始曲目。<b>不给 XAML 用</b>，理由见类型说明。</summary>
    internal Track Source { get; set; } = null!;

    /// <summary>从 1 开始的序号（榜单里是名次）。超过 99 时自然变成三位，不截断。</summary>
    public int Ordinal { get; set; }

    /// <summary>
    /// 序号是否补零成两位数。
    /// </summary>
    /// <remarks>
    /// 曲目列表补零（<c>01</c> <c>02</c>）让列宽稳定；<b>榜单的名次不补零</b> ——
    /// 「第 1 名」写成 <c>01</c> 是错的。两种列表共用这个控件，所以由数据源决定。
    /// </remarks>
    public bool PadOrdinal { get; set; } = true;

    public string OrdinalText => PadOrdinal
        ? Ordinal.ToString("D2", CultureInfo.InvariantCulture)
        : Ordinal.ToString(CultureInfo.InvariantCulture);

    public string Title => Source.Title;

    public string ArtistText => Source.ArtistText;

    public string AlbumName => Formats.AlbumName(Source.AlbumName);

    public Uri? CoverImage => Source.CoverImage;

    // ── 付费：显示逻辑收口在 Formats，这里只做转发 ──

    public Visibility PayLabelVisibility => Formats.PayLabelVisibility(Source.RequiresVip, Source.RequiresPurchase);

    public string PayLabel => Formats.PayLabel(Source.RequiresVip, Source.RequiresPurchase);

    public string DurationText => Formats.Duration(Source.Duration);

    // ── 行状态 ──────────────────────────────────────────────────────────────

    /// <summary>鼠标是否在这一行上。由行的 <c>PointerEntered</c> / <c>PointerExited</c> 驱动。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IndexVisibility), nameof(PlayGlyphVisibility), nameof(MoreVisibility))]
    public partial bool IsPointerOver { get; set; }

    /// <summary>
    /// 这一行的「更多」菜单是不是开着。
    /// </summary>
    /// <remarks>
    /// <b>没有这个状态位，菜单一打开就会自己关掉</b>：菜单在 <c>Popup</c> 里，鼠标移进去的瞬间
    /// 行收到 <c>PointerExited</c>、<see cref="IsPointerOver"/> 变回 <c>false</c>，按钮随之折叠，
    /// 而按钮折叠会把它的 Flyout 一起带走。症状是「点了没反应」，光看代码很难发现。
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MoreVisibility))]
    public partial bool IsMenuOpen { get; set; }

    /// <summary>这一行是不是当前正在播放的那首。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IndexVisibility), nameof(PlayGlyphVisibility))]
    public partial bool IsCurrent { get; set; }

    // 正在播放那一态的显隐不在这里：起伏条是 Controls/PlayingBars，它自己按 IsPlaying 管显隐，
    // 行模板直接把 IsCurrent 绑上去。多算一个 Visibility 反而多一处要同步的状态。

    public Visibility PlayGlyphVisibility => Vis(!IsCurrent && IsPointerOver);

    public Visibility IndexVisibility => Vis(!IsCurrent && !IsPointerOver);

    /// <summary>行尾「更多」按钮：悬停或菜单开着时可见。</summary>
    public Visibility MoreVisibility => Vis(IsPointerOver || IsMenuOpen);

    private static Visibility Vis(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
}
