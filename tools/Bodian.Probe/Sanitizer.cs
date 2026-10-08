using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bodian.Probe;

/// <summary>响应脱敏。原始响应里带账号标识与签名地址，落盘和打印前都必须过一遍。</summary>
internal static class Sanitizer
{
    public const string Placeholder = "<redacted>";

    /// <summary>
    /// query 形态的敏感参数。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>sign</c> 也收进来——非空值的 sign 同样是凭据。
    /// </para>
    /// <para>
    /// <b>手机号与验证码也在里面。</b> 发码那条把手机号放在 query 上
    /// （<c>?type=2&amp;mobile=…</c>），是这条表今天唯一真正会用上的项。
    /// </para>
    /// </remarks>
    private static readonly Regex QueryCredentialPattern = new(
        @"\b(token|uid|freeSign|devid|devId|qimei36|sign|mobile|mobilePhone|phone|verifyCode|smsCode|encvMobile|encvVerifyCode)=([^&\s""]*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>响应里的中文默认会被转义成 \uXXXX，读起来太费劲，这里放开。</summary>
    public static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>打印用：只抹掉凭据类字段，保留其余可读性。</summary>
    private static readonly HashSet<string> DisplayKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "token", "freeSign",
    };

    /// <summary>落盘用：额外抹掉设备、账号标识与 PII。</summary>
    private static readonly HashSet<string> FixtureKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "token", "freeSign", "uid", "userId", "fromUid", "devid", "devId", "qimei36",
        "nickname", "headImg",
        // PII：手机号与验证码绝不能进 fixture 文件。
        "mobile", "mobilePhone", "phone", "verifyCode", "smsCode", "encvMobile", "encvVerifyCode",
    };

    /// <summary>这些键的值是带签名参数的 CDN 地址，只保留 scheme + host + path。</summary>
    private static readonly HashSet<string> UrlKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "url", "https", "audioUrl", "audioHttpsUrl",
        "car_url", "car_url_https",
    };

    /// <summary>
    /// 这些键的值是**签名在路径里**的视频直链（形如
    /// <c>http://host/&lt;签名段&gt;/&lt;签名段&gt;/le/resource/…/x.mp4</c>）。
    /// </summary>
    /// <remarks>
    /// 光去 query 抹不掉它——<see cref="UrlKeys"/> 那条「留 path 去 query」的处理对这里是无效的，
    /// 凭据正是路径前两段。见 <c>reverse/findings/15-mv.md</c>。
    /// </remarks>
    private static readonly HashSet<string> SignedPathUrlKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "highUrl", "lowUrl", "shortLowUrl",
    };

    public static string ForDisplay(string json) => Mask(json, DisplayKeys);

    /// <summary>
    /// 打印用：URL 或任何嵌着 query 的字符串。
    /// </summary>
    /// <remarks>
    /// <b>发码那条 URL 上就带着手机号</b>（<c>?type=2&amp;mobile=…</c>），
    /// 所以 <c>--verbose</c> 必须过这一层 —— 否则一打日志，手机号就进了终端与滚动历史。
    /// </remarks>
    public static string ForLogQuery(string text) => RedactQueryCredentials(text);

    /// <summary>
    /// 打印用：请求体（JSON）。手机号与验证码都是明文传的，必须抹。
    /// </summary>
    /// <remarks>
    /// <b>按原文替换，不重新格式化。</b> 不能用 <see cref="Mask"/> —— 它会把 JSON 美化一遍，
    /// 而签名覆盖的是**精确字节**，美化后的 body 对不上签名，<c>--signed</c> 的调试就没参照了。
    /// </remarks>
    public static string ForLogBody(string json) => JsonSecretPattern.Replace(json, m =>
        m.Groups[2].Value.Length == 0 ? m.Value : $"{m.Groups[1].Value}{Placeholder}\"");

    /// <summary>
    /// JSON 形态的敏感字段，按<b>原文</b>替换。值部分不含引号，所以空值不匹配（与 query 那条一致）。
    /// </summary>
    /// <remarks>
    /// <c>code</c> 不在此列：信封顶层的 <c>code</c> 是业务码，抹掉会让所有报错失去意义。
    /// </remarks>
    private static readonly Regex JsonSecretPattern = new(
        @"(""(?:token|uid|freeSign|devid|devId|qimei36|sign|nickname|headImg|mobile|mobilePhone|phone|verifyCode|smsCode|encvMobile|encvVerifyCode)""\s*:\s*"")([^""]*)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string ForFixture(string json, params string[] extraMaskedKeys)
    {
        if (extraMaskedKeys.Length == 0)
        {
            return Mask(json, FixtureKeys);
        }

        var keys = new HashSet<string>(FixtureKeys, StringComparer.OrdinalIgnoreCase);
        keys.UnionWith(extraMaskedKeys);
        return Mask(json, keys);
    }

    private static string Mask(string json, HashSet<string> maskedKeys)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return json;
        }

        if (node is null)
        {
            return json;
        }

        MaskInPlace(node, maskedKeys);

        return node.ToJsonString(Pretty);
    }

    /// <summary>就地改值，不返回新节点——JsonNode 不允许一个节点挂到两个父节点上。</summary>
    private static void MaskInPlace(JsonNode node, HashSet<string> maskedKeys)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (maskedKeys.Contains(key))
                    {
                        obj[key] = Placeholder;
                        continue;
                    }

                    var child = obj[key];
                    if (child is null)
                    {
                        continue;
                    }

                    if (child is JsonValue && SignedPathUrlKeys.Contains(key) && child.GetValueKind() == JsonValueKind.String)
                    {
                        obj[key] = StripSignedPath(child.GetValue<string>());
                        continue;
                    }

                    if (child is JsonValue && UrlKeys.Contains(key) && child.GetValueKind() == JsonValueKind.String)
                    {
                        obj[key] = StripQuery(child.GetValue<string>());
                        continue;
                    }

                    // 字符串里可能嵌着 query（比如 signtest 的 seedQuery），
                    // 按键名抹值抹不到，要再扫一遍内容。
                    if (child is JsonValue && child.GetValueKind() == JsonValueKind.String)
                    {
                        obj[key] = RedactQueryCredentials(child.GetValue<string>());
                        continue;
                    }

                    MaskInPlace(child, maskedKeys);
                }

                break;

            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is { } item)
                    {
                        MaskInPlace(item, maskedKeys);
                    }
                }

                break;
        }
    }

    /// <summary>
    /// 抹掉字符串里以 query 形态出现的凭据参数。
    /// 空值不动——seed 里的 <c>sign=</c> 是签名用例的一部分，要留着。
    /// </summary>
    private static string RedactQueryCredentials(string value)
    {
        if (!value.Contains('='))
        {
            return value;
        }

        return QueryCredentialPattern.Replace(value, m =>
            m.Groups[2].Value.Length == 0 ? m.Value : $"{m.Groups[1].Value}={Placeholder}");
    }

    /// <summary>
    /// 把签名在路径里的视频直链压成 scheme + host + 文件名。
    /// </summary>
    /// <remarks>
    /// 文件名是资源 id（与封面 URL 里的 <c>2430535878.webp</c> 同级），不是凭据，可以留；
    /// 签名段与 query 一并丢掉。
    /// </remarks>
    private static string StripSignedPath(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
           && uri.Segments.Length > 0
            ? $"{uri.Scheme}://{uri.Authority}/{uri.Segments[^1]}"
            : value;

    private static string StripQuery(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? $"{uri.Scheme}://{uri.Authority}{uri.AbsolutePath}?{Placeholder}"
            : value;
}
