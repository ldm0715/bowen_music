using Bodian.Core.Models;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 榜单里的一首：曲目 + 它在榜上的名次。
/// </summary>
/// <remarks>
/// <b>名次是榜单的核心信息</b>，而它是「位置」不是曲目的字段 ——
/// 曲目对象里没有、也不该有。所以榜单类的列表用这个包一层。
/// </remarks>
/// <param name="Rank">名次，从 1 起。</param>
/// <param name="Track">曲目。</param>
public sealed record RankedTrack(int Rank, Track Track)
{
    /// <summary>名次文案。前三名在界面上会换个颜色，所以这里只管格式。</summary>
    public string RankText => Rank.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
