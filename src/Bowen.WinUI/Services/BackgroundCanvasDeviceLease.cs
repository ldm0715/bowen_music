using Microsoft.Graphics.Canvas;

namespace Bowen.WinUI.Services;

/// <summary>后台绘图共用一台常驻设备，与 UI 的 CanvasControl 设备隔离。</summary>
/// <remarks>
/// 设备在进程内只建一次，最后一个绘图线程结束后不销毁，只归还缓存。
/// 实测每建、销毁一次 CanvasDevice 与配套的合成绘图设备，驱动侧都会留下句柄和原生内存；
/// 进出歌词页、歌词页开着时最小化恢复各触发一次，长时间使用会持续累积。
/// 常驻的代价是一份固定占用。只有设备丢失后才换新设备，旧设备在最后一个使用者放下时释放。
/// </remarks>
internal sealed class BackgroundCanvasDeviceLease : IDisposable
{
    private static readonly object Gate = new();
    private static SharedDevice? _current;
    private readonly SharedDevice _shared;
    private bool _disposed;

    private BackgroundCanvasDeviceLease(SharedDevice shared) => _shared = shared;
    public CanvasDevice Device => _shared.Device;

    public static BackgroundCanvasDeviceLease Acquire()
    {
        lock (Gate)
        {
            if (_current is null)
            {
                // 限制效果中间表面缓存，避免切换不同歌词/封面把设备缓存推到历史峰值。
                var device = new CanvasDevice { MaximumCacheSize = 8UL * 1024 * 1024 };
                device.DeviceLost += OnDeviceLost;
                _current = new SharedDevice(device);
            }
            _current.Leases++;
            return new BackgroundCanvasDeviceLease(_current);
        }
    }

    private static void OnDeviceLost(CanvasDevice sender, object args)
    {
        lock (Gate)
        {
            if (!ReferenceEquals(_current?.Device, sender)) return;
            sender.DeviceLost -= OnDeviceLost;
            var lost = _current;
            _current = null;
            if (lost.Leases == 0) lost.Device.Dispose();
        }
    }

    /// <summary>
    /// 让常驻设备归还驱动侧的缓存。
    /// </summary>
    /// <remarks>
    /// <b>设备常驻意味着这些缓存不会随页面离开自动消失。</b> 歌词页每次大跳都会为新的当前行
    /// 重建整行位图与模糊输出，驱动把释放掉的显存留在自己的堆里；页面上来回拖几次，
    /// 私有内存就会停在「唱过的最宽那几行」的峰值。离开页面、以及跳转落定后各调用一次，
    /// 把当前没在用的缓存还回去。正在使用的资源不会被回收，所以可以随时调用。
    /// </remarks>
    public static void TrimShared()
    {
        lock (Gate)
        {
            if (_current is not { } shared) return;

            try { shared.Device.Trim(); }
            catch (System.Runtime.InteropServices.COMException) { }
        }
    }

    public void Dispose()
    {
        lock (Gate)
        {
            if (_disposed) return;
            _disposed = true;
            if (--_shared.Leases != 0) return;
            if (!ReferenceEquals(_current, _shared))
            {
                // 已丢失的旧设备：最后一个使用者放下后释放。
                _shared.Device.Dispose();
                return;
            }
            // 常驻设备：没有使用者时只归还驱动缓存的显存，设备本身保留。
            // 设备丢失的通知可能晚于这一步，那时 Trim 会失败，交给 DeviceLost 处理。
            try { _shared.Device.Trim(); }
            catch (System.Runtime.InteropServices.COMException) { }
        }
    }

    private sealed class SharedDevice(CanvasDevice device)
    {
        public CanvasDevice Device { get; } = device;
        public int Leases { get; set; }
    }
}
