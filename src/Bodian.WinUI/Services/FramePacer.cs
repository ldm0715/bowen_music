using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Bodian.WinUI.Services;

/// <summary>用高精度可等待计时器按绝对时间调度；不忙等，不修改系统计时器分辨率。</summary>
internal sealed class FramePacer : IDisposable
{
    private readonly SafeWaitHandle _timer;
    private readonly nint[] _handles;
    private double _frameTicks;
    private double _deadline;

    public FramePacer(double framesPerSecond, WaitHandle stop)
    {
        _frameTicks = Stopwatch.Frequency / framesPerSecond;
        _timer = CreateWaitableTimerEx(0, null, 0x00000002, 0x001F0003);
        if (_timer.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法创建高精度帧计时器");
        _handles = [_timer.DangerousGetHandle(), stop.SafeWaitHandle.DangerousGetHandle()];
        Reset();
    }

    public void Reset() => _deadline = Stopwatch.GetTimestamp();
    public void SetFrameRate(double framesPerSecond) => _frameTicks = Stopwatch.Frequency / framesPerSecond;

    public bool WaitForNextFrame()
    {
        var now = Stopwatch.GetTimestamp();
        _deadline += _frameTicks;
        // 长帧之后跳过已经错过的时间槽；不挤占 CPU 补画过时帧。
        if (_deadline <= now) _deadline += (Math.Floor((now - _deadline) / _frameTicks) + 1) * _frameTicks;
        var dueTime = -(long)Math.Ceiling((_deadline - now) * TimeSpan.TicksPerSecond / Stopwatch.Frequency);
        if (!SetWaitableTimer(_timer, ref dueTime, 0, 0, 0, false))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法设置帧计时器");
        var result = WaitForMultipleObjects((uint)_handles.Length, _handles, false, uint.MaxValue);
        if (result == uint.MaxValue) throw new Win32Exception(Marshal.GetLastWin32Error(), "等待帧计时器失败");
        return result == 0;
    }

    public void Dispose() => _timer.Dispose();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeWaitHandle CreateWaitableTimerEx(nint attributes, string? name, uint flags, uint access);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimer(SafeWaitHandle timer, ref long dueTime, int period,
        nint completionRoutine, nint argument, [MarshalAs(UnmanagedType.Bool)] bool resume);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForMultipleObjects(uint count, [In] nint[] handles,
        [MarshalAs(UnmanagedType.Bool)] bool waitAll, uint milliseconds);
}
