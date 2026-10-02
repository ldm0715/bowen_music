namespace Bodian.Core.Threading;

/// <summary>高频状态只保留最新值，最多向消费线程投递一个待执行回调。</summary>
public sealed class LatestValueDispatcher<T>(Func<Action, bool> enqueue, Action<T> publish) where T : class
{
    private readonly Func<Action, bool> _enqueue = enqueue ?? throw new ArgumentNullException(nameof(enqueue));
    private readonly Action<T> _publish = publish ?? throw new ArgumentNullException(nameof(publish));
    private T? _latest;
    private int _scheduled;

    public bool Post(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Volatile.Write(ref _latest, value);
        return Schedule();
    }

    private bool Schedule()
    {
        if (Interlocked.CompareExchange(ref _scheduled, 1, 0) != 0) return true;
        if (_enqueue(Drain)) return true;
        Volatile.Write(ref _scheduled, 0);
        return false;
    }

    private void Drain()
    {
        try
        {
            if (Interlocked.Exchange(ref _latest, null) is { } latest) _publish(latest);
        }
        finally
        {
            Volatile.Write(ref _scheduled, 0);
            if (Volatile.Read(ref _latest) is not null) Schedule();
        }
    }
}
