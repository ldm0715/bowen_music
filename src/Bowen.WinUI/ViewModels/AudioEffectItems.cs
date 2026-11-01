using Bowen.Core.Audio;
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
