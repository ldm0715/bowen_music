using Bodian.Core.Models.Account;
using Bodian.Core.Models.Login;
using Bodian.Core.Services.Abstractions;

namespace Bodian.Core.Api;

/// <summary>
/// 登录。覆盖扫码与手机号两条换会话路径，以及身份校验、会话持久化、账号切换。
/// </summary>
/// <remarks>
/// <para>
/// 扫码的三步拆成三个方法而不是一个大循环，是为了让 UI 掌握节奏：轮询间隔由调用方控制
/// （协议要求 ≥2 秒），界面可以显示倒计时、可以随时取消、可以换一张二维码重来。
/// </para>
/// <para>
/// <b>会改动会话的入口有三个</b>：<see cref="CompleteAsync"/>、<see cref="LoginByPhoneAsync"/>
/// 与 <see cref="SwitchTo"/>。前两者共用同一条落地路径，且只有身份校验通过才会落盘；
/// 后者用的是已经落过盘的那份凭据，不重新认证。
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

    /// <summary>登录成功、主动登出、被服务端清会话、以及切换账号时触发。</summary>
    event EventHandler? AccountChanged;

    /// <summary>
    /// 上次会话结束是不是服务端判死的（<c>11012</c>），而不是用户主动登出。
    /// </summary>
    /// <remarks>
    /// <b>界面读它来区分两种「回到登录页」</b>：主动登出是用户自己的动作，不必解释；
    /// 被服务端判死要说一句「登录已失效」，否则用户只看到自己被踢了出来。
    /// 下一次成功采纳会话时复位。
    /// </remarks>
    bool LastSessionEndWasServerInitiated { get; }

    /// <summary>
    /// 记住的账号，按上次使用时间倒序。未接清单时为<b>空列表</b>（不是 <c>null</c>）。
    /// </summary>
    /// <remarks>
    /// 「切换账号」列表就用它。里面的凭据是**可用的 token**，只走内存，不再往界面外扩散。
    /// </remarks>
    IReadOnlyList<RememberedAccount> RememberedAccounts { get; }

    /// <summary>
    /// 这个 uid 的凭据是不是被服务端判死过（本次运行期间观察到 <c>11012</c>）。
    /// </summary>
    /// <remarks>
    /// <b>只在内存里记，不落盘。</b> 失效是服务端当下的态度，本机没资格把它记成永久结论 ——
    /// 下次登录成功（同一个 uid）就会清掉。界面据此在列表里把该账号标成「登录已失效」，
    /// 免得用户反复切过去又被踢出来。
    /// </remarks>
    bool IsStale(string uid);

    /// <summary>
    /// 切到已记住的某个账号。<b>不重新认证</b>，直接用清单里那份凭据。
    /// </summary>
    /// <remarks>
    /// <b>不事先验证 token 还有效</b>：多一次往返与等待，而它挡掉的场景本来就少见。
    /// 真失效了，第一个业务请求会拿到 <c>11012</c>，走既有的会话清理路径并给出提示。
    /// </remarks>
    /// <returns><paramref name="uid"/> 不在清单里（或清单不可用）时返回 <c>false</c>，此时会话不变。</returns>
    bool SwitchTo(string uid);

    /// <summary>
    /// 从清单里忘掉某个账号。<b>只忘登录凭据，不动该账号的本地数据</b>
    /// （<c>accounts\&lt;uid&gt;\</c> 里的队列与历史照旧）。
    /// </summary>
    void Forget(string uid);

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

    /// <summary>
    /// 登出：清当前会话与 <c>session.dat</c>。
    /// </summary>
    /// <remarks>
    /// <b>不动「记住的账号」清单</b> —— 否则「切换账号」就退化成每次都要重新扫码。
    /// 直接后果要说清楚：<b>登出不再等于「把凭据从这台机器上删掉」</b>，想清干净得逐条
    /// <see cref="Forget"/>。
    /// </remarks>
    void SignOut();
}
