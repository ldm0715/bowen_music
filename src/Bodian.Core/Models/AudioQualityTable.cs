using System.Globalization;
using System.Text.RegularExpressions;

namespace Bodian.Core.Models;

public static partial class AudioQualityTable
{
    public static string DisplayName(AudioQuality quality) => quality switch
    {
        AudioQuality.Standard => "标准",
        AudioQuality.High => "HQ",
        AudioQuality.Lossless => "SQ",
        _ => throw new ArgumentOutOfRangeException(nameof(quality)),
    };

    // 兼容没有 audios 明细的历史数据。API 映射出的曲目始终使用 AudioVariant.RequestBitrate。
    public static string RequestBitrate(AudioQuality quality) => quality switch
    {
        AudioQuality.Standard => "128kmp3",
        AudioQuality.High => "320kmp3",
        AudioQuality.Lossless => "2000kflac",
        _ => throw new ArgumentOutOfRangeException(nameof(quality)),
    };

    public static string ExpectedFormat(AudioQuality quality) => quality switch
    {
        AudioQuality.Standard or AudioQuality.High => "mp3",
        AudioQuality.Lossless => "flac",
        _ => throw new ArgumentOutOfRangeException(nameof(quality)),
    };

    public static int ExpectedBitrateKbps(AudioQuality quality) => quality switch
    {
        AudioQuality.Standard => 128,
        AudioQuality.High => 320,
        AudioQuality.Lossless => 2000,
        _ => throw new ArgumentOutOfRangeException(nameof(quality)),
    };

    public static bool MatchesServed(AudioQuality quality, string? format, int bitrate) =>
        MatchesServed(ExpectedFormat(quality), ExpectedBitrateKbps(quality), format, bitrate);

    public static bool MatchesServed(string expectedFormat, int expectedBitrate, string? format, int bitrate) =>
        !string.IsNullOrEmpty(format)
        && string.Equals(format, expectedFormat, StringComparison.OrdinalIgnoreCase)
        && (bitrate <= 0 || bitrate >= expectedBitrate);

    public static bool TryParseLevel(string? level, out AudioQuality quality)
    {
        AudioQuality? parsed = level switch
        {
            "s" or "h" => AudioQuality.Standard,
            "p" => AudioQuality.High,
            "ff" => AudioQuality.Lossless,
            _ => null,
        };
        quality = parsed ?? default;
        return parsed.HasValue;
    }

    public static AudioVariant? ParseVariant(string? level, string? format, string? bitrate, string? size)
    {
        if (!TryParseLevel(level, out var quality)
            || string.IsNullOrWhiteSpace(format)
            || string.Equals(level, format, StringComparison.OrdinalIgnoreCase)
            || !SizePattern().IsMatch(size ?? "")
            || !int.TryParse(bitrate, NumberStyles.None, CultureInfo.InvariantCulture, out var kbps)
            || kbps <= 0)
        {
            return null;
        }
        var normalized = format.ToLowerInvariant();
        if (!IsPlayableFormat(normalized)) { return null; }
        return new AudioVariant(quality, level!, normalized, kbps, ParseSize(size));
    }

    public static IReadOnlyList<AudioQuality> BuildRequestChain(IEnumerable<string?> trackLevels)
    {
        ArgumentNullException.ThrowIfNull(trackLevels);
        return trackLevels.Select(level => TryParseLevel(level, out var q) ? (AudioQuality?)q : null)
            .Where(q => q.HasValue).Select(q => q!.Value).Distinct().OrderDescending().ToArray();
    }

    public static bool IsPlayableFormat(string? format) => format?.ToLowerInvariant() is "aac" or "mp3" or "ogg" or "flac";

    /// <summary>同时校验 level、产品档位及明文格式，排除旧历史或外部构造的加密规格。</summary>
    public static bool IsSupportedVariant(AudioVariant variant) =>
        TryParseLevel(variant.Level, out var quality) && quality == variant.Quality
        && IsPlayableFormat(variant.Format) && variant.BitrateKbps > 0;

    public static AudioVariant? SelectVariant(Track track, AudioQuality? preferred = null) =>
        track.AudioVariants.Where(v => IsSupportedVariant(v) && (preferred is null || v.Quality <= preferred))
            .OrderByDescending(v => v.Quality).ThenByDescending(v => v.BitrateKbps).FirstOrDefault();

    public static AudioQuality? ServedQuality(AudioSource source)
    {
        if (IsPlayableFormat(source.Format) && !source.WasDowngraded
            && source.RequestedQuality is { } requested && Enum.IsDefined(requested)) { return requested; }
        return source.Format.ToLowerInvariant() switch
        {
            "aac" => AudioQuality.Standard,
            "mp3" => source.BitrateKbps > 128 ? AudioQuality.High : AudioQuality.Standard,
            "ogg" => source.BitrateKbps > 100 ? AudioQuality.High : AudioQuality.Standard,
            "flac" => AudioQuality.Lossless,
            _ => null,
        };
    }

    public static long ParseSize(string? size)
    {
        var match = SizePattern().Match(size ?? "");
        if (!match.Success || !double.TryParse(match.Groups[1].Value, NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var number)) { return 0; }
        var scale = match.Groups[3].Value.ToLowerInvariant() switch
        {
            "kb" => 1024d, "mb" => 1024d * 1024, "gb" => 1024d * 1024 * 1024, _ => 0,
        };
        var value = number * scale;
        return double.IsFinite(value) && value > 0 && value < long.MaxValue ? (long)Math.Round(value) : 0;
    }

    public static string FormatSize(long size) => size <= 0 ? "大小未知"
        : size >= 1024L * 1024 * 1024 ? $"{size / (1024d * 1024 * 1024):0.##} GB"
        : size >= 1024 * 1024 ? $"{size / (1024d * 1024):0.##} MB"
        : $"{size / 1024d:0.##} KB";

    public static string Describe(AudioSource source, bool isAudition = false)
    {
        if (isAudition) { return "试听片段"; }
        var actual = ServedQuality(source) is { } quality ? DisplayName(quality) : "音质未知";
        var label = source.WasDowngraded && source.RequestedQuality is { } requested
            ? $"{actual}（{DisplayName(requested)}不可用）" : actual;
        return $"{label} · {FormatSize(source.SizeBytes)}";
    }

    [GeneratedRegex(@"^(\d+(\.\d+)?)(Mb|Kb|Gb)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex SizePattern();
}
