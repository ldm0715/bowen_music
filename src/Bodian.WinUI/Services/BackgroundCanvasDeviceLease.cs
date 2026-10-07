using Microsoft.Graphics.Canvas;

namespace Bodian.WinUI.Services;

/// <summary>后台绘图共用一台设备，与 UI 的 CanvasControl 设备隔离；最后一个绘图线程结束后释放。</summary>
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
            if (ReferenceEquals(_current?.Device, sender)) _current = null;
    }

    public void Dispose()
    {
        lock (Gate)
        {
            if (_disposed) return;
            _disposed = true;
            if (--_shared.Leases != 0) return;
            if (ReferenceEquals(_current, _shared)) _current = null;
            _shared.Device.DeviceLost -= OnDeviceLost;
            _shared.Device.Dispose();
        }
    }

    private sealed class SharedDevice(CanvasDevice device)
    {
        public CanvasDevice Device { get; } = device;
        public int Leases { get; set; }
    }
}
