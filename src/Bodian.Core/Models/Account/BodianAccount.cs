namespace Bodian.Core.Models.Account;

/// <summary>
/// 当前登录的账号。
/// </summary>
/// <remarks>
/// <para>
/// <b>没有 token。</b> 凭据只存在于 <c>BodianSession</c>（内存）与
/// <c>BodianCredential</c>（DPAPI 落盘）里，不往界面层传。
/// </para>
/// <para>
/// <see cref="IsVip"/> 与 <see cref="VipExpiresAt"/> 是**登录那一刻的快照**：
/// 会员到期后不会自动变。它们只用于界面展示，
/// <b>任何播放权限判断都必须走服务端的 <c>checkRight</c></b>。
/// </para>
/// <para>
/// 从磁盘恢复会话时，老凭据文件里没有头像与会员字段，那两项会是
/// <c>null</c> / <c>false</c> —— 界面必须容忍（显示昵称即可，徽标等下次登录）。
/// </para>
/// </remarks>
/// <param name="Uid">账号 uid。</param>
/// <param name="Nickname">昵称。</param>
/// <param name="Avatar">头像地址。</param>
/// <param name="IsVip">登录时是否会员。</param>
/// <param name="VipExpiresAt">会员最晚到期时刻。</param>
/// <param name="VipBadge">
/// 会员档位。<b>后加的字段</b>，老凭据文件里没有它，读出来是 <see cref="VipBadgeKind.None"/> ——
/// 界面回落到文字徽标。
/// </param>
public sealed record BodianAccount(
    string Uid,
    string? Nickname,
    Uri? Avatar,
    bool IsVip,
    DateTimeOffset? VipExpiresAt,
    VipBadgeKind VipBadge = VipBadgeKind.None);
