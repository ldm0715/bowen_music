namespace Bodian.Core.Models.Login;

/// <summary>
/// 一次扫码登录的二维码挑战。
/// </summary>
/// <remarks>
/// <see cref="Key"/> 是后续轮询与换取会话的凭据，<see cref="LandingPage"/> 才是要渲染进二维码的内容。
/// 两者不是一回事 —— 把 key 直接编码进二维码是行不通的。
/// </remarks>
/// <param name="Key">服务端签发的二维码 key。</param>
/// <param name="LandingPage">二维码要承载的完整落地页地址。</param>
/// <param name="CreatedAt">创建时刻。有效期按 5 分钟处理。</param>
public sealed record QrCodeChallenge(string Key, Uri LandingPage, DateTimeOffset CreatedAt)
{
    /// <summary>二维码有效期。服务端未明确给出，按文档建议的 5 分钟处理。</summary>
    public static TimeSpan Lifetime { get; } = TimeSpan.FromMinutes(5);

    /// <summary>按创建时刻算出的到期时间。</summary>
    public DateTimeOffset ExpiresAt => CreatedAt + Lifetime;
}

/// <summary>
/// 轮询二维码得到的扫码状态。
/// </summary>
public enum QrScanStatus
{
    /// <summary>还没人扫（服务端 <c>status=1</c>）。继续轮询。</summary>
    Waiting = 0,

    /// <summary>二维码已过期（服务端 <c>status=2</c>）。应提示用户重新获取。</summary>
    Expired = 1,

    /// <summary>已扫码并确认（服务端 <c>status=3</c>）。立即换取会话。</summary>
    Confirmed = 2,

    /// <summary>服务端返回了预期之外的状态值。按「继续等待」处理并记录。</summary>
    Unknown = 3,
}
