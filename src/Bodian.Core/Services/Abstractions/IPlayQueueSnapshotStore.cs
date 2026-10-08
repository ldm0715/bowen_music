using Bodian.Core.Models;

namespace Bodian.Core.Services.Abstractions;

/// <summary>
/// 播放队列快照的读写，<b>按作用域分开存</b>。<see cref="Load"/> 永不抛，读不出来时退回
/// <see cref="PlayQueueSnapshot.Default"/>（空队列）。
/// </summary>
/// <remarks>
/// <b>为什么这里要显式传 scope，而历史 / 搜索历史那两个不用。</b>
/// 那两个是「每次操作读-改-写整份文件」，问一次「当前是谁」就够；
/// 队列的内存副本由 <c>PlaybackCoordinator</c> 长期持有，切号时必须先把它写回<b>原来那个</b>作用域，
/// 而那一刻「当前是谁」已经是新账号了 —— 让 store 自己去问当前账号，会把上一个账号的队列写进新账号的目录。
/// 所以作用域由持有者显式传，store 不猜。
/// </remarks>
public interface IPlayQueueSnapshotStore
{
    /// <param name="scope">见 <see cref="ICurrentAccount.Scope"/>。</param>
    PlayQueueSnapshot Load(string scope);

    /// <returns>写成功返回 <c>true</c>；失败只记日志、不抛 —— 调用方据此回滚界面上的开关。</returns>
    bool Save(string scope, PlayQueueSnapshot snapshot);

    /// <summary>
    /// 删掉<b>所有</b>作用域下的队列文件。
    /// </summary>
    /// <remarks>
    /// 只服务「记住播放列表」这个开关：关掉时要抹掉盘上已存的队列，否则开关重新打开时
    /// 它们会「复活」。只清当前账号那份是不够的 —— 换号之后别人的队列会冒出来，
    /// 与「别在我的磁盘上留播放列表」这句话相反。
    /// </remarks>
    /// <returns>全部删除成功返回 <c>true</c>。</returns>
    bool DeleteAllScopes();
}
