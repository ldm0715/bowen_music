namespace Bowen.Core.Services.Abstractions;

/// <summary>
/// 把桌面端的播放行为同步到账号的「最近播放」。
/// </summary>
/// <remarks>
/// <para>
/// <b>这是写操作，失败必须静默。</b> 它不参与播放决策，也不提供任何界面状态；
/// 调用方只需要在「这首歌真的播起来了」时调用一次，其余交给实现。
/// </para>
/// <para>
/// 未登录时什么都不做，不发请求。协议与风险见 <c>docs/play-history-sync.md</c>。
/// </para>
/// </remarks>
public interface IPlayHistorySync
{
    /// <summary>
    /// 上报一次歌曲播放。方法<b>立即返回</b>，网络请求在后台完成。
    /// </summary>
    void NotifyPlayed(long musicId);

    /// <summary>
    /// 上报一次歌单 / 专辑播放。调用方负责去重，只在播放上下文变化时调用。
    /// </summary>
    /// <param name="historySourceType"><c>2</c> = 歌单，<c>3</c> = 专辑。</param>
    /// <param name="sourceId">歌单 / 专辑 id。</param>
    /// <param name="playlistType">对象自身的 <c>sourceType</c>，必须透传。</param>
    void NotifyCollectionPlayed(int historySourceType, long sourceId, int playlistType);
}
