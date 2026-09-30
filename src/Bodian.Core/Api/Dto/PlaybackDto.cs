using System.Text.Json.Serialization;

namespace Bodian.Core.Api.Dto;

/// <summary>
/// <c>play/music/v2/checkRight</c> 的响应。
/// </summary>
/// <remarks>
/// 请求形态特殊：**GET 但必须带 JSON body，且签名覆盖该 body**，
/// 不能退化成普通 GET 丢掉 body（见 <c>bodian-api-reference.md</c> 1.3 节）。
/// </remarks>
internal sealed class CheckRightDto
{
    /// <summary>
    /// 播放权限。<c>3</c> = 只能试听；<c>7</c> = 无权限；其他（实测 <c>4</c>）= 完整播放。
    /// <para>
    /// **试听不能当完整歌曲**，也不能写入完整曲目的缓存；无权限要提示用户且不要重试。
    /// </para>
    /// </summary>
    [JsonPropertyName("status")] public int Status { get; init; }

    /// <summary>实测 <see cref="Status"/> 不为 3 时**不存在**。</summary>
    [JsonPropertyName("audition")] public AuditionDto? Audition { get; init; }
}

/// <summary>试听片段信息。</summary>
/// <remarks>
/// <b>这里的时间是秒</b>——同一个响应里 <c>payInfo.refrain_*</c> 却是毫秒，别混。
/// </remarks>
internal sealed class AuditionDto
{
    /// <summary>试听总时长，单位**秒**。</summary>
    [JsonPropertyName("duration")] public int DurationSeconds { get; init; }

    /// <summary>试听区间起点，单位**秒**（实测为 0）。</summary>
    [JsonPropertyName("start")] public int StartSeconds { get; init; }

    /// <summary>试听区间终点，单位**秒**（实测为 29）。</summary>
    [JsonPropertyName("end")] public int EndSeconds { get; init; }

    [JsonPropertyName("br")] public int Bitrate { get; init; }

    [JsonPropertyName("format")] public string? Format { get; init; }

    [JsonPropertyName("id")] public long Id { get; init; }

    [JsonPropertyName("url")] public string? Url { get; init; }

    [JsonPropertyName("https")] public string? Https { get; init; }

    [JsonPropertyName("car_url")] public string? CarUrl { get; init; }

    [JsonPropertyName("car_url_https")] public string? CarUrlHttps { get; init; }

    [JsonPropertyName("overseas_copyright")] public string? OverseasCopyright { get; init; }
}

/// <summary>
/// <c>play/music/v2/audioUrl</c> 的响应。请求形态与 <c>checkRight</c> 相同。
/// </summary>
internal sealed class AudioUrlDto
{
    /// <summary>HTTP 直链。**凭据字段（带签名 query），绝不入日志。**</summary>
    [JsonPropertyName("audioUrl")] public string? AudioUrl { get; init; }

    /// <summary>HTTPS 直链。本项目优先用它。</summary>
    [JsonPropertyName("audioHttpsUrl")] public string? AudioHttpsUrl { get; init; }

    /// <summary>
    /// **服务端实际给出的格式**，可能与请求参数不同。
    /// <para>
    /// 实测：请求 <c>format=flac</c> 时服务端会静默降级成 mp3（见
    /// <c>fixtures/audiourl-formatflac-downgraded.json</c>：请求 flac，返回 <c>mp3</c> / <c>320</c>）。
    /// </para>
    /// <para>
    /// **所以必须校验返回值，不能相信请求参数。** 那条 fixture 就是这条回归测试的素材。
    /// </para>
    /// </summary>
    [JsonPropertyName("format")] public string? Format { get; init; }

    /// <summary>这里确实是**数字**（与曲目 <c>audios[].bitrate</c> 是字符串不同）。</summary>
    [JsonPropertyName("bitrate")] public int Bitrate { get; init; }

    /// <summary>时长，单位**秒**。</summary>
    [JsonPropertyName("duration")] public int DurationSeconds { get; init; }

    /// <summary>字符串且带单位，形如 <c>"52.83Mb"</c>。</summary>
    [JsonPropertyName("size")] public string? Size { get; init; }

    [JsonPropertyName("respCode")] public int RespCode { get; init; }

    /// <summary>PC 端的 P2P 音源标识。本项目不走 P2P，**不使用**。</summary>
    [JsonPropertyName("p2pAudioSourceId")] public string? P2pAudioSourceId { get; init; }
}
