using Bowen.Core.Audio;

namespace Bowen.Core.Models;

/// <summary>
/// 音频效果偏好。
/// </summary>
/// <remarks>
/// <para>
/// <b>预设与环境音是两层。</b> <see cref="PresetId"/> 管银河/蝰蛇那一组 EQ + 空间效果，
/// <see cref="AmbientId"/> 管叠加在音乐上的环境音；两者可以同时选中，也可以只开其中一个。
/// </para>
/// <para>
/// <b>不存「当前页签」。</b> 页签是界面状态，从两个 id 的前缀就能恢复；存下来只会多一份要同步的状态。
/// </para>
/// </remarks>
public sealed record AudioEffectSettings(
    bool Enabled = false,
    string PresetId = "",
    string AmbientId = "",
    double AmbientVolume = 30,
    double[]? EqBands = null)
{
    /// <summary>环境音音量下限。</summary>
    public const double MinimumAmbientVolume = 0;

    /// <summary>环境音音量上限。</summary>
    public const double MaximumAmbientVolume = 100;

    /// <summary>默认环境音音量。</summary>
    public const double DefaultAmbientVolume = 30;

    /// <summary>默认设置。</summary>
    public static AudioEffectSettings Default { get; } = new();

    /// <summary>
    /// 把越界的值收进合法范围。
    /// </summary>
    /// <remarks>
    /// <b>逐项夹，不整份退回默认</b>：坏字段只影响自己。未知的预设/环境音 id 归一化为空，
    /// 这样目录删掉某个预设后，旧设置不会把它重新选中。
    /// </remarks>
    public AudioEffectSettings Normalized() => this with
    {
        PresetId = AudioEffectCatalog.FindPreset(PresetId)?.Id ?? "",
        AmbientId = AudioEffectCatalog.FindAmbient(AmbientId)?.Id ?? "",
        AmbientVolume = double.IsFinite(AmbientVolume)
            ? Math.Clamp(AmbientVolume, MinimumAmbientVolume, MaximumAmbientVolume)
            : DefaultAmbientVolume,
        EqBands = NormalizeBands(EqBands),
    };

    private static double[] NormalizeBands(double[]? bands)
    {
        var result = new double[AudioEffectCatalog.EqBandCount];
        if (bands is null)
        {
            return result;
        }

        for (var index = 0; index < result.Length && index < bands.Length; index++)
        {
            var value = bands[index];
            result[index] = double.IsFinite(value) ? Math.Clamp(value, -12, 12) : 0;
        }

        return result;
    }
}
