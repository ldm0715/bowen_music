using System.Text;

namespace Bodian.ImeProbe;

internal sealed class ProbeLog : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly object _gate = new();
    private bool _disposed;

    public ProbeLog()
    {
        // 与应用的数据目录同名（AppPaths 里的 "Bowen"）。这里不做改名迁移 ——
        // 这些是一次性排查日志，重建没有任何损失，而留在旧目录反而更乱。
        var directory = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bowen", "ime-probe");
        Directory.CreateDirectory(directory);
        Path = System.IO.Path.Combine(directory, $"winui-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log");
        _writer = new StreamWriter(Path, false, new UTF8Encoding(false)) { AutoFlush = true };
        Write($"start pid={Environment.ProcessId} thread={NativeWindows.GetCurrentThreadId()} os={Environment.OSVersion} runtime={Environment.Version}");
    }

    public string Path { get; }
    public event Action<string>? LineWritten;

    public void Write(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} {message}";
        lock (_gate)
        {
            if (_disposed) return;
            _writer.WriteLine(line);
        }
        LineWritten?.Invoke(line);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _writer.Dispose();
        }
    }
}
