using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Bodian.ImeProbe;

internal static class Program
{
    internal static ProbeLog Log { get; private set; } = null!;

    [STAThread]
    private static void Main(string[] args)
    {
        using var log = new ProbeLog();
        Log = log;
        log.Write($"arguments={string.Join(' ', args)}");
        using var initializationHook = args.Contains("--legacy-ime", StringComparer.OrdinalIgnoreCase)
            ? LegacyImeInitializationHook.TryInstall(log) : null;
        RunWinUi();
    }

    // Keep XAML initialization after the isolated initialization experiment.
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
}
