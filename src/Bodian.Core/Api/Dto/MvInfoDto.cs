using System.Text.Json.Serialization;

namespace Bodian.Core.Api.Dto;

/// <summary>
/// <c>service/mv/info</c> 的 <c>data</c> 子树。数据在 <c>mv</c> 键下。
/// </summary>
internal sealed class MvInfoPayload
{
    [JsonPropertyName("mv")] public MvInfoDto? Mv { get; init; }
}

/// <summary>
/// 一首歌的 MV 信息。
/// </summary>
/// <remarks>
/// 字段与实测取值见 <c>reverse/findings/15-mv.md</c>。两个直链字段是**签名在路径里**的
/// 视频地址，与 <c>audioUrl</c> 那种「凭据在 query」的形态不同，脱敏方式也不一样
/// （见 <c>tools/Bodian.Probe/Sanitizer.cs</c> 的 <c>SignedPathUrlKeys</c>）。
/// </remarks>
internal sealed class MvInfoDto
{
    [JsonPropertyName("mid")] public long Mid { get; init; }

    [JsonPropertyName("coverUrl")] public string? CoverUrl { get; init; }

    /// <summary>高码率直链。**带签名，绝不入日志。**</summary>
    [JsonPropertyName("highUrl")] public string? HighUrl { get; init; }

    /// <summary>低码率直链。**带签名，绝不入日志。**</summary>
    [JsonPropertyName("lowUrl")] public string? LowUrl { get; init; }

    /// <summary>短视频直链，走另一个 CDN。**同样带凭据。**</summary>
    [JsonPropertyName("shortLowUrl")] public string? ShortLowUrl { get; init; }

    /// <summary>MV 时长，单位**秒**。与曲目详情里的 <c>mvduration</c> 同值。</summary>
    [JsonPropertyName("mvDuration")] public int MvDurationSeconds { get; init; }

    /// <summary>试看秒数。<c>0</c> = 不限。</summary>
    [JsonPropertyName("playLimitTime")] public int PlayLimitSeconds { get; init; }

    /// <summary>后台/熄屏播放限制秒数。<c>0</c> = 不限。</summary>
    [JsonPropertyName("bgPlayLimitTime")] public int BgPlayLimitSeconds { get; init; }

    /// <summary>版权提示文案，服务端直接给的是给人看的一句话。</summary>
    [JsonPropertyName("playCopyrightTips")] public string? PlayCopyrightTips { get; init; }

    [JsonPropertyName("highBitrate")] public int HighBitrate { get; init; }

    [JsonPropertyName("lowBitrate")] public int LowBitrate { get; init; }
}
