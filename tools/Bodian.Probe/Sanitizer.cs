using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bodian.Probe;

/// <summary>响应脱敏。原始响应里带账号标识与签名地址，落盘和打印前都必须过一遍。</summary>
internal static class Sanitizer
{
    public const string Placeholder = "<redacted>";

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

    /// <summary>落盘用：额外抹掉设备与账号标识。</summary>
    private static readonly HashSet<string> FixtureKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "token", "freeSign", "uid", "userId", "fromUid", "devid", "devId", "qimei36",
        "nickname", "headImg",
    };

    /// <summary>这些键的值是带签名参数的 CDN 地址，只保留 scheme + host + path。</summary>
    private static readonly HashSet<string> UrlKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "url", "https", "audioUrl", "audioHttpsUrl",
        "car_url", "car_url_https",
    };

    public static string ForDisplay(string json) => Mask(json, DisplayKeys);

    public static string ForFixture(string json) => Mask(json, FixtureKeys);

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

                    if (child is JsonValue && UrlKeys.Contains(key) && child.GetValueKind() == JsonValueKind.String)
                    {
                        obj[key] = StripQuery(child.GetValue<string>());
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

    private static string StripQuery(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? $"{uri.Scheme}://{uri.Authority}{uri.AbsolutePath}?{Placeholder}"
            : value;
}
