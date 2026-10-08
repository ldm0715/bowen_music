namespace Bodian.Core.Models.Login;

/// <summary>
/// 发送短信验证码的结果。
/// </summary>
/// <remarks>
/// <b>刻意不复用 <see cref="LoginOutcome"/>。</b> 那个类型的 <c>Success(Uid, Nickname)</c>
/// 语义是「会话已写入并落盘」，而发验证码根本不碰会话 —— 借用它会让调用方以为已经登录了。
/// </remarks>
public abstract record SmsSendOutcome
{
    /// <summary>
    /// 服务端接下了这个号码。
    /// </summary>
    /// <remarks>
    /// <b>这不代表短信真的送达。</b> 服务端只表示「请求合法、已投递」，
    /// 号码是否能收、是否被运营商拦截都在它之后发生。
    /// </remarks>
    public sealed record Sent : SmsSendOutcome;

    /// <summary>
    /// 服务端返回了非 <c>200</c> 的业务码。**原样上报，不猜测语义。**
    /// </summary>
    /// <param name="Code">业务码。常见的有 <c>11003</c>（短信发送失败）。</param>
    /// <param name="Message">服务端消息。</param>
    public sealed record Failed(int Code, string? Message) : SmsSendOutcome;
}
