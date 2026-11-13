using System.Globalization;
using Bowen.Core.Audio;
using Bowen.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Bowen.WinUI.ViewModels;

/// <summary>音效面板里的一张预设卡。</summary>
public sealed partial class AudioEffectPresetItem(AudioEffectPreset preset) : ObservableObject
{
    public AudioEffectPreset Preset { get; } = preset;

    public string Id => Preset.Id;

    public string Name => Preset.Name;

    public string Description => Preset.Description;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>自定义预设网格里的一张卡片。</summary>
public sealed partial class CustomEqPresetItem : ObservableObject
{
    public CustomEqPresetItem(CustomEqPreset preset)
    {
        Id = preset.Id;
        Name = preset.Name;
        Description = preset.Description;
        Bands = preset.Bands is null ? [] : [.. preset.Bands];
    }

    public string Id { get; }

    [ObservableProperty]
    public partial string Name { get; set; }

    /// <summary>卡片上的小字说明，由用户在编辑器里填写。</summary>
    public string Description { get; }

    /// <summary>曲线快照；滑杆变化时由 ViewModel 同步回写，来回切换预设才不会读到旧值。</summary>
    public double[] Bands { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>均衡器里的一根滑杆。</summary>
public sealed partial class EqBandItem : ObservableObject
{
    public EqBandItem(int frequency, double gain)
    {
        Frequency = frequency;
        Label = FormatFrequency(frequency);
        Gain = gain;
    }

    public int Frequency { get; }

    /// <summary>频率标签，超过 1 kHz 用 k 缩写。</summary>
    public string Label { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GainText))]
    public partial double Gain { get; set; }

    /// <summary>滑杆上方的带符号增益，只显示到 0.5 dB。</summary>
    public string GainText
    {
        get
        {
            var value = Math.Round(Gain * 2, MidpointRounding.AwayFromZero) / 2;
            return value > 0
                ? $"+{value.ToString("0.#", CultureInfo.InvariantCulture)}"
                : value.ToString("0.#", CultureInfo.InvariantCulture);
        }
    }

    private static string FormatFrequency(int frequency) =>
        frequency >= 1000
            ? (frequency / 1000d).ToString("0.#", CultureInfo.InvariantCulture) + "k"
            : frequency.ToString(CultureInfo.InvariantCulture);
}

/// <summary>环境音列表里的一行。</summary>
public sealed partial class AmbientSoundItem(AmbientSoundDefinition sound) : ObservableObject
{
    public AmbientSoundDefinition Sound { get; } = sound;

    public string Id => Sound.Id;

    public string Name => Sound.Name;

    public string Description => Sound.Description;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
