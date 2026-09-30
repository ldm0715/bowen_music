namespace Bodian.Core.Tests.Support;

/// <summary>
/// 时间可以手动推进的 <see cref="TimeProvider"/>，给歌词时钟的插值测试用。
/// </summary>
/// <remarks>
/// <b>必须一并覆写 <see cref="TimestampFrequency"/>。</b> 默认实现取自 <c>Stopwatch.Frequency</c>，
/// 那个值跨平台不同（Windows 10^7、Linux 10^9），不覆写的话测试结果会跟机器有关。
/// 定成 <see cref="TimeSpan.TicksPerSecond"/> 之后，一个时间戳单位就等于一个 tick。
/// </remarks>
internal sealed class ManualTimeProvider : TimeProvider
{
    private long _timestamp;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _timestamp;

    /// <summary>推进时间。</summary>
    public void Advance(TimeSpan delta) => _timestamp += delta.Ticks;
}
