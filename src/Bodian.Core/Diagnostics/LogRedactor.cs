using System.Text.RegularExpressions;

namespace Bodian.Core.Diagnostics;

/// <summary>
/// 字符串级脱敏：把凭据与 PII 字段的值换成 <c>&lt;redacted&gt;</c>。
/// </summary>
/// <remarks>
/// <para>
/// <b>空值不动。</b> 两个模式的值部分都要求「至少一个字符」，所以 <c>token=</c>（未登录）
/// 与 <c>sign=</c>（黄金用例里的空签名位）保持原样——脱敏后的 query 串仍能与代码里的常量
/// 逐字比对，不至于让人以为哪里写错了。
/// </para>
/// <para>
/// 覆盖两种形态，因为两者都会进日志：query 串（请求 URL 走的是这个）与
/// JSON（签名覆盖的 body 是 JSON）。
/// </para>
/// <para>
/// <b>除了凭据，手机号与验证码也在表里。</b> 请求体本身不进日志（第一道防线），
/// 所以它们今天不会漏;但只要有人加一行打印 body 的日志，或者某个网关卡回显了请求，
/// 那就是明文落到磁盘上。手机号是 PII、验证码是限时的登录凭据，两者都不该留下。
/// </para>
/// <para>
/// <b><c>code</c> 绝不能加进来。</b> 信封顶层的 <c>code</c> 是业务码，
/// 每一行日志和每一条异常消息里都有它；抹掉等于把所有报错变成
/// <c>返回业务码 &lt;redacted&gt;</c>，日志就没用了。短信那边叫 <c>verifyCode</c> / <c>smsCode</c>，
/// 名字不会撞上。
/// </para>
/// <para>
/// 这是**第二道防线**。第一道是 <c>BodianHttpTransport</c> 根本不构造含 query 的字符串。
/// </para>
/// </remarks>
public static partial class LogRedactor
{
    private const string Placeholder = "<redacted>";

    /// <summary>未登录时 uid 的这个值不是凭据，留着不脱敏。</summary>
    private const string AnonymousUid = "-1";

    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        return JsonForm().Replace(QueryForm().Replace(text, QueryReplacer), JsonReplacer);
    }

    /// <summary>
    /// query 形态：<c>key=value</c>，值到 <c>&amp;</c>、空白或引号为止。
    /// </summary>
    /// <remarks>
    /// 值的字符类里不含 <c>&amp;</c>，所以**空值不匹配**，从而实现「空值不动」。
    /// </remarks>
    [GeneratedRegex(
        @"\b(?:token|ekey|freeSign|devid|qimei36|sign|nickname|headImg|uid|mobile|mobilePhone|phone|verifyCode|smsCode)=([^&\s""'<>]+)",
        RegexOptions.IgnoreCase)]
    private static partial Regex QueryForm();

    /// <summary>JSON 形态：<c>"key":"value"</c>。空字符串同样不匹配。</summary>
    [GeneratedRegex(
        @"(""(?:token|ekey|freeSign|devid|qimei36|sign|nickname|headImg|uid|mobile|mobilePhone|phone|verifyCode|smsCode)""\s*:\s*"")([^""]+)""",
        RegexOptions.IgnoreCase)]
    private static partial Regex JsonForm();

    private static readonly MatchEvaluator QueryReplacer = match =>
        match.Groups[1].Value == AnonymousUid
            ? match.Value                       // uid=-1 是匿名标记，不是凭据
            : match.Value[..(match.Value.Length - match.Groups[1].Length)] + Placeholder;

    private static readonly MatchEvaluator JsonReplacer = match =>
        match.Groups[1].Value + Placeholder + "\"";
}
