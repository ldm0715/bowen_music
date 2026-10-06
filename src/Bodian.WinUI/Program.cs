using System.Runtime.CompilerServices;
using Bodian.Core.Services.Implementations;
using Bodian.WinUI.Services;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Bodian.WinUI;

internal static class Program
{
    private static readonly object LogGate = new();
    private static readonly List<string> StartupMessages = new();
    private static ILogger? _logger;
    internal static bool IsInputMethodCompatibilityActive { get; private set; }

    [STAThread]
    private static void Main(string[] args)
    {
        var enabled = new JsonInputMethodSettingsStore().Load().CompatibilityEnabled
            || args.Contains("--ime-compatibility", StringComparer.OrdinalIgnoreCase);
        using var compatibility = enabled ? LegacyImeCompatibility.TryEnable(Report) : null;
        IsInputMethodCompatibilityActive = compatibility is not null;
        if (IsInputMethodCompatibilityActive)
            Report("输入法兼容版本：final-minimal，仅跳过初始化限制；不改 IMM 上下文、候选位置或托盘生命周期");
        RunWinUi();
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
