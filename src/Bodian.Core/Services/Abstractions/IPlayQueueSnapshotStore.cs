using Bodian.Core.Models;

namespace Bodian.Core.Services.Abstractions;

/// <summary>
/// 播放队列快照的读写。<b><see cref="Load"/> 永不抛</b>，读不出来时退回
/// <see cref="PlayQueueSnapshot.Default"/>（空队列）。
/// </summary>
public interface IPlayQueueSnapshotStore
{
    PlayQueueSnapshot Load();

    /// <returns>写成功返回 <c>true</c>；失败只记日志、不抛 —— 调用方据此回滚界面上的开关。</returns>
    bool Save(PlayQueueSnapshot snapshot);
}
