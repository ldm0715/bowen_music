namespace Bodian.Core.Diagnostics;

/// <summary>
/// 可安全入日志的 URL：只保留 <c>scheme://host/path</c>，**query 一律丢弃**。
/// </summary>
/// <remarks>
/// <para>
/// 这个类型存在的意义是让「不泄露」变成不容易写错的事：<see cref="ToString"/> 就是安全的，
/// 不需要每个日志语句自己记得裁剪 query。而波点的 query 里恰好装着
/// <c>token</c> / <c>freeSign</c> / <c>sign</c>——以及音源直链的整段防盗链签名。
/// </para>
/// <para>
/// 这是**第二道防线**。第一道是 <c>BodianHttpTransport</c> 根本不构造含 query 的字符串。
/// </para>
/// </remarks>
public readonly struct SafeUrl : IEquatable<SafeUrl>
{
    public string Scheme { get; }

    public string Host { get; }

    public string Path { get; }

    private SafeUrl(string scheme, string host, string path)
    {
        Scheme = scheme;
        Host = host;
        Path = path;
    }

    /// <summary>丢弃 query 与 fragment，只留定位信息。</summary>
    public static SafeUrl From(Uri uri) => new(uri.Scheme, uri.Host, uri.AbsolutePath);

    /// <summary>
    /// 无法解析成绝对 URI 时返回 <c>?://unparsable</c>，而不是原始文本——
    /// 原始文本恰恰可能就是泄露源。
    /// </summary>
    public static SafeUrl From(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? From(uri) : new SafeUrl("?", "unparsable", string.Empty);

    public override string ToString() => $"{Scheme}://{Host}{Path}";

    public bool Equals(SafeUrl other)
        => string.Equals(Scheme, other.Scheme, StringComparison.Ordinal)
           && string.Equals(Host, other.Host, StringComparison.Ordinal)
           && string.Equals(Path, other.Path, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is SafeUrl other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Scheme, Host, Path);

    public static bool operator ==(SafeUrl left, SafeUrl right) => left.Equals(right);

    public static bool operator !=(SafeUrl left, SafeUrl right) => !left.Equals(right);
}
