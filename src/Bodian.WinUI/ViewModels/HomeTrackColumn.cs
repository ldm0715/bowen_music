using Bodian.Core.Models.Home;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 发现页单曲模块里的一列：几首曲目纵向排成一列，列与列横向排开。
/// </summary>
/// <remarks>
/// <b>这是排版决定，不是接口的形状</b> —— 接口给的是一整片曲目，切几首一列是界面的事，
/// 所以放视图层这边，不塞进 <see cref="HomeCard"/>。
/// </remarks>
/// <param name="Tracks">这一列里的曲目。</param>
public sealed record HomeTrackColumn(IReadOnlyList<HomeCard> Tracks);
