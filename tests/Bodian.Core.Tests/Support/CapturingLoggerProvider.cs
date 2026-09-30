using Microsoft.Extensions.Logging;

namespace Bodian.Core.Tests.Support;

internal sealed record CapturedLogEntry(
    string Category,
    LogLevel Level,
    string Message,
    IReadOnlyList<KeyValuePair<string, object?>> State);

/// <summary>
/// 把日志收进内存，供脱敏测试断言。
/// </summary>
/// <remarks>
/// 用它当 <c>RedactingLoggerFactory</c> 的内层 provider，就能验证「别的 provider 也绕不过脱敏」。
/// </remarks>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly List<CapturedLogEntry> _entries = [];

    public IReadOnlyList<CapturedLogEntry> Entries => _entries;

    public string AllText => string.Join('\n', _entries.Select(e => e.Message));

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    public void Dispose()
    {
        // 没有非托管资源
    }

    private void Add(CapturedLogEntry entry) => _entries.Add(entry);

    private sealed class CapturingLogger(CapturingLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => owner.Add(new CapturedLogEntry(
                category,
                logLevel,
                formatter(state, exception),
                state as IReadOnlyList<KeyValuePair<string, object?>> ?? []));
    }
}
