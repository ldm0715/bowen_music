using Bodian.Core.Models;
using Bodian.Core.Services.Abstractions;

namespace Bodian.Core.Tests.Support;

/// <summary>
/// 内存版队列快照仓库：按作用域记账 + 可注入写失败。
/// </summary>
/// <remarks>
/// <see cref="Saves"/> 是有序的，断言时看最后一项就是「最近一次落盘的内容」；
/// 它带上作用域，是为了能验「切号时先把旧账号那份写回去」。
/// <see cref="Stored"/> 是匿名桶的便捷入口 —— 不关心账号的用例照旧只碰它。
/// </remarks>
internal sealed class FakePlayQueueSnapshotStore : IPlayQueueSnapshotStore
{
    /// <summary><see cref="Stored"/> 读写的那一格。与 <see cref="ICurrentAccount.Scope"/> 的匿名值一致。</summary>
    public const string DefaultScope = "anonymous";

    private readonly Dictionary<string, PlayQueueSnapshot> _byScope = new(StringComparer.Ordinal);

    /// <summary>匿名桶里的东西。测试起手就把它摆成「上次退出时的样子」。</summary>
    public PlayQueueSnapshot Stored
    {
        get => Load(DefaultScope);
        set => _byScope[DefaultScope] = value;
    }

    /// <summary>每一次 <see cref="Save"/> 收到的（作用域, 快照），按先后顺序。</summary>
    public List<(string Scope, PlayQueueSnapshot Snapshot)> Saves { get; } = [];

    /// <summary>置上之后每次 <see cref="Save"/> 都返回 <c>false</c>，用来验开关回滚。</summary>
    public bool FailSaves { get; set; }

    /// <summary><see cref="DeleteAllScopes"/> 被调了几次。</summary>
    public int DeleteAllCount { get; private set; }

    public PlayQueueSnapshot Load(string scope) =>
        _byScope.TryGetValue(scope, out var snapshot) ? snapshot : PlayQueueSnapshot.Default;

    public bool Save(string scope, PlayQueueSnapshot snapshot)
    {
        Saves.Add((scope, snapshot));

        if (FailSaves) { return false; }

        _byScope[scope] = snapshot;
        return true;
    }

    public bool DeleteAllScopes()
    {
        DeleteAllCount++;

        if (FailSaves) { return false; }

        _byScope.Clear();
        return true;
    }
}
