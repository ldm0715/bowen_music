using System.Diagnostics;
using Bodian.WinUI.ViewModels;
using Bodian.WinUI.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bodian.WinUI.Services;

/// <summary>可重复的大数据 UI 验证入口；只有显式设置诊断环境变量时启用。</summary>
internal static class PerformanceScenarios
{
    public static async Task RunAsync(IServiceProvider services)
    {
        if (Environment.GetEnvironmentVariable("BODIAN_LYRICS_DIAGNOSTICS") != "1"
            || Environment.GetEnvironmentVariable("BODIAN_UI_PERF_SCENARIO") != "search-list") return;
        var count = int.TryParse(Environment.GetEnvironmentVariable("BODIAN_UI_PERF_ITEMS"), out var requested)
            ? Math.Clamp(requested, 1, 50000) : 10000;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("PerformanceScenarios");
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            var start = Stopwatch.GetTimestamp();
            services.GetRequiredService<SearchViewModel>().PopulatePerformanceSample(count);
            services.GetRequiredService<INavigationService>().NavigateRoot<SearchPage>();
            logger.LogInformation("性能场景 search-list：{Count} 首，准备 {Elapsed:F2} ms", count, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
        catch (Exception exception) { logger.LogError(exception, "启动本机 UI 性能场景失败"); }
    }
}
