using Bodian.Core.Models.Account;
using Bodian.Core.Models.Login;

namespace Bodian.Core.Api;

/// <summary>
/// 登录。覆盖扫码与手机号两条换会话路径，以及身份校验与会话持久化。
/// </summary>
/// <remarks>
/// <para>
/// 扫码的三步拆成三个方法而不是一个大循环，是为了让 UI 掌握节奏：轮询间隔由调用方控制
/// （协议要求 ≥2 秒），界面可以显示倒计时、可以随时取消、可以换一张二维码重来。
/// </para>
/// <para>
/// <b><see cref="CompleteAsync"/> 与 <see cref="LoginByPhoneAsync"/> 是仅有的两个会改动
/// 会话的入口</b>，两者共用同一条落地路径，且只有身份校验通过才会落盘。
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

    /// <summary>
    /// 手机号登录第 1 步：请求短信验证码。<b>不碰会话。</b>
    /// </summary>
    /// <remarks>
    /// <b>手机号是 PII —— 不写日志、不落盘、不进 fixture。</b> 调用方应先经
    /// <see cref="Services.MobileNumber.Normalize"/> 归一化再传进来。
    /// </remarks>
    Task<SmsSendOutcome> SendSmsCodeAsync(string mobile, CancellationToken cancellationToken = default);

    /// <summary>
    /// 手机号登录第 2 步：用验证码换会话。
    /// </summary>
    /// <remarks>
    /// 成功时走的是与 <see cref="CompleteAsync"/> <b>完全相同</b>的落地路径
    /// （身份校验 → 先落盘 → 再改内存会话 → 触发 <see cref="AccountChanged"/>）。
    /// <see cref="LoginOutcome.Expired"/> 在本路径上不会出现。
    /// </remarks>
    Task<LoginOutcome> LoginByPhoneAsync(string mobile, string verifyCode, CancellationToken cancellationToken = default);

    /// <summary>启动时调一次：从磁盘恢复会话。没有可用凭据时返回 <c>false</c>，不改动任何状态。</summary>
    bool TryRestorePersistedSession();

    /// <summary>登出：清会话与磁盘凭据。</summary>
    void SignOut();
}
