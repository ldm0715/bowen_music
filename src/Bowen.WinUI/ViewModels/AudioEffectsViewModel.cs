using System.Collections.ObjectModel;
using Bowen.Core.Audio;
using Bowen.Core.Models;
using Bowen.Core.Services.Abstractions;
using Bowen.WinUI.Playback;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;

namespace Bowen.WinUI.ViewModels;

/// <summary>
/// 音效面板的状态与落盘。
/// </summary>
/// <remarks>
/// <para>
/// <b>单例。</b> 播放条与歌词页共用同一份选择；环境音只生成一次，两处看到的是同一张卡。
/// </para>
/// <para>
/// <b>滤镜链只有一条 <c>af</c>。</b> 预设、环境音、总开关任何一项变化都重建整条链；
/// 只有环境音滑杆走 <c>af-command</c>，避免拖动时反复重建卷积/混音图。
/// </para>
/// </remarks>
public sealed partial class AudioEffectsViewModel : ObservableObject
{
    private readonly IPlaybackService _engine;
    private readonly IAudioEffectSettingsStore? _store;
    private readonly ILogger<AudioEffectsViewModel> _logger;
    private readonly DispatcherQueueTimer? _saveTimer;
    private readonly DispatcherQueueTimer? _applyTimer;
    private readonly SemaphoreSlim _generateGate = new(1, 1);

    private AudioEffectSettings _settings;
    private bool _loading = true;
    private bool _graphHasTexture;
    private int _applyVersion;

    public AudioEffectsViewModel(
        IPlaybackService engine,
        IAudioEffectSettingsStore? store = null,
        ILogger<AudioEffectsViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _engine = engine;
        _store = store;
        _logger = logger ?? NullLogger<AudioEffectsViewModel>.Instance;
        _settings = (store?.Load() ?? AudioEffectSettings.Default).Normalized();

        foreach (var sound in AudioEffectCatalog.AmbientSounds)
        {
            AmbientSounds.Add(new AmbientSoundItem(sound));
        }

        var dispatcher = DispatcherQueue.GetForCurrentThread();
        if (dispatcher is not null)
        {
            _saveTimer = dispatcher.CreateTimer();
            _saveTimer.Interval = TimeSpan.FromSeconds(1);
            _saveTimer.IsRepeating = false;
            _saveTimer.Tick += (_, _) => Persist();

            _applyTimer = dispatcher.CreateTimer();
            _applyTimer.Interval = TimeSpan.FromMilliseconds(80);
            _applyTimer.IsRepeating = false;
            _applyTimer.Tick += (_, _) => _ = ApplyAsync();
        }

        IsEnabled = _settings.Enabled;
        AmbientVolume = _settings.AmbientVolume;
        SelectedTabIndex = ResolveInitialTab(_settings);
        _loading = false;

        RebuildVisiblePresets();
        RefreshSelection();
        QueueApply();
    }

    /// <summary>页签名。环境音是独立叠加层，不占页签，固定显示在下方共享行。</summary>
    public IReadOnlyList<string> Tabs { get; } = ["经典音效", "增强音效"];

    /// <summary>当前页签里的预设卡。</summary>
    public ObservableCollection<AudioEffectPresetItem> VisiblePresets { get; } = [];

    /// <summary>当前页签下选中的预设。给托盘面板的下拉框用。</summary>
    public AudioEffectPresetItem? SelectedPreset =>
        VisiblePresets.FirstOrDefault(item => item.IsSelected);

    /// <summary>全部环境音，固定 11 项。</summary>
    public ObservableCollection<AmbientSoundItem> AmbientSounds { get; } = [];

    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGalaxyTab))]
    [NotifyPropertyChangedFor(nameof(IsViperTab))]
    public partial int SelectedTabIndex { get; set; }

    [ObservableProperty]
    public partial double AmbientVolume { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string StatusText { get; set; } = "";

    public bool IsGalaxyTab => SelectedTabIndex == 0;

    public bool IsViperTab => SelectedTabIndex == 1;

    public bool HasStatus => StatusText.Length > 0;

    /// <summary>环境音是否已选；音量条据此启用。</summary>
    public bool HasAmbientSelected => ActiveAmbientName.Length > 0;

    /// <summary>共享行里环境音按钮的文字。</summary>
    public string AmbientButtonText => ActiveAmbientName.Length > 0 ? ActiveAmbientName : "无";

    /// <summary>当前选中的预设名；没选时为空串。</summary>
    public string ActivePresetName => AudioEffectCatalog.FindPreset(_settings.PresetId)?.Name ?? "";

    /// <summary>当前选中的环境音名；没选时为空串。</summary>
    public string ActiveAmbientName => AudioEffectCatalog.FindAmbient(_settings.AmbientId)?.Name ?? "";

    /// <summary>头部显示的名字：预设优先，其次环境音；都没选时为空串。</summary>
    public string ActiveEffectName => ActivePresetName.Length > 0 ? ActivePresetName : ActiveAmbientName;

    [RelayCommand]
    private void SelectPreset(AudioEffectPresetItem? item)
    {
        if (item is null)
        {
            return;
        }

        var next = string.Equals(_settings.PresetId, item.Id, StringComparison.OrdinalIgnoreCase)
            ? ""
            : item.Id;
        _settings = _settings with { PresetId = next };
        SetEnabledFromEffects();
        RefreshSelection();
        QueueApply();
        ScheduleSave();
    }

    [RelayCommand]
    private void SelectAmbient(AmbientSoundItem? item)
    {
        if (item is null)
        {
            return;
        }

        var next = string.Equals(_settings.AmbientId, item.Id, StringComparison.OrdinalIgnoreCase)
            ? ""
            : item.Id;
        _settings = _settings with { AmbientId = next };
        SetEnabledFromEffects();
        RefreshSelection();
        QueueApply();
        ScheduleSave();
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        _settings = _settings with { Enabled = value };
        QueueApply();
        ScheduleSave();
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        if (_loading)
        {
            return;
        }

        RebuildVisiblePresets();
    }

    partial void OnAmbientVolumeChanged(double value)
    {
        if (_loading)
        {
            return;
        }

        _settings = _settings with { AmbientVolume = value };
        if (_settings.Enabled && _settings.AmbientId.Length > 0)
        {
            // 纹理（黑胶底噪）也在同一张 amix 里，权重的个数必须和当前图的输入数一致。
            _engine.SendAudioFilterCommand(
                AudioEffectGraphBuilder.FilterLabel,
                "weights",
                AudioEffectGraphBuilder.MixWeights(_settings, _graphHasTexture, hasAmbient: true),
                "amix");
        }

        ScheduleSave();
    }

    private void SetEnabledFromEffects()
    {
        var enabled = _settings.PresetId.Length > 0 || _settings.AmbientId.Length > 0;
        _settings = _settings with { Enabled = enabled };
        _loading = true;
        IsEnabled = enabled;
        _loading = false;
    }

    private void QueueApply()
    {
        if (_loading)
        {
            return;
        }

        if (_applyTimer is null)
        {
            _ = ApplyAsync();
            return;
        }

        _applyTimer.Stop();
        _applyTimer.Start();
    }

    private async Task ApplyAsync()
    {
        var version = Interlocked.Increment(ref _applyVersion);
        _settings = _settings.Normalized();

        string? ambientPath = null;
        string? texturePath = null;
        if (_settings.Enabled)
        {
            var preset = AudioEffectCatalog.FindPreset(_settings.PresetId);
            var sound = _settings.AmbientId.Length > 0
                ? AudioEffectCatalog.FindAmbient(_settings.AmbientId)
                : null;

            // 纹理和环境音写同一个缓存目录，共用一把锁避免并发解压撞临时文件。
            await _generateGate.WaitAsync();
            try
            {
                if (preset?.TextureId is { Length: > 0 } textureId)
                {
                    try
                    {
                        texturePath = await Task.Run(() => AmbientSoundGenerator.EnsureLoop(textureId));
                    }
                    catch (Exception exception)
                    {
                        _logger.LogWarning(exception, "音效纹理生成失败：{Id}", textureId);
                    }
                }

                if (sound is not null)
                {
                    try
                    {
                        ambientPath = await Task.Run(() => AmbientSoundGenerator.Ensure(sound));
                    }
                    catch (Exception exception)
                    {
                        _logger.LogWarning(exception, "环境音生成失败：{Id}", sound.Id);
                        StatusText = $"环境音“{sound.Name}”生成失败";
                    }
                }
            }
            finally
            {
                _generateGate.Release();
            }
        }

        if (version != Volatile.Read(ref _applyVersion))
        {
            return;
        }

        _graphHasTexture = texturePath is not null;
        var filter = AudioEffectGraphBuilder.Build(_settings, ambientPath, texturePath);
        _engine.SetAudioFilter(filter);
        if (ambientPath is not null || _settings.AmbientId.Length == 0)
        {
            StatusText = "";
        }
    }

    private void RebuildVisiblePresets()
    {
        VisiblePresets.Clear();
        var category = SelectedTabIndex == 1 ? AudioEffectCategory.Viper : AudioEffectCategory.Galaxy;
        foreach (var preset in AudioEffectCatalog.PresetsFor(category))
        {
            VisiblePresets.Add(new AudioEffectPresetItem(preset)
            {
                IsSelected = string.Equals(preset.Id, _settings.PresetId, StringComparison.OrdinalIgnoreCase),
            });
        }

        OnPropertyChanged(nameof(SelectedPreset));
    }

    private void RefreshSelection()
    {
        foreach (var item in VisiblePresets)
        {
            item.IsSelected = string.Equals(item.Id, _settings.PresetId, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var item in AmbientSounds)
        {
            item.IsSelected = string.Equals(item.Id, _settings.AmbientId, StringComparison.OrdinalIgnoreCase);
        }

        OnPropertyChanged(nameof(SelectedPreset));
        OnPropertyChanged(nameof(ActivePresetName));
        OnPropertyChanged(nameof(ActiveAmbientName));
        OnPropertyChanged(nameof(ActiveEffectName));
        OnPropertyChanged(nameof(HasAmbientSelected));
        OnPropertyChanged(nameof(AmbientButtonText));
    }

    private void ScheduleSave()
    {
        if (_store is null)
        {
            return;
        }

        if (_saveTimer is null)
        {
            Persist();
            return;
        }

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void Persist() => _store?.Save(_settings);

    private static int ResolveInitialTab(AudioEffectSettings settings) =>
        settings.PresetId.StartsWith("viper.", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
}
