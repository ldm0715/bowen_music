namespace Bodian.Core.Services;

/// <summary>
/// 把服务端给的字符串转成可安全使用的 http(s) 地址。
/// </summary>
/// <remarks>
/// 这条规则从 P0 探针搬进来并在多处复用：服务端理论上不会给 <c>javascript:</c>
/// 或带 userinfo 的地址，但真给了也绝不能把它交给播放器或图片加载器。
/// </remarks>
internal static class HttpUrl
{
    /// <summary>解析成功且是 http/https、不带 userinfo 时返回 <see cref="Uri"/>，否则 <c>null</c>。</summary>
    public static Uri? TryParse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var schemeOk = uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;

        return schemeOk && string.IsNullOrEmpty(uri.UserInfo) ? uri : null;
    }
}
