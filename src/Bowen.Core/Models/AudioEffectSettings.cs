using Bowen.Core.Audio;

namespace Bowen.Core.Models;

/// <summary>
/// 音频效果偏好。
/// </summary>
/// <remarks>
/// <para>
/// <b>预设与环境音是两层。</b> <see cref="PresetId"/> 管经典/增强那一组 EQ + 空间效果，
/// <see cref="AmbientId"/> 管叠加在音乐上的环境音；两者可以同时选中，也可以只开其中一个。
/// </para>
/// <para>
/// <b>不存「当前页签」。</b> 页签是界面状态，从两个 id 的前缀就能恢复；存下来只会多一份要同步的状态。
/// </para>
/// </remarks>
public sealed record AudioEffectSettings
{
    /// <summary>环境音音量下限。</summary>
    public const double MinimumAmbientVolume = 0;

    /// <summary>环境音音量上限。</summary>
    public const double MaximumAmbientVolume = 100;

    /// <summary>默认环境音音量。</summary>
    public const double DefaultAmbientVolume = 30;

    /// <summary>自定义预设名称的最大长度。</summary>
    public const int MaxCustomPresetNameLength = 24;

    /// <summary>自定义预设说明文字的最大长度。</summary>
    public const int MaxCustomPresetDescriptionLength = 32;

    /// <summary>音效是否启用。</summary>
    public bool Enabled { get; set; }

    /// <summary>当前选中的音效预设 id；空串表示不选。内置预设与自定义预设共用这个字段。</summary>
    public string PresetId { get; set; } = "";

    /// <summary>当前选中的环境音 id；空串表示不选。</summary>
    public string AmbientId { get; set; } = "";

    /// <summary>环境音音量。</summary>
    public double AmbientVolume { get; set; } = DefaultAmbientVolume;

    /// <summary>用户保存的自定义均衡预设。</summary>
    public List<CustomEqPreset> CustomPresets { get; set; } = [];

    /// <summary>默认设置。</summary>
    public static AudioEffectSettings Default { get; } = new();

    /// <summary>保留旧的构造签名，供现有调用点使用。</summary>
    public AudioEffectSettings()
    {
    }

    /// <summary>保留旧的构造签名，供现有调用点使用。</summary>
    public AudioEffectSettings(
        bool Enabled = false,
        string PresetId = "",
        string AmbientId = "",
        double AmbientVolume = DefaultAmbientVolume,
        List<CustomEqPreset>? CustomPresets = null)
    {
        this.Enabled = Enabled;
        this.PresetId = PresetId;
        this.AmbientId = AmbientId;
        this.AmbientVolume = AmbientVolume;
        this.CustomPresets = CustomPresets ?? [];
    }

    /// <summary>
    /// 把越界的值收进合法范围。
    /// </summary>
    /// <remarks>
    /// <b>逐项夹，不整份退回默认</b>：坏字段只影响自己。未知的预设/环境音 id 归一化为空，
    /// 这样目录删掉某个预设后，旧设置不会把它重新选中。
    /// </remarks>
    public AudioEffectSettings Normalized()
    {
        var customPresets = NormalizeCustomPresets(CustomPresets);
        return this with
        {
            PresetId = ResolvePresetId(PresetId, customPresets),
            AmbientId = AudioEffectCatalog.FindAmbient(AmbientId)?.Id ?? "",
            AmbientVolume = double.IsFinite(AmbientVolume)
                ? Math.Clamp(AmbientVolume, MinimumAmbientVolume, MaximumAmbientVolume)
                : DefaultAmbientVolume,
            CustomPresets = customPresets,
        };
    }

    private static string ResolvePresetId(string? id, IReadOnlyList<CustomEqPreset> customPresets)
    {
        var builtIn = AudioEffectCatalog.FindPreset(id);
        if (builtIn is not null)
        {
            return builtIn.Id;
        }

        return AudioEffectCatalog.FindCustomPreset(customPresets, id)?.Id ?? "";
    }

    private static List<CustomEqPreset> NormalizeCustomPresets(List<CustomEqPreset>? presets)
    {
        var result = new List<CustomEqPreset>();
        if (presets is null)
        {
            return result;
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var preset in presets)
        {
            if (result.Count >= AudioEffectCatalog.MaxCustomPresets)
            {
                break;
            }

            if (preset is null)
            {
                continue;
            }

            var id = preset.Id?.Trim() ?? "";
            if (!id.StartsWith(AudioEffectCatalog.CustomPresetIdPrefix, StringComparison.OrdinalIgnoreCase)
                || !ids.Add(id))
            {
                continue;
            }

            var name = (preset.Name ?? "").Trim();
            if (name.Length == 0)
            {
                name = "未命名";
            }

            if (name.Length > MaxCustomPresetNameLength)
            {
                name = name[..MaxCustomPresetNameLength];
            }

            var description = (preset.Description ?? "").Trim();
            if (description.Length == 0)
            {
                description = CustomEqPreset.DefaultDescription;
            }

            if (description.Length > MaxCustomPresetDescriptionLength)
            {
                description = description[..MaxCustomPresetDescriptionLength];
            }

            result.Add(new CustomEqPreset
            {
                Id = id,
                Name = name,
                Description = description,
                Bands = NormalizeBands(preset.Bands),
            });
        }

        return result;
    }

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

/// <summary>
/// 用户保存的一套十段均衡预设。
/// </summary>
/// <remarks>
/// <c>Id</c> 必须带 <see cref="AudioEffectCatalog.CustomPresetIdPrefix"/> 前缀，
/// 归一化时据此把它与内置预设区分开。段数固定为 <see cref="AudioEffectCatalog.EqBandCount"/>，
/// 读写都经过 <see cref="AudioEffectSettings.Normalized"/> 收口。
/// </remarks>
public sealed record CustomEqPreset
{
    /// <summary>说明文字的默认值；旧配置没有这个字段时也用它。</summary>
    public const string DefaultDescription = "自定义十段均衡曲线";

    /// <summary>持久化用的稳定 id。</summary>
    public string Id { get; set; } = "";

    /// <summary>展示名。</summary>
    public string Name { get; set; } = "";

    /// <summary>卡片上的小字说明。</summary>
    public string Description { get; set; } = DefaultDescription;

    /// <summary>按 <see cref="AudioEffectCatalog.EqBandFrequencies"/> 排列的增益，单位 dB。</summary>
    public double[] Bands { get; set; } = new double[AudioEffectCatalog.EqBandCount];
}
