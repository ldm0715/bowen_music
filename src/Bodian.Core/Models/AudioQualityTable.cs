namespace Bodian.Core.Models;

/// <summary>
/// 档位与本项目请求参数之间的映射，以及「服务端实际给了什么」的判定。
/// </summary>
/// <remarks>
/// <para>
/// 请求音频地址时<b>只传 <c>br</c>，绝不传 <c>format</c></b>。实测：带 <c>format=flac</c>
/// 会让服务端静默降级到 320k mp3，而 <c>code</c> 仍然是 200，不核对返回值根本发现不了。
/// </para>
/// <para>
///<b>必须核对返回值。</b> 响应里的 <c>format</c> / <c>bitrate</c> 是服务端告诉你「实际给了什么」，
/// 不是回显请求参数。降级时如实上报用户，不要静默当作无损。
/// </para>
/// </remarks>
public static class AudioQualityTable
{
    /// <summary>请求该档位时要传的 <c>br</c> 值。</summary>
    public static string RequestBitrate(AudioQuality quality) => quality switch
    {
        AudioQuality.Standard => "128kmp3",
        AudioQuality.High => "320kmp3",
        AudioQuality.Lossless => "2000kflac",
        _ => throw new ArgumentOutOfRangeException(nameof(quality), quality, "未知档位"),
    };

    /// <summary>该档位未被降级时，服务端应当返回的 <c>format</c>。</summary>
    public static string ExpectedFormat(AudioQuality quality) => quality switch
    {
        AudioQuality.Standard or AudioQuality.High => "mp3",
        AudioQuality.Lossless => "flac",
        _ => throw new ArgumentOutOfRangeException(nameof(quality), quality, "未知档位"),
    };

    /// <summary>该档位未被降级时，服务端应当返回的码率（kbps）。</summary>
    public static int ExpectedBitrateKbps(AudioQuality quality) => quality switch
    {
        AudioQuality.Standard => 128,
        AudioQuality.High => 320,
        AudioQuality.Lossless => 2000,
        _ => throw new ArgumentOutOfRangeException(nameof(quality), quality, "未知档位"),
    };

    /// <summary>
    /// 判断服务端实际返回的音源是否就是请求的那一档。
    /// </summary>
    /// <param name="quality">请求时用的档位。</param>
    /// <param name="servedFormat">响应里的 <c>format</c>。</param>
    /// <param name="servedBitrateKbps">响应里的 <c>bitrate</c>（kbps）。</param>
    /// <returns><c>false</c> 表示被降级了，调用方应如实告知用户。</returns>
    public static bool MatchesServed(AudioQuality quality, string? servedFormat, int servedBitrateKbps)
    {
        if (string.IsNullOrEmpty(servedFormat))
        {
            return false;
        }

        if (!string.Equals(servedFormat, ExpectedFormat(quality), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // 服务端偶尔不给 bitrate，这时只按格式判定，避免把有效音源误判成降级。
        return servedBitrateKbps <= 0 || servedBitrateKbps >= ExpectedBitrateKbps(quality);
    }

    /// <summary>
    /// 把曲目声明的 <c>audios[].level</c> 映射成本项目的档位。
    /// </summary>
    /// <remarks>
    /// 不认识的 level（<c>zp</c> / <c>bcms</c> / <c>ac4</c> / <c>hr</c> 等）返回 <c>false</c>，
    /// 由调用方丢弃 —— 本项目播不了的档位不该出现在候选里。
    /// </remarks>
    public static bool TryParseLevel(string? level, out AudioQuality quality)
    {
        switch (level)
        {
            case "s":
                quality = AudioQuality.Standard;
                return true;

            // h 与 p 是同一档：两者请求的都是 320kmp3。
            case "h":
            case "p":
                quality = AudioQuality.High;
                return true;

            case "ff":
                quality = AudioQuality.Lossless;
                return true;

            default:
                quality = default;
                return false;
        }
    }

    /// <summary>
    /// 从曲目声明的档位算出请求链：**由高到低、已去重**。
    /// </summary>
    /// <remarks>
    /// 去重不是可选优化：实测同一首曲的 <c>audios[]</c> 有 13 条，其中 <c>p</c> 出现 3 次、
    /// <c>h</c> 出现 2 次，不去重就会对同一档重复请求。
    /// <para>返回空列表表示这首歌没有本项目可播的档位，只能试听或不可播。</para>
    /// </remarks>
    public static IReadOnlyList<AudioQuality> BuildRequestChain(IEnumerable<string?> trackLevels)
    {
        ArgumentNullException.ThrowIfNull(trackLevels);

        var found = new SortedSet<AudioQuality>();

        foreach (var level in trackLevels)
        {
            if (TryParseLevel(level, out var quality))
            {
                found.Add(quality);
            }
        }

        return [.. found.Reverse()];
    }
}
