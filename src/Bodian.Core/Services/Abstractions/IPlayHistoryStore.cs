using Bodian.Core.Models;

namespace Bodian.Core.Services.Abstractions;

/// <summary>
/// 「最近播放」的本地记录。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么是本地而不是接口</b>：官方桌面端的历史就是纯本地的
/// （<c>addHistorySong</c> 写 SQL → <c>%LOCALAPPDATA%\cn.wenyu.bodian\...\songDB.db</c>），
/// 整个桌面端二进制里没有任何读取历史的服务端路径。移动端虽然有
/// <c>playlist/history/page</c>，但那是移动端专属，桌面头能不能对上静态无法回答。
/// 见 <c>docs/library-sidebar.md</c> §6。
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
