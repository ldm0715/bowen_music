using System.Text.RegularExpressions;

namespace Bodian.Core.Services;

/// <summary>
/// 中国大陆手机号的规范化与校验。纯函数，无状态。
/// </summary>
/// <remarks>
/// <para>
/// 放在 Core 而不是 ViewModel：它是可直接单测的纯逻辑，和 <see cref="SessionIdentity"/>
/// 同一种东西。界面只用它来开关按钮，不做第二套判断。
/// </para>
/// <para>
/// <b>这是本地输入校验，不是替服务端做参数校验。</b> 服务端仍然会自己判一遍并把
/// 结论放在业务码里 —— 这里只是让明显不合法的输入不必要地打一次网络请求和一次风控计数。
/// </para>
/// </remarks>
public static partial class MobileNumber
{
    /// <summary>11 位，<c>1</c> 开头，第二位 <c>3</c>–<c>9</c>。</summary>
    [GeneratedRegex(@"^1[3-9]\d{9}$")]
    private static partial Regex Pattern();

    /// <summary>
    /// 去掉分隔符与 <c>+86</c> / <c>86</c> 前缀，返回裸 11 位数字；不合法返回 <c>null</c>。
    /// </summary>
    /// <remarks>
    /// 服务端要的就是裸 11 位（`reverse/findings/16-phone-login.md` 实测），
    /// 所以界面上的分隔符必须在这一步抹掉。
    /// </remarks>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var digits = string.Concat(raw.Where(char.IsAsciiDigit));

        // 国际前缀：剥掉后才可能凑够 11 位。只剥一次，不递归。
        if (raw.TrimStart().StartsWith("+86", StringComparison.Ordinal)
            || (digits.Length > 11 && digits.StartsWith("86", StringComparison.Ordinal)))
        {
            digits = digits.StartsWith("86", StringComparison.Ordinal) ? digits[2..] : digits;
        }

        return Pattern().IsMatch(digits) ? digits : null;
    }

    /// <summary>是否是可直接发给服务端的手机号。</summary>
    public static bool IsValid(string? raw) => Normalize(raw) is not null;
}
