using Bodian.Core.Models;
using Bodian.Core.Services.Abstractions;

namespace Bodian.Core.Tests.Support;

/// <summary>
/// 内存版队列快照仓库：记账 + 可注入写失败。
/// </summary>
/// <remarks>
/// <see cref="Saves"/> 是有序的，断言时看最后一项就是「最近一次落盘的内容」。
/// </remarks>
internal sealed class FakePlayQueueSnapshotStore : IPlayQueueSnapshotStore
{
    /// <summary>下一次 <see cref="Load"/> 会读到的东西。测试起手就把它摆成「上次退出时的样子」。</summary>
    public PlayQueueSnapshot Stored { get; set; } = PlayQueueSnapshot.Default;

    /// <summary>每一次 <see cref="Save"/> 收到的快照，按先后顺序。</summary>
    public List<PlayQueueSnapshot> Saves { get; } = [];

    /// <summary>置上之后每次 <see cref="Save"/> 都返回 <c>false</c>，用来验开关回滚。</summary>
    public bool FailSaves { get; set; }

    public PlayQueueSnapshot Load() => Stored;

    public bool Save(PlayQueueSnapshot snapshot)
    {
        Saves.Add(snapshot);

        if (FailSaves) { return false; }

        Stored = snapshot;
        return true;
    }
}
