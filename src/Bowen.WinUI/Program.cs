using System.Diagnostics;
using System.Runtime.CompilerServices;
using Bowen.Core.Services;
using Bowen.Core.Services.Implementations;
using Bowen.WinUI.Services;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Bowen.WinUI;

internal static class Program
{
    /// <summary>托盘「重启」传给新进程的开关，后面跟旧进程的 pid。</summary>
    private const string RestartWaitSwitch = "--restart-wait";

    private static readonly object LogGate = new();
    private static readonly List<string> StartupMessages = new();
    private static ILogger? _logger;
    internal static bool IsInputMethodCompatibilityActive { get; private set; }

    [STAThread]
    private static void Main(string[] args)
    {
        WaitForPreviousInstance(args);

        var enabled = ReadInputMethodCompatibility()
            || args.Contains("--ime-compatibility", StringComparer.OrdinalIgnoreCase);
        using var compatibility = enabled ? LegacyImeCompatibility.TryEnable(Report) : null;
        IsInputMethodCompatibilityActive = compatibility is not null;
        if (IsInputMethodCompatibilityActive)
            Report("输入法兼容版本：final-minimal，仅跳过初始化限制；不改 IMM 上下文、候选位置或托盘生命周期");
        RunWinUi();
    }

    /// <summary>
    /// 在 XAML 初始化前读输入法兼容开关。
    /// </summary>
    /// <remarks>
    /// <b>只读，不迁移、不写盘。</b> 这一步早于单实例判定；若在这里跑完整迁移，
    /// 用户第二次启动（只该唤醒已有实例）也会去动配置文件。
    /// 完整迁移在 <see cref="App"/> 构造函数里、单实例判定之后执行。
    /// </remarks>
    private static bool ReadInputMethodCompatibility()
    {
        try
        {
            if (File.Exists(AppPaths.ConfigFile) || File.Exists(AppPaths.ConfigFile + ".bak"))
            {
                return JsonAppSettingsStore.Load().InputMethod.CompatibilityEnabled;
            }

            return new JsonInputMethodSettingsStore().Load().CompatibilityEnabled;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 重启出来的新进程先等旧进程退完。
    /// </summary>
    /// <remarks>
    /// 旧进程退出时要释放播放引擎、托盘图标并把播放位置落盘；单实例互斥量的寿命又是进程寿命。
    /// 不等的话，新进程会在旧进程还活着时做单实例判定，把自己当成第二个实例，只把正在关闭的
    /// 窗口唤到前台 —— 重启就变成「点了没反应」。
    /// </remarks>
    private static void WaitForPreviousInstance(string[] args)
    {
        var index = Array.IndexOf(args, RestartWaitSwitch);
        if (index < 0 || index + 1 >= args.Length || !int.TryParse(args[index + 1], out var processId))
        {
            return;
        }

        try
        {
            using var previous = Process.GetProcessById(processId);
            // 收尾链是同步的，正常远不到一秒；上限只是兜住慢盘或杀毒扫描。
            previous.WaitForExit(TimeSpan.FromSeconds(20));
        }
        catch (ArgumentException)
        {
            // 旧进程已经退出，GetProcessById 会抛这个。
        }
    }

    // Resolve XAML only after the optional initialization compatibility handler is installed.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunWinUi()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            new App();
        });
    }

    internal static void AttachLogger(ILogger logger)
    {
        lock (LogGate)
        {
            _logger = logger;
            foreach (var message in StartupMessages) logger.LogInformation("{StartupMessage}", message);
            StartupMessages.Clear();
        }
    }

    internal static void Report(string message)
    {
        lock (LogGate)
        {
            if (_logger is null) StartupMessages.Add(message);
            else _logger.LogInformation("{StartupMessage}", message);
        }
    }
}
