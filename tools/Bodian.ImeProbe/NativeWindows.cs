using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Bodian.ImeProbe;

internal sealed class NativeWindows(ProbeLog log)
{
    private readonly HashSet<int> _extraProcesses = new();
    private string? _previous;

    public static string VirtualScreen => $"{GetSystemMetrics(76)},{GetSystemMetrics(77)},{GetSystemMetrics(78)},{GetSystemMetrics(79)}";
    public void AddProcess(int id) => _extraProcesses.Add(id);

    public void Capture()
    {
        try
        {
            var processes = new Dictionary<int, string> { [Environment.ProcessId] = "ime-probe" };
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        if (process.ProcessName.StartsWith("wetype", StringComparison.OrdinalIgnoreCase)
                            || _extraProcesses.Contains(process.Id)) processes[process.Id] = process.ProcessName;
                    }
                    catch (InvalidOperationException) { }
                }
            }
            var rows = new List<string>();
            var gui = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
            if (GetGUIThreadInfo(GetCurrentThreadId(), ref gui))
            {
                var focusThread = GetWindowThreadProcessId(gui.Focus, out var focusProcess);
                var layout = GetKeyboardLayout(focusThread);
                rows.Add($"focus=0x{gui.Focus:X} pid={focusProcess} caret=0x{gui.Caret:X} caret-rect={gui.CaretRect} hkl=0x{layout:X}");
            }
            EnumWindows((window, _) =>
            {
                GetWindowThreadProcessId(window, out var processId);
                if (!processes.TryGetValue((int)processId, out var processName)) return true;
                AddWindow(window, processId, processName, rows);
                EnumChildWindows(window, (child, _) =>
                {
                    GetWindowThreadProcessId(child, out var childProcess);
                    if (processes.TryGetValue((int)childProcess, out var childName)) AddWindow(child, childProcess, childName, rows);
                    return true;
                }, 0);
                return true;
            }, 0);
            var snapshot = string.Join(Environment.NewLine, rows.Order(StringComparer.Ordinal));
            if (snapshot == _previous) return;
            _previous = snapshot;
            log.Write("window-snapshot\n" + snapshot);
        }
        catch (Exception exception) { log.Write($"snapshot-error {exception.GetType().Name}: {exception.Message}"); }
    }

    private static void AddWindow(nint window, uint processId, string processName, List<string> rows)
    {
        var className = new StringBuilder(256);
        GetClassName(window, className, className.Capacity);
        GetWindowRect(window, out var rect);
        var cloakResult = DwmGetWindowAttribute(window, 14, out var cloaked, sizeof(int));
        rows.Add($"hwnd=0x{window:X} pid={processId} process={processName} class={className} visible={IsWindowVisible(window)} rect={rect} owner=0x{GetWindow(window, 4):X} exstyle=0x{GetWindowLongPtr(window, -20):X} cloaked={(cloakResult >= 0 ? cloaked : -1)}");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
        public override readonly string ToString() => $"{Left},{Top},{Right},{Bottom}";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public uint Size, Flags;
        public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public Rect CaretRect;
    }

    private delegate bool EnumWindowProc(nint window, nint parameter);
    [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowProc callback, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint window, EnumWindowProc callback, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder name, int maximum);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("user32.dll")] private static extern nint GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, uint attribute, out int value, int size);
}
