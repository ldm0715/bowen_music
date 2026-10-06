using System.Text;

namespace Bodian.ImeProbe;

internal sealed class ProbeLog : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly object _gate = new();
    private bool _disposed;

    public ProbeLog()
    {
        var directory = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bodian", "ime-probe");
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
