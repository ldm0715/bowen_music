using System.Text.Json.Serialization;
using Bowen.Core.Services;

namespace Bowen.Core.Models;

/// <summary>更新通道走哪条路。</summary>
[JsonConverter(typeof(TolerantEnumConverter<ProxyMode>))]
public enum ProxyMode
{
    /// <summary>
    /// 不使用代理。**默认值** —— 应用开箱即与系统代理隔离，行为是确定的。
    /// </summary>
    Direct,

    /// <summary>跟随系统代理（即 2026-10-14 之前那条路）。</summary>
    System,

    /// <summary>用 <see cref="ProxySettings.Address"/> 里那个地址。</summary>
    Custom,
}

/// <summary>
/// 更新通道的代理偏好（明文 JSON）。
/// </summary>
/// <remarks>
/// <para>
/// <b>只管更新检查与安装包下载。</b> 常规请求（登录、歌单、封面）**固定直连**，没有开关 ——
/// 它们全在国内，直连永远是对的；给一个「业务也走代理」的开关，唯一的用途是把国内流量绕出去。
/// 见 <c>docs/proxy.md</c> §3。
/// </para>
/// <para>
/// <b><see cref="Address"/> 里可能带密码</b>（<c>http://user:pass@host:port</c>），
/// 而这份偏好是明文存的。用户自己填的本地代理占绝大多数，从简；
/// 真要收企业代理的账号密码时，这个文件得改成走 DPAPI。
/// </para>
/// </remarks>
public sealed record ProxySettings
{
    /// <summary>代理模式。</summary>
    public ProxyMode Mode { get; set; } = ProxyMode.Direct;

    /// <summary>自定义代理地址；仅 <see cref="ProxyMode.Custom"/> 时生效。</summary>
    public string Address { get; set; } = "";

    /// <summary>默认：不使用代理。</summary>
    public static ProxySettings Default { get; } = new();

    /// <summary>保留旧的构造签名，供现有调用点使用。</summary>
    public ProxySettings()
    {
    }

    /// <summary>保留旧的构造签名，供现有调用点使用。</summary>
    public ProxySettings(ProxyMode Mode = ProxyMode.Direct, string Address = "")
    {
        this.Mode = Mode;
        this.Address = Address;
    }

    /// <summary>
    /// 把认不出来的模式换回直连，其余字段原样留着。
    /// </summary>
    /// <remarks>
    /// 模式存的是字符串（<c>"Custom"</c>），但 <c>JsonStringEnumConverter</c> **默认也接受数字** ——
    /// 手改过文件、或以后删掉某个枚举项时，会读出一个未定义的值。那种值落不进任何一条分支
    /// （<see cref="UpdateProxy"/> 的 <c>switch</c> 会走到兜底），这里提前收口，
    /// 免得它一路带到界面上（下拉框会一个都不选）。
    /// 地址刻意留着，用户重新选一次「自定义」就能接着用。
    /// </remarks>
    public ProxySettings WithKnownMode() =>
        Mode is ProxyMode.Direct or ProxyMode.System or ProxyMode.Custom
            ? this
            : this with { Mode = ProxyMode.Direct };

    /// <summary>认的协议。socks4 / socks5 是 .NET 自己的 <c>WebProxy</c> 支持的，不是我们转的。</summary>
    private static readonly string[] SupportedSchemes = ["http", "https", "socks4", "socks5"];

    /// <summary>
    /// 校验自定义地址。
    /// </summary>
    /// <remarks>
    /// <b>必须写明端口。</b> 没有端口的代理地址是无意义的（<c>WebProxy</c> 会退回该协议的默认端口，
    /// 那几乎不可能是用户想要的），而漏写端口是最常见的一种写错，早点说清楚比之后连接超时好。
    /// </remarks>
    /// <param name="uri">解析结果。返回假时是 <c>null</c>。</param>
    /// <param name="error">给用户看的一句话。返回真时是空串。</param>
    public bool TryGetCustomUri(out Uri? uri, out string error)
    {
        uri = null;
        error = "";

        var text = Address.Trim();

        if (text.Length == 0)
        {
            error = "还没有填代理地址。";
            return false;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var parsed))
        {
            error = "地址写得不对，例：http://127.0.0.1:7890";
            return false;
        }

        if (!SupportedSchemes.Contains(parsed.Scheme, StringComparer.Ordinal))
        {
            error = $"不支持的协议 {parsed.Scheme}，只认 http / https / socks4 / socks5";
            return false;
        }

        if (parsed.Host.Length == 0)
        {
            error = "地址里没有主机名。";
            return false;
        }

        if (!HasExplicitPort(text))
        {
            error = "地址里要带端口，例：http://127.0.0.1:7890";
            return false;
        }

        uri = parsed;
        return true;
    }

    /// <summary>
    /// 原串里到底写没写端口。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>不能问 <see cref="Uri"/>，只能自己看原串。</b> 它对已知协议会把**默认端口规范化掉**：
    /// <c>http://127.0.0.1/</c> 的 <c>Port</c> 是 80、<c>IsDefaultPort</c> 为真，
    /// 而 <c>http://127.0.0.1:80/</c> 同样如此、<c>Authority</c> 也同样是 <c>127.0.0.1</c> ——
    /// 「没写端口」和「写了 :80」在它眼里一模一样。而这两者该不该接受是不一样的。
    /// </para>
    /// <para>
    /// 所以自己切：剥掉 <c>scheme://</c>，取到第一个 <c>/ ? #</c> 为止的 authority，
    /// 再剥掉 userinfo，看剩下那段有没有冒号。
    /// </para>
    /// </remarks>
    private static bool HasExplicitPort(string text)
    {
        var schemeEnd = text.IndexOf("://", StringComparison.Ordinal);
        var rest = schemeEnd >= 0 ? text[(schemeEnd + 3)..] : text;

        var end = rest.IndexOfAny(['/', '?', '#']);
        var authority = end >= 0 ? rest[..end] : rest;

        var at = authority.LastIndexOf('@');
        if (at >= 0)
        {
            authority = authority[(at + 1)..];
        }

        // IPv6 主机是 [::1]:7890 —— 方括号里本来就有冒号，所以要从 ] 之后找。
        if (authority.StartsWith('['))
        {
            var close = authority.IndexOf(']');
            return close >= 0 && authority[(close + 1)..].StartsWith(':');
        }

        return authority.Contains(':');
    }
}
