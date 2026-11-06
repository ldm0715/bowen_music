using Bowen.Core.Models;

namespace Bowen.Core.Services.Abstractions;

/// <summary>
/// 统一配置的唯一读写入口。
/// </summary>
/// <remarks>
/// <para>
/// <b>所有修改必须走 <see cref="Update"/> / <see cref="UpdateAndFlush"/>。</b>
/// 直接改 <see cref="Current"/> 上的属性不会安排落盘，也与其它写入者没有同步。
/// </para>
/// <para>
/// <b>单例。</b> 多个 store 各自「读整份 → 改一段 → 写整份」会互相覆盖；
/// 内存里只保留一份配置、写盘只走一个入口，才能消掉这类竞争。
/// </para>
/// </remarks>
public interface ISettingsStore
{
    /// <summary>当前内存副本。只读使用；修改请走 <see cref="Update"/>。</summary>
    AppSettings Current { get; }

    /// <summary>改内存并安排防抖落盘。写盘失败只记日志。</summary>
    /// <param name="mutate">在锁内对当前配置执行修改。</param>
    void Update(Action<AppSettings> mutate);

    /// <summary>改内存并立即落盘。失败敏感的开关用它拿真实结果。</summary>
    /// <returns>写盘成功返回 <c>true</c>。</returns>
    bool UpdateAndFlush(Action<AppSettings> mutate);

    /// <summary>把待写内容立即写盘。退出、重启前调用。</summary>
    /// <returns>写盘成功或本来就没有待写内容时返回 <c>true</c>。</returns>
    bool Flush();
}
