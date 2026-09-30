namespace Bodian.Core.Models.Login;

/// <summary>
/// 扫码登录的节奏参数。
/// </summary>
/// <remarks>
/// <para>
/// <b>轮询间隔协议要求不小于 2 秒</b>，所以 <see cref="Default"/> 就是 2 秒。
/// 不在循环里用 <c>Math.Max</c> 夹紧：夹紧会让测试没法把间隔调成 0 全速跑。
/// </para>
/// </remarks>
public sealed record LoginOptions
{
    /// <summary>生产用默认值。</summary>
    public static LoginOptions Default { get; } = new();

    /// <summary>轮询扫码状态的间隔。协议要求 ≥2 秒。</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>换取会话连续返回 11027（已扫未确认）时的最大尝试次数。</summary>
    public int MaxExchangeAttempts { get; init; } = 5;

    /// <summary>11027 两次尝试之间的等待。</summary>
    public TimeSpan ExchangeRetryInterval { get; init; } = TimeSpan.FromSeconds(2);
}
