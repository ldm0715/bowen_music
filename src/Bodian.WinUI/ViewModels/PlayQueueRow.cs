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

    /// <summary>在队列里的位置，从 0 起。删除与切歌都按它定位。</summary>
    internal int Position { get; init; }

    public string OrdinalText => (Position + 1).ToString("D2", System.Globalization.CultureInfo.InvariantCulture);

    public string Title => Source.Title;

    public string ArtistText => Source.ArtistText;

    public Uri? CoverImage => Source.CoverImage;

    public string DurationText => Formats.Duration(Source.Duration);

    /// <summary>这一行是不是正在播放的那首。起伏条自己按它管显隐。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OrdinalVisibility))]
    [NotifyPropertyChangedFor(nameof(CanRemove))]
    public partial bool IsCurrent { get; set; }

    /// <summary>正在播放那一行不显示序号，让位给起伏条。</summary>
    public Visibility OrdinalVisibility => Formats.VisibleWhenFalse(IsCurrent);

    /// <summary>
    /// 正在播放的那一首不给删。
    /// </summary>
    /// <remarks>
    /// 删掉当前曲目之后「要不要自动切下一首」没有不别扭的答案，所以干脆把这一行的删除按钮置灰；
    /// 想跳过它就点下一首。界面这样定，<c>PlayQueue.RemoveItem</c> 就永远不会删到游标位置。
    /// </remarks>
    public bool CanRemove => !IsCurrent;
}
