using Bowen.Core.Models;

namespace Bowen.Core.Services.Abstractions;

/// <summary>
/// 「最近播放」的本地记录。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么本地仍是展示数据源</b>：本地记录不依赖登录与网络，未登录也能用。
/// 官方桌面端的历史是纯本地的（<c>addHistorySong</c> 写 SQL →
/// <c>%LOCALAPPDATA%\cn.wenyu.bodian\...\songDB.db</c>），整个桌面二进制里没有任何
/// 服务端历史路径；但该接口<b>不是</b>移动端专属 —— <c>playlist/history/add</c> 已用桌面
/// 请求头实测可写。跨端同步由 <see cref="IPlayHistorySync"/> 负责，见
/// <c>docs/play-history-sync.md</c>。
/// </para>
/// <para>
/// <b>同一首歌重复播放会被提到最前，不新增条目</b>：历史列表里出现十遍同一首歌没有意义。
/// </para>
/// </remarks>
public interface IPlayHistoryStore
{
    /// <summary>一条都没有时返回空数组，不返回 <c>null</c>。</summary>
    Task<IReadOnlyList<PlayHistoryEntry>> GetRecentAsync(
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>记一次播放。<paramref name="track"/> 已经在列表里时把它提到最前。</summary>
    Task RecordAsync(Track track, CancellationToken cancellationToken = default);

    /// <summary>清空并落盘。</summary>
    Task ClearAsync(CancellationToken cancellationToken = default);
}
