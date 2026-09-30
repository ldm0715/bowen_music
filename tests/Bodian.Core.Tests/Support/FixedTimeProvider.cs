namespace Bodian.Core.Tests.Support;

/// <summary>
/// 冻结时间的 <see cref="TimeProvider"/>，用来让签名时间戳可复现。
/// </summary>
/// <remarks>
/// 自己写十几行，不引 <c>Microsoft.Extensions.TimeProvider.Testing</c> 包——
/// 需要的只有「<c>GetUtcNow</c> 返回定值」这一件事。
/// </remarks>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    /// <summary><c>fixtures/sign-golden.json</c> 里那个时间戳对应的时刻。</summary>
    public static FixedTimeProvider Golden { get; } =
        new(DateTimeOffset.FromUnixTimeMilliseconds(1790750540920));

    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
