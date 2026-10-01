using System.Security.Cryptography;
using System.Text;

namespace Bodian.Probe;

/// <summary>
/// 移动端请求签名。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="BodianSigner"/>（桌面版）是<b>两套不同的东西</b>，不是参数差异 ——
/// 起因是「排序串覆盖的范围不同」，见 <c>reverse/findings/03-sign-mobile.md</c>。
/// </para>
/// <code>
/// stripped = uri 去掉所有非 [a-zA-Z0-9] 的字符
/// sorted   = stripped 的字符升序拼接
/// bodyHash = body 非空 ? md5(jsonBody + "kuwotest") : ""
/// seed     = "kuwotest" + sorted + bodyHash + uri
/// sign     = md5(seed)
/// </code>
/// <para>
/// <b>关键差异</b>：
/// </para>
/// <list type="bullet">
/// <item>桌面版只签 <b>query 串</b>，并把裸业务路径单独拼在末尾；
/// 移动端签的是 <b>整条 URL</b>（scheme + host + path + query 全揉进去），
/// 且末尾再放一次整条 URL。</item>
/// <item><b>GET 不签名</b>：客户端的 <c>HttpUtils::encryptParam</c> 只在
/// <c>post</c>/<c>delete</c>/<c>put</c> 且 <c>content-type</c> 是 JSON 时才生效。</item>
/// </list>
/// <para>
/// <b>2026-10-01 从 5.9.8 arm64 重新推导</b>（此前是 5.2.5）：
/// 调用序列里 seed 末尾那一段调的是 <c>RequestOptions::uri</c> <b>getter 本身</b>，
/// 紧接着就是 <c>_interpolate</c> —— 中间没有 <c>.path</c> 的访问，
/// 所以是<b>整条 URL 的 toString()</b>，不是早期猜测的 <c>.path</c>。
/// 另外 <b>5.9.8 里没有 <c>kpk</c> 参数</b>（5.2.5 有），本实现不含它。
/// </para>
/// <para>
/// <b>还没验证过</b>：这套实现是按反编译读出的流程写的，写出来就是为了打一次真实请求来验证。
/// 验证通过之前，别把它当成已证实的协议事实。
/// </para>
/// </remarks>
internal static class BodianMobileSigner
{
    private const string Salt = "kuwotest";

    /// <summary>计算签名。<paramref name="uri"/> 是**不含 sign 参数**的完整请求 URL。</summary>
    /// <param name="uri">完整 URL（含 scheme/host/path/query），但**不要带 sign**。</param>
    /// <param name="jsonBody">即将发送的精确 JSON 串；GET 或无 body 时传 <c>null</c>。</param>
    public static string Sign(string uri, string? jsonBody)
    {
        ArgumentNullException.ThrowIfNull(uri);

        var stripped = new StringBuilder(uri.Length);

        foreach (var c in uri.Where(char.IsAsciiLetterOrDigit))
        {
            stripped.Append(c);
        }

        var sorted = string.Concat(stripped.ToString().Order());

        var seed = new StringBuilder(Salt.Length + sorted.Length + uri.Length + 64)
            .Append(Salt)
            .Append(sorted);

        if (!string.IsNullOrEmpty(jsonBody))
        {
            seed.Append(Md5Hex(jsonBody + Salt));
        }

        return Md5Hex(seed.Append(uri).ToString());
    }

    private static string Md5Hex(string value) =>
        Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(value)));
}
