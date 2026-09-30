namespace Bodian.Core.Models;

/// <summary>
/// 一个可播放的音源地址，附带服务端**实际**给出的档位信息。
/// </summary>
/// <remarks>
/// <para>
/// 地址里带签名参数，属凭据：**绝不入日志**（<c>LogRedactor</c> 会处理，但调用点也不要主动打）。
/// </para>
/// <para>
/// 这里不带时长。时长以播放引擎读到的为准 —— 试听片段的流只有几十秒，而 API 响应里的
/// <c>duration</c> 是**整曲**时长，两者不是一回事。
/// </para>
/// </remarks>
public sealed record AudioSource
{
    /// <summary>播放地址。优先 https。</summary>
    public required Uri Url { get; init; }

    /// <summary>请求时用的档位。试听片段不是按档位选的，为 <c>null</c>。</summary>
    public AudioQuality? RequestedQuality { get; init; }

    /// <summary>服务端实际给出的格式（<c>flac</c> / <c>mp3</c> / <c>aac</c> …）。</summary>
    public required string Format { get; init; }

    /// <summary>服务端实际给出的码率（kbps）。</summary>
    public int BitrateKbps { get; init; }

    /// <summary>
    /// 服务端是否没给到请求的档位。
    /// </summary>
    /// <remarks>
    /// 服务端会在业务码 200 的前提下**静默降级**（实测：请求无损拿到 320k mp3）。
    /// 为 <c>true</c> 时 UI 必须如实说明，不能显示成用户请求的那一档。
    /// </remarks>
    public bool WasDowngraded =>
        RequestedQuality is { } quality
        && !AudioQualityTable.MatchesServed(quality, Format, BitrateKbps);
}
