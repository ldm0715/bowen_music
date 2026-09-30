using System.Security.Cryptography;
using System.Text;

namespace Bodian.Probe;

/// <summary>
/// 波点请求签名。
/// </summary>
/// <remarks>
/// <code>
/// query = form-urlencode(参数)
/// seed  = "kuwotest" + query 中所有 [a-zA-Z0-9] 字符按升序拼接
/// if (body) seed += md5(body + "kuwotest")
/// sign  = md5(seed + path)
/// </code>
/// 进入 seed 的只有字母数字，因此 query 的参数顺序不影响结果；
/// 但编码方式影响——非字母数字的字符（含百分号与十六进制位）会改变 seed 的字符集。
/// 所以这里的编码必须逐字节对齐 WHATWG 的 application/x-www-form-urlencoded。
/// </remarks>
internal static class BodianSigner
{
    private const string Salt = "kuwotest";

    /// <summary>计算签名。body 必须是即将发送的精确 UTF-8 JSON 串，无 body 传 null。</summary>
    public static string Sign(string path, IEnumerable<KeyValuePair<string, string>> query, string? body)
        => SignRaw(path, FormUrlEncode(query), body);

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

    /// <summary>WHATWG application/x-www-form-urlencoded 序列化。</summary>
    public static string FormUrlEncode(IEnumerable<KeyValuePair<string, string>> pairs)
        => string.Join('&', pairs.Select(p => $"{EncodeComponent(p.Key)}={EncodeComponent(p.Value)}"));

    /// <summary>
    /// URLSearchParams 的编码规则：字母数字与 <c>* - . _</c> 保留，空格转 <c>+</c>，其余按 UTF-8 百分号编码（大写十六进制）。
    /// 注意 <c>~</c> 会被编码，这一点与 <see cref="Uri.EscapeDataString"/> 不同。
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

    public static string Md5Hex(string text)
        => Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(text)));
}
