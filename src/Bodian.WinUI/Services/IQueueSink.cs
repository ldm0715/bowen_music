using Bodian.Core.Models;

namespace Bodian.WinUI.Services;

/// <summary>
/// 把曲目送进<b>播放队列</b>的出口。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="ITrackNavigator"/> / <see cref="INoticeSink"/> 同一套手法：
/// <c>TrackActionsViewModel</c> 刻意不碰 WinUI 类型（这样它才能被 link 进离线测试工程），
/// 队列那边的真实实现在 <c>TrackActionsService</c> 里注入，于是菜单的两条新动作也能离屏测。
/// </para>
/// <para>
/// <b>名字里不要出现「歌单」</b>：那是服务器上的自建歌单，与这里的播放队列是两回事。
/// </para>
/// </remarks>
public interface IQueueSink
{
    /// <summary>插到当前曲目之后。队列为空时会直接开播。</summary>
    Task PlayNextAsync(Track track);

    /// <summary>加到队尾。队列为空时会直接开播。</summary>
    Task AddToQueueAsync(Track track);
}
