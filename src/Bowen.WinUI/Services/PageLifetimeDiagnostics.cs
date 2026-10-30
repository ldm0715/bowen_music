using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;

namespace Bowen.WinUI.Services;

/// <summary>
/// 页面存活诊断：回答「离开的页面到底有没有被回收」。
/// </summary>
/// <remarks>
/// <para>
/// 只看私有工作集分不清三种情况：页面仍被引用、页面已不可达但 GC 没跑、增长根本不在页面上。
/// 这里用弱引用登记每个显示过的页面，按类型统计仍存活的数量，并记录托管堆与 GC 次数。
/// </para>
/// <para>
/// 只在设置 <c>BODIAN_MEMORY_DIAGNOSTICS=1</c> 时启用；正常启动时登记直接返回。
/// <see cref="CollectAsync"/> 会强制回收，只用于判断，不在正式路径上调用。
/// </para>
/// </remarks>
internal static class PageLifetimeDiagnostics
{
    public static readonly bool IsEnabled = Environment.GetEnvironmentVariable("BODIAN_MEMORY_DIAGNOSTICS") == "1";

    private static readonly List<WeakReference<Page>> Pages = [];
    private static readonly ConditionalWeakTable<Page, object> Seen = new();

    /// <summary>登记一个显示过的页面。同一实例只登记一次。</summary>
    public static void Track(Page page)
    {
        if (!IsEnabled || Seen.TryGetValue(page, out _)) return;
        Seen.Add(page, new object());
        Pages.Add(new WeakReference<Page>(page));
    }

    /// <summary>写一行统计：仍存活的页面（按类型）、累计登记数、托管堆、GC 次数与进程提交内存。</summary>
    public static void Report(ILogger logger, string label)
    {
        if (!IsEnabled) return;
        var alive = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var total = Pages.Count;
        Pages.RemoveAll(reference =>
        {
            if (!reference.TryGetTarget(out var page)) return true;
            var name = page.GetType().Name;
            alive[name] = alive.GetValueOrDefault(name) + 1;
            return false;
        });
        Released += total - Pages.Count;
        using var process = Process.GetCurrentProcess();
        logger.LogInformation(
            "页面存活 {Label}：存活 {Alive} 个 [{Detail}]，已回收累计 {Released}，托管堆 {Heap:F1} MiB，GC {Gen0}/{Gen1}/{Gen2}，提交 {Commit:F1} MiB，工作集 {WorkingSet:F1} MiB",
            label, Pages.Count, string.Join(", ", alive.Select(pair => $"{pair.Key}={pair.Value}")), Released,
            GC.GetTotalMemory(false) / 1048576d, GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2),
            process.PrivateMemorySize64 / 1048576d, process.WorkingSet64 / 1048576d);
    }

    /// <summary>
    /// 诊断用的完整回收：隔开三次，每次带终结器等待。
    /// </summary>
    /// <remarks>
    /// CsWinRT 包装对象要跨几次 GC 才能收干净，一次紧挨一次的回收不够，
    /// 见 microsoft-ui-xaml #6894 中的分析。
    /// </remarks>
    public static async Task CollectAsync()
    {
        if (!IsEnabled) return;
        for (var pass = 0; pass < 3; pass++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Task.Delay(500);
        }
    }

    private static int Released;
}
