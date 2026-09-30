using Microsoft.Extensions.Logging;

namespace Bodian.Core.Diagnostics;

/// <summary>
/// 把整个 <see cref="ILoggerFactory"/> 包一层，让所有 provider 的输出都先过脱敏。
/// </summary>
/// <remarks>
/// <para>
/// <b>这是日志脱敏的唯一收口。</b> 只要容器里的 <c>ILoggerFactory</c> 是这一个，
/// 任何 provider（Serilog 也好、内存 provider 也好）都绕不过它——
/// 这正是 <c>docs/roadmap.md</c> 要求的「做封装，不靠自觉」。
/// </para>
/// <para>
/// 它同时处理两处：
/// <list type="bullet">
/// <item><b>渲染后的消息文本</b>——把 formatter 的结果过一遍 <see cref="LogRedactor"/></item>
/// <item><b>结构化属性</b>——字符串值逐个脱敏，键与结构保持不变</item>
/// </list>
/// 所以结构化日志不会退化成字符串；只是字符串值被换掉了。
/// </para>
/// <para>
/// <b>它不该是唯一防线。</b> 传输层本来就不把 query 与 body 放进日志语句，
/// 这一层是第二道。两道都在，才不会有「某天有人顺手 log 了整个 URL」的事故。
/// </para>
/// </remarks>
public sealed class RedactingLoggerFactory : ILoggerFactory
{
    private readonly ILoggerFactory _inner;

    public RedactingLoggerFactory(ILoggerFactory inner)
        => _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public ILogger CreateLogger(string categoryName) => new RedactingLogger(_inner.CreateLogger(categoryName));

    public void AddProvider(ILoggerProvider provider) => _inner.AddProvider(provider);

    public void Dispose() => _inner.Dispose();

    private sealed class RedactingLogger(ILogger inner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            => RedactingState.Wrap(state) is RedactingState wrapped
                // 用 RedactingState 本身作 TState 参数：ILogger.BeginScope 要求 TState 非空，
                // 而 RedactingState 是引用类型，满足约束。
                ? inner.BeginScope(wrapped)
                : inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            inner.Log(
                logLevel,
                eventId,
                RedactingState.Wrap(state),
                exception,
                // 渲染后的文本也过一遍：即使状态不是键值对集合（自定义 ILogger 可能这么传），
                // formatter 的输出仍然会被脱敏。原状态在闭包里，所以这里直接用它渲染。
                (_, e) => LogRedactor.Redact(formatter(state, e)));
        }
    }

    /// <summary>
    /// 包住日志状态，把其中的字符串值逐个脱敏。
    /// </summary>
    /// <remarks>
    /// 非键值对形态的状态（自定义 state）原样透传，由 <see cref="RedactingLogger.Log{TState}"/>
    /// 在渲染层兜底——两层里至少有一层会生效。
    /// </remarks>
    private sealed class RedactingState : IReadOnlyList<KeyValuePair<string, object?>>
    {
        private readonly IReadOnlyList<KeyValuePair<string, object?>> _pairs;

        private RedactingState(IReadOnlyList<KeyValuePair<string, object?>> pairs) => _pairs = pairs;

        /// <summary>非键值对形态（含 null）原样透传，由渲染层兜底。</summary>
        public static object? Wrap<TState>(TState state)
            => state is IReadOnlyList<KeyValuePair<string, object?>> pairs
                ? new RedactingState(pairs)
                : state;

        public KeyValuePair<string, object?> this[int index]
        {
            get
            {
                var pair = _pairs[index];
                return pair.Value is string text
                    ? new KeyValuePair<string, object?>(pair.Key, LogRedactor.Redact(text))
                    : pair;
            }
        }

        public int Count => _pairs.Count;

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            for (var i = 0; i < _pairs.Count; i++)
            {
                yield return this[i];
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
