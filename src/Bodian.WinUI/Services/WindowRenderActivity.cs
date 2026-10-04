using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;

namespace Bodian.WinUI.Services;

/// <summary>在 XAML 处理窗口消息前暂停视觉刷新；不改变音频播放状态。</summary>
internal sealed class WindowRenderActivity : IDisposable
{
    private const uint WmSize = 0x0005, WmNcDestroy = 0x0082, WmNcCalcSize = 0x0083;
    private const uint WmEnterSizeMove = 0x0231, WmExitSizeMove = 0x0232;
    private readonly nint _window;
    private readonly NativeMethods.SubclassProc _callback;
    private readonly bool _extendTopFrame = !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);
    private readonly DispatcherQueueTimer _transitionTimer;
    private readonly ILogger _logger;
    private bool _interactive;
    private bool _transition;
    private bool _suspended;
    private bool _disposed;
    private NativeMethods.WindowPosition? _pendingPosition;
    private readonly bool _diagnostics = Environment.GetEnvironmentVariable("BODIAN_LYRICS_DIAGNOSTICS") == "1";
    private long _statsStarted;
    private readonly List<double> _messageTimes = new();

    public WindowRenderActivity(nint window, DispatcherQueue dispatcher, ILoggerFactory? factory)
    {
        _window = window;
        _logger = (factory ?? NullLoggerFactory.Instance).CreateLogger<WindowRenderActivity>();
        _callback = OnWindowMessage;
        if (!NativeMethods.SetWindowSubclass(window, _callback, 1, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法监听窗口绘制状态");
        IsMinimized = NativeMethods.IsIconic(window);
        _suspended = IsMinimized;
        _transitionTimer = dispatcher.CreateTimer();
        _transitionTimer.Interval = TimeSpan.FromMilliseconds(50);
        _transitionTimer.IsRepeating = false;
        _transitionTimer.Tick += (_, _) =>
        {
            _transition = false;
            ReplayWindowPosition();
            Publish();
        };
        // SWP_FRAMECHANGED | NOMOVE | NOSIZE | NOZORDER | NOACTIVATE
        if (_extendTopFrame) NativeMethods.SetWindowPos(window, 0, 0, 0, 0, 0, 0x0037);
    }

    public event EventHandler? Changed;
    public event EventHandler? InteractionChanged;
    public bool IsInteractive => _interactive;
    public bool IsSuspended => _suspended;
    public bool IsMinimized { get; private set; }

    public void BeginTransition()
    {
        _transition = true;
        _transitionTimer.Stop();
        Publish();
    }

    public void EndTransition() => _transitionTimer.Start();

    private nint OnWindowMessage(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (_extendTopFrame && message == WmNcCalcSize && wParam != 0 && lParam != 0)
        {
            var frameResult = NativeMethods.DefSubclassProc(window, message, wParam, lParam);
            var style = (long)NativeMethods.GetWindowLongPtr(window, -16);
            // Cover the 1-physical-pixel top border on Windows 10, including fullscreen.
            // Preserve the other edges and the system client area for maximized windows.
            if ((style & 0x01000000L) == 0)
            {
                var rect = Marshal.PtrToStructure<NativeMethods.NativeRect>(lParam);
                rect.Top -= 1;
                Marshal.StructureToPtr(rect, lParam, false);
            }
            return frameResult;
        }
        try
        {
            switch (message)
            {
                case WmEnterSizeMove: _interactive = true; InteractionChanged?.Invoke(this, EventArgs.Empty); Publish(); break;
                case WmExitSizeMove:
                    _interactive = false;
                    InteractionChanged?.Invoke(this, EventArgs.Empty);
                    ReplayWindowPosition();
                    Publish();
                    break;
                case WmSize:
                    IsMinimized = wParam == 1;
                    Publish();
                    break;
                case WmNcDestroy: Dispose(); break;
            }
        }
        catch (Exception exception) { _logger.LogError(exception, "处理窗口绘制状态失败"); }
        if ((_interactive || _transition) && message == 0x0047 && lParam != 0)
        {
            var position = Marshal.PtrToStructure<NativeMethods.WindowPosition>(lParam);
            if ((position.Flags & 0x00C0) == 0 && !IsMinimized)
            {
                // 连续拖动或全屏切换中合并输入区域同步，避免 Windowing.Core 每次都对
                // Non Client Input Sink 调用 SetRegionRects + SetWindowPos。
                // DefWindowProc 继续产生 WM_SIZE/WM_MOVE，XAML 正常更新客户区。
                _pendingPosition = position;
                return NativeMethods.DefWindowProc(window, message, wParam, lParam);
            }
        }
        if (!_diagnostics || (message != WmSize && message != 0x0047))
            return NativeMethods.DefSubclassProc(window, message, wParam, lParam);
        var start = Stopwatch.GetTimestamp();
        var result = NativeMethods.DefSubclassProc(window, message, wParam, lParam);
        _messageTimes.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        if (_statsStarted == 0) _statsStarted = start;
        if (Stopwatch.GetElapsedTime(_statsStarted).TotalSeconds >= 5)
        {
            _messageTimes.Sort();
            _logger.LogInformation("窗口原生消息处理：{Count} 次，平均 {Average:F2} ms，P99 {P99:F2} ms，最长 {Maximum:F2} ms",
                _messageTimes.Count, _messageTimes.Average(), _messageTimes[(int)Math.Ceiling(_messageTimes.Count * 0.99) - 1], _messageTimes[^1]);
            _statsStarted = 0;
            _messageTimes.Clear();
        }
        return result;
    }

    private void ReplayWindowPosition()
    {
        if (_pendingPosition is not { } position) return;
        _pendingPosition = null;
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.WindowPosition>());
        try
        {
            Marshal.StructureToPtr(position, memory, false);
            NativeMethods.SendMessage(_window, 0x0047, 0, memory);
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    private void Publish()
    {
        var suspended = _transition || IsMinimized;
        if (_suspended == suspended) return;
        _suspended = suspended;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _transitionTimer.Stop();
        NativeMethods.RemoveWindowSubclass(_window, _callback, 1);
    }
}
