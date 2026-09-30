using Bodian.Core.Models.Account;
using Bodian.Core.Models.Login;

namespace Bodian.Core.Api;

/// <summary>
/// 扫码登录。负责二维码的三步协议、身份校验、以及会话的持久化。
/// </summary>
/// <remarks>
/// <para>
/// 三步拆成三个方法而不是一个大循环，是为了让 UI 掌握节奏：轮询间隔由调用方控制
/// （协议要求 ≥2 秒），界面可以显示倒计时、可以随时取消、可以换一张二维码重来。
/// </para>
/// <para>
/// <b><see cref="CompleteAsync"/> 是唯一会改动会话的地方</b>，且只有身份校验通过才会落盘。
/// </para>
/// </remarks>
public interface IBodianLogin
{
    /// <summary>当前是否有可用会话（含从磁盘恢复的）。</summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// 当前账号。未登录时为 <c>null</c>。
    /// </summary>
    /// <remarks>
    /// 从磁盘恢复的会话只带得回昵称/头像/会员状态（凭据里存的就是这几样）；
    /// 老凭据文件缺头像与会员字段，那两项会是 <c>null</c> / <c>false</c>。
    /// </remarks>
    BodianAccount? Account { get; }

    /// <summary>当前账号昵称，等价于 <c>Account?.Nickname</c>。</summary>
    string? Nickname { get; }

    /// <summary>登录成功、主动登出、或被服务端清会话时触发。</summary>
    event EventHandler? AccountChanged;

    /// <summary>创建二维码挑战。拿到的 <c>LandingPage</c> 才是要渲染进二维码的内容。</summary>
    Task<QrCodeChallenge> CreateChallengeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 轮询一次扫码状态。<b>不含等待</b>，间隔由调用方按 <see cref="LoginOptions.PollInterval"/> 控制。
    /// </summary>
    Task<QrScanStatus> PollAsync(QrCodeChallenge challenge, CancellationToken cancellationToken = default);

    /// <summary>
    /// 换取会话。遇到 <c>11027</c>（已扫未确认）会按配置重试，不当作失败。
    /// </summary>
    Task<LoginOutcome> CompleteAsync(QrCodeChallenge challenge, CancellationToken cancellationToken = default);

    /// <summary>启动时调一次：从磁盘恢复会话。没有可用凭据时返回 <c>false</c>，不改动任何状态。</summary>
    bool TryRestorePersistedSession();

    /// <summary>登出：清会话与磁盘凭据。</summary>
    void SignOut();
}
