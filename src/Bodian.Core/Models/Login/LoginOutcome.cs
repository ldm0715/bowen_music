namespace Bodian.Core.Models.Login;

/// <summary>
/// 换取会话的结果。
/// </summary>
/// <remarks>
/// <b>只有 <see cref="Success"/> 会写入会话与凭据存储。</b> 其余分支都必须保证
/// 「现有会话未被改动、凭据未落盘」—— 身份存疑的会话宁可不要。
/// </remarks>
public abstract record LoginOutcome
{
    /// <summary>成功。会话已写入 <c>BodianSession</c>，凭据已加密落盘。</summary>
    /// <param name="Uid">账号 uid。</param>
    /// <param name="Nickname">昵称，可能为空。</param>
    public sealed record Success(string Uid, string? Nickname) : LoginOutcome;

    /// <summary>二维码已过期，需要重新创建。</summary>
    public sealed record Expired : LoginOutcome;

    /// <summary>
    /// 身份校验不通过：那几个 id 字段对不上，或凑不出足够证据。<b>会话已丢弃。</b>
    /// </summary>
    /// <param name="Detail">出现过的身份字段，供诊断。不含 token。</param>
    public sealed record IdentityMismatch(string Detail) : LoginOutcome;

    /// <summary>响应不完整，例如没有 token。<b>会话已丢弃。</b></summary>
    /// <param name="Detail">缺了什么。</param>
    public sealed record InvalidResponse(string Detail) : LoginOutcome;

    /// <summary>服务端返回了非期望的业务码。</summary>
    /// <param name="Code">业务码。</param>
    /// <param name="Message">服务端消息。</param>
    public sealed record Failed(int Code, string? Message) : LoginOutcome;
}
