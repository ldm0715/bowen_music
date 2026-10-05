using Bodian.Core.Models;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 「查看歌手」弹窗里的一格。
/// </summary>
/// <param name="Id">
/// 歌手 id。<b>只有为正才跳得过去</b> —— 判据只看这个，不发请求核实（见 <see cref="IsAvailable"/>）。
/// </param>
/// <param name="Name">歌手名。</param>
/// <param name="Avatar">头像地址，可能为空。</param>
/// <remarks>
/// <b>为什么不直接用 <see cref="TrackArtist"/></b>：网格的容器样式只能用经典 <c>{Binding}</c>
/// （<c>x:Bind</c> 在 <c>ItemContainerStyle</c> 里拿不到数据类型，见 <c>TrackMoreButton.xaml</c>），
/// 所以需要一个<b>可绑定的布尔属性</b>来驱动「可不可点」，而它在
/// <see cref="TrackArtist"/> 上不存在。
/// </remarks>
public sealed record ArtistChoice(long Id, string Name, Uri? Avatar)
{
    /// <summary>
    /// 能不能跳。<c>false</c> 时界面画默认黑头像且不可点击。
    /// </summary>
    /// <remarks>
    /// 服务端没给 id 的条目（以及按 <c>&amp;</c> 拆艺人串拆出来的那些）走这一支。
    /// 只判 id、不去请求核实：打开弹窗不该等 N 次网络往返，而且拿不到 id 本来也请求不了。
    /// </remarks>
    public bool IsAvailable => Id > 0;

    /// <summary>
    /// 转成歌手详情页的入参。
    /// </summary>
    /// <remarks>
    /// 转换收在这里，界面与 ViewModel 都不必各自再拼一次。
    /// </remarks>
    public Artist ToArtist() => new() { Id = Id, Name = Name, CoverImage = Avatar };
}
