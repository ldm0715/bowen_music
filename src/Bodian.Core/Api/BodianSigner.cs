using System.Security.Cryptography;
using System.Text;

namespace Bodian.Core.Api;

/// <summary>
/// 波点请求签名（**桌面端算法**）。
/// </summary>
/// <remarks>
/// <para><b>本项目唯一碰 MD5 的地方。</b></para>
/// <code>
/// query = form-urlencode(参数)
/// seed  = "kuwotest" + query 中所有 [a-zA-Z0-9] 字符按升序拼接
/// if (body) seed += md5(body + "kuwotest")
/// sign  = md5(seed + path)
/// </code>
/// <para>
/// 进入 seed 的只有字母数字，所以 query 的**参数顺序不影响结果**；但**编码方式影响**——
/// 非字母数字的字符（含百分号与十六进制位）会改变 seed 的字符集。因此
/// <see cref="FormUrlEncode"/> 必须逐字节对齐 WHATWG 的
/// <c>application/x-www-form-urlencoded</c>，为了对齐 Dart 的 <c>URLSearchParams</c>。
/// </para>
/// <para>
/// <b>这段算法是 characterization，不是已验证事实。</b>
/// <c>fixtures/sign-golden.json</c> 的 <c>verified</c> 是 <c>false</c>：
/// 校验由 <c>ver</c> 请求头控制，<c>ver ≤ 3.0.0</c>（本项目钉死的 <c>1.1.7</c>）
/// 服务端**完全不校验**签名，<c>ver ≥ 3.5</c> 才强制校验、且走的是**移动端另一套算法**
/// （排序串覆盖整条 URL 的字母数字，见 <c>reverse/findings/03-sign-mobile.md</c>），
/// 探针实测十种变体全被拒。所以这里的实现**没有**在强制校验下被证明过。
/// </para>
/// <para>
/// <b>不要删掉签名。</b> <c>ver</c> 一越过门槛，全部请求会被 <c>439 sign invalid</c> 拒掉，
/// 而错误信息不指向具体哪里错。
/// </para>
/// </remarks>
internal static class BodianSigner
{
    private const string Salt = "kuwotest";

    /// <summary>计算签名。body 必须是即将发送的精确 UTF-8 JSON 串，无 body 传 null。</summary>
    public static string Sign(string path, IEnumerable<KeyValuePair<string, string>> query, string? body)
        => SignRaw(path, FormUrlEncode(query), body);

    /// <summary>与 <see cref="Sign"/> 相同，但直接接受已编码的 query 串。</summary>
    public static string SignRaw(string path, string encodedQuery, string? body)
    {
        var seed = new StringBuilder(encodedQuery.Length + path.Length + 64).Append(Salt);

        foreach (var c in encodedQuery.Where(char.IsAsciiLetterOrDigit).Order())
        {
            seed.Append(c);
        }

        if (!string.IsNullOrEmpty(body))
        {
            seed.Append(Md5Hex(body + Salt));
        }

        return Md5Hex(seed.Append(path).ToString());
    }

    /// <summary>
    /// WHATWG <c>application/x-www-form-urlencoded</c> 序列化。
    /// </summary>
    /// <remarks>
    /// <b>不能换成 <see cref="Uri.EscapeDataString"/></b>：两者对 <c>~</c>、空格与编码字符集的
    /// 处理都不同，而签名只取字母数字，任何差异都会改变 seed。
    /// </remarks>
    public static string FormUrlEncode(IEnumerable<KeyValuePair<string, string>> pairs)
        => string.Join('&', pairs.Select(p => $"{EncodeComponent(p.Key)}={EncodeComponent(p.Value)}"));

    /// <summary>
    /// <c>URLSearchParams</c> 的编码规则：字母数字与 <c>* - . _</c> 保留，空格转 <c>+</c>，
    /// 其余按 UTF-8 百分号编码（大写十六进制）。注意 <c>~</c> 会被编码。
    /// </summary>
    private static string EncodeComponent(string value)
    {
        var sb = new StringBuilder(value.Length * 2);

        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            switch (b)
            {
                case (byte)' ':
                    sb.Append('+');
                    break;
                case >= (byte)'a' and <= (byte)'z':
                case >= (byte)'A' and <= (byte)'Z':
                case >= (byte)'0' and <= (byte)'9':
                case (byte)'*':
                case (byte)'-':
                case (byte)'.':
                case (byte)'_':
                    sb.Append((char)b);
                    break;
                default:
                    sb.Append('%').Append(b.ToString("X2"));
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>小写十六进制，与 Dart 侧一致。</summary>
    public static string Md5Hex(string text)
        => Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(text)));
}
