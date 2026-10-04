using Bodian.Core.Models.Account;

namespace Bodian.Core.Services.Abstractions;

/// <summary>
/// 落盘的会话凭据。
/// </summary>
/// <remarks>
/// <b>属性名是对外契约的一部分。</b> 它决定了 <c>session.dat</c> 里的 JSON 字段名，
/// 必须与 P0 探针的 <c>SessionStore</c> 写下的形态一致，否则老会话会读不出来。
/// 改动这几个名字等于改凭据文件格式。
/// <para><see cref="Token"/> 是凭据字段，绝不入日志。</para>
/// </remarks>
/// <param name="Uid">账号 uid。</param>
/// <param name="Token">会话 token。</param>
/// <param name="Nickname">昵称。</param>
/// <param name="AvatarUrl">
/// 头像地址。**后加的字段**，老凭据文件里没有它，读出来是 <c>null</c> —— 界面要容忍没有头像。
/// </param>
/// <param name="IsVip">
/// 登录那一刻的会员状态。**这是一份快照**：会员到期后它不会自己变。
/// 真正的播放权限永远以服务端返回的 <c>checkRight</c> 为准，这里只用于界面展示。
/// </param>
/// <param name="VipExpiresAt">会员到期时刻；服务端没给或为 0 时为 <c>null</c>。</param>
/// <param name="VipBadge">
/// 登录那一刻的会员档位。**后加的字段**，老凭据文件里没有它，读出来是
/// <see cref="VipBadgeKind.None"/> —— 界面回落到文字徽标。与 <see cref="IsVip"/> 一样只是快照。
/// </param>
public sealed record BodianCredential(
    string Uid,
    string Token,
    string? Nickname,
    string? AvatarUrl = null,
    bool IsVip = false,
    DateTimeOffset? VipExpiresAt = null,
    VipBadgeKind VipBadge = VipBadgeKind.None)
{
    public bool IsAuthenticated => Uid.Length > 0 && Uid != "-1";
}

/// <summary>
/// 凭据存取。实现负责加密落盘。
/// </summary>
/// <remarks>
/// 抽这一层的目的**不是**为了让 <c>Bodian.Core</c> 能编译（DPAPI 在纯 <c>net10.0</c> 下本来就能用，
/// 见 <c>docs/transport.md</c> 2.1 节），而是：
/// <list type="number">
/// <item><b>可测性</b>：绝大多数服务层测试不该碰真实磁盘和 DPAPI（慢，且会污染
/// <c>%LOCALAPPDATA%\Bodian</c>），用 <c>InMemoryCredentialStore</c> 替换。</item>
/// <item><b>可替换性</b>：若日后改成 MSIX 打包，<c>PasswordVault</c> 或 <c>ApplicationData</c>
/// 可以换进来而不动上层。</item>
/// </list>
/// </remarks>
public interface ICredentialStore
{
    /// <summary>没有会话、或文件损坏/换了 Windows 账号解不开时返回 <c>null</c>。</summary>
    BodianCredential? Load();

    void Save(BodianCredential credential);

    /// <summary>返回是否真的删掉了东西。</summary>
    bool Clear();
}
