using System.Collections.ObjectModel;
using System.ComponentModel;
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
    private bool _syncingName;
    private bool _suppressBandWrites;
    private CustomEqPresetItem? _loadedCustomPreset;
    private CustomEqPreset? _preEditPreset;
    private string _editingPresetId = "";
    private string _preEditPresetId = "";
    private bool _preEditEnabled;
    private bool _editingNewPreset;
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
        RebuildCustomPresets();
        _loading = false;

        RebuildVisiblePresets();
        RefreshSelection();
        QueueApply();
    }

    /// <summary>页签名。环境音是独立叠加层，不占页签，固定显示在下方共享行。</summary>
    public IReadOnlyList<string> Tabs { get; } = ["经典音效", "增强音效", "自定义"];

    /// <summary>当前页签里的预设卡。</summary>
    public ObservableCollection<AudioEffectPresetItem> VisiblePresets { get; } = [];

    /// <summary>用户保存的自定义预设。</summary>
    public ObservableCollection<CustomEqPresetItem> CustomPresets { get; } = [];

    /// <summary>当前编辑的自定义预设的十根滑杆。</summary>
    public ObservableCollection<EqBandItem> CustomBands { get; } = [];

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
    [NotifyPropertyChangedFor(nameof(IsCustomTab))]
    public partial int SelectedTabIndex { get; set; }

    /// <summary>当前生效的自定义预设；选中的是内置预设时为 <c>null</c>。</summary>
    public CustomEqPresetItem? SelectedCustomPreset =>
        CustomPresets.FirstOrDefault(item => item.IsSelected);

    /// <summary>编辑器里的名称输入框。</summary>
    [ObservableProperty]
    public partial string CustomPresetName { get; set; } = "";

    /// <summary>编辑器里的说明输入框。</summary>
    [ObservableProperty]
    public partial string CustomPresetDescription { get; set; } = "";

    /// <summary>是否正在新建 / 编辑自定义预设；编辑器与卡片网格互斥显示。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditCustomPreset))]
    [NotifyPropertyChangedFor(nameof(CanToggleEnabled))]
    public partial bool IsEditingCustomPreset { get; set; }

    [ObservableProperty]
    public partial double AmbientVolume { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string StatusText { get; set; } = "";

    public bool IsGalaxyTab => SelectedTabIndex == 0;

    public bool IsViperTab => SelectedTabIndex == 1;

    public bool IsCustomTab => SelectedTabIndex == 2;

    /// <summary>自定义页签是否有生效中的预设；决定下方均衡器是否显示。</summary>
    public bool HasCustomPreset => SelectedCustomPreset is not null;

    /// <summary>是否保存过自定义预设；决定空状态提示是否显示。</summary>
    public bool HasAnyCustomPreset => CustomPresets.Count > 0;

    /// <summary>托盘效率面板里自定义预设下拉框的占位文字。</summary>
    public string CustomPresetPlaceholder => HasAnyCustomPreset ? "选择自定义音效" : "暂无自定义预设";

    /// <summary>选中了自定义预设且不在编辑态时，显示「编辑 / 删除」。</summary>
    public bool CanEditCustomPreset => HasCustomPreset && !IsEditingCustomPreset;

    /// <summary>编辑期间锁住总开关，避免预览状态与提交状态互相覆盖。</summary>
    public bool CanToggleEnabled => !IsEditingCustomPreset;

    public bool HasStatus => StatusText.Length > 0;

    /// <summary>环境音是否已选；音量条据此启用。</summary>
    public bool HasAmbientSelected => ActiveAmbientName.Length > 0;

    /// <summary>共享行里环境音按钮的文字。</summary>
    public string AmbientButtonText => ActiveAmbientName.Length > 0 ? ActiveAmbientName : "无";

    /// <summary>当前选中的预设名；没选时为空串。</summary>
    public string ActivePresetName =>
        AudioEffectCatalog.FindPreset(_settings.PresetId)?.Name
        ?? AudioEffectCatalog.FindCustomPreset(_settings.CustomPresets, _settings.PresetId)?.Name
        ?? "";

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

    /// <summary>与内置预设卡片一致：点击选中，再点同一张取消。</summary>
    [RelayCommand]
    private void SelectCustomPreset(CustomEqPresetItem? item)
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

    /// <summary>新建一套空曲线并直接进入编辑器；取消时删除这套草稿。</summary>
    [RelayCommand]
    private void AddCustomPreset()
    {
        if (IsEditingCustomPreset)
        {
            return;
        }

        if (_settings.CustomPresets.Count >= AudioEffectCatalog.MaxCustomPresets)
        {
            StatusText = $"最多保存 {AudioEffectCatalog.MaxCustomPresets} 套自定义音效";
            return;
        }

        _preEditPresetId = _settings.PresetId;
        _preEditEnabled = _settings.Enabled;
        _editingNewPreset = true;
        _preEditPreset = null;
        var id = AudioEffectCatalog.CustomPresetIdPrefix + Guid.NewGuid().ToString("N")[..8];
        _editingPresetId = id;
        var preset = new CustomEqPreset
        {
            Id = id,
            Name = NextCustomPresetName(),
            Description = CustomEqPreset.DefaultDescription,
            Bands = new double[AudioEffectCatalog.EqBandCount],
        };

        _settings = _settings with
        {
            PresetId = id,
            CustomPresets = [.. _settings.CustomPresets, preset],
        };
        SetEnabledFromEffects();
        SelectedTabIndex = 2;
        RebuildCustomPresets();
        RefreshSelection();
        IsEditingCustomPreset = true;
        QueueApply();
    }

    /// <summary>编辑当前生效的自定义预设；保存前不落盘，取消时恢复原设置。</summary>
    [RelayCommand]
    private void EditCustomPreset()
    {
        if (IsEditingCustomPreset || SelectedCustomPreset is null)
        {
            return;
        }

        _editingPresetId = SelectedCustomPreset.Id;
        _editingNewPreset = false;
        _preEditPreset = new CustomEqPreset
        {
            Id = SelectedCustomPreset.Id,
            Name = SelectedCustomPreset.Name,
            Description = SelectedCustomPreset.Description,
            Bands = [.. SelectedCustomPreset.Bands],
        };
        SetEnabledFromEffects();
        IsEditingCustomPreset = true;
        LoadCustomBands(SelectedCustomPreset);
        QueueApply();
    }

    [RelayCommand]
    private void SaveCustomPreset()
    {
        if (!IsEditingCustomPreset)
        {
            return;
        }

        _settings = _settings.Normalized();
        _preEditPreset = null;
        IsEditingCustomPreset = false;
        RebuildCustomPresets();
        RefreshSelection();
        QueueApply();
        Persist();
    }

    [RelayCommand]
    private void CancelCustomEdit()
    {
        if (!IsEditingCustomPreset)
        {
            return;
        }

        var id = _editingPresetId;
        if (_editingNewPreset)
        {
            // 新建的草稿没有落盘过，取消时直接移除，并把选择与开关恢复成进入编辑器前的样子。
            var remaining = _settings.CustomPresets
                .Where(preset => !string.Equals(preset.Id, id, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var presetId = string.Equals(_settings.PresetId, id, StringComparison.OrdinalIgnoreCase)
                ? _preEditPresetId
                : _settings.PresetId;
            _settings = (_settings with { PresetId = presetId, CustomPresets = remaining }).Normalized();
            _settings = _settings with { Enabled = _preEditEnabled };
        }
        else if (_preEditPreset is not null)
        {
            var restored = _preEditPreset;
            _settings = _settings with
            {
                CustomPresets = _settings.CustomPresets
                    .Select(preset => string.Equals(preset.Id, id, StringComparison.OrdinalIgnoreCase)
                        ? restored with { Bands = [.. restored.Bands] }
                        : preset)
                    .ToList(),
            };
        }

        IsEditingCustomPreset = false;
        _preEditPreset = null;
        RebuildCustomPresets();
        RefreshSelection();
        QueueApply();
        Persist();
    }

    [RelayCommand]
    private void DeleteCustomPreset()
    {
        if (IsEditingCustomPreset)
        {
            return;
        }

        var target = SelectedCustomPreset;
        if (target is null)
        {
            return;
        }

        var remaining = _settings.CustomPresets
            .Where(preset => !string.Equals(preset.Id, target.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // 能点的删除按钮只出现在生效中的预设上，所以这里总是清空选择。
        _settings = _settings with { PresetId = "", CustomPresets = remaining };
        SetEnabledFromEffects();
        StatusText = "";
        RebuildCustomPresets();
        RefreshSelection();
        QueueApply();
        ScheduleSave();
    }

    [RelayCommand]
    private void ResetCustomBands()
    {
        if (SelectedCustomPreset is null)
        {
            return;
        }

        _suppressBandWrites = true;
        foreach (var band in CustomBands)
        {
            band.Gain = 0;
        }

        _suppressBandWrites = false;
        WriteCustomBands();
        QueueApply();
    }

    partial void OnCustomPresetNameChanged(string value)
    {
        if (_loading || _syncingName || SelectedCustomPreset is null)
        {
            return;
        }

        var name = value.Trim();
        if (name.Length == 0)
        {
            return;
        }

        if (name.Length > AudioEffectSettings.MaxCustomPresetNameLength)
        {
            name = name[..AudioEffectSettings.MaxCustomPresetNameLength];
        }

        SelectedCustomPreset.Name = name;
        UpdateSelectedCustomPreset(preset => preset with { Name = name });
        OnPropertyChanged(nameof(ActivePresetName));
        OnPropertyChanged(nameof(ActiveEffectName));
    }

    partial void OnCustomPresetDescriptionChanged(string value)
    {
        if (_loading || _syncingName || SelectedCustomPreset is null)
        {
            return;
        }

        var description = value.Trim();
        if (description.Length > AudioEffectSettings.MaxCustomPresetDescriptionLength)
        {
            description = description[..AudioEffectSettings.MaxCustomPresetDescriptionLength];
        }

        UpdateSelectedCustomPreset(preset => preset with { Description = description });
    }

    private void UpdateSelectedCustomPreset(Func<CustomEqPreset, CustomEqPreset> update)
    {
        if (SelectedCustomPreset is null)
        {
            return;
        }

        var id = SelectedCustomPreset.Id;
        _settings = _settings with
        {
            CustomPresets = _settings.CustomPresets
                .Select(preset => string.Equals(preset.Id, id, StringComparison.OrdinalIgnoreCase)
                    ? update(preset)
                    : preset)
                .ToList(),
        };
    }

    private void OnBandPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(EqBandItem.Gain)
            || _loading
            || _suppressBandWrites
            || SelectedCustomPreset is null)
        {
            return;
        }

        WriteCustomBands();
        QueueApply();
        ScheduleSave();
    }

    private void LoadCustomBands(CustomEqPresetItem? item)
    {
        foreach (var band in CustomBands)
        {
            band.PropertyChanged -= OnBandPropertyChanged;
        }

        CustomBands.Clear();
        _syncingName = true;
        CustomPresetName = item?.Name ?? "";
        CustomPresetDescription = item?.Description ?? "";
        _syncingName = false;
        if (item is null)
        {
            return;
        }

        for (var index = 0; index < AudioEffectCatalog.EqBandCount; index++)
        {
            var gain = index < item.Bands.Length ? item.Bands[index] : 0;
            var band = new EqBandItem(AudioEffectCatalog.EqBandFrequencies[index], gain);
            band.PropertyChanged += OnBandPropertyChanged;
            CustomBands.Add(band);
        }
    }

    private void WriteCustomBands()
    {
        if (SelectedCustomPreset is null)
        {
            return;
        }

        var bands = CustomBands.Select(band => band.Gain).ToArray();
        var id = SelectedCustomPreset.Id;
        SelectedCustomPreset.Bands = bands;
        _settings = _settings with
        {
            CustomPresets = _settings.CustomPresets
                .Select(preset => string.Equals(preset.Id, id, StringComparison.OrdinalIgnoreCase)
                    ? preset with { Bands = bands }
                    : preset)
                .ToList(),
        };
    }

    private void RebuildCustomPresets()
    {
        CustomPresets.Clear();
        foreach (var preset in _settings.CustomPresets)
        {
            CustomPresets.Add(new CustomEqPresetItem(preset));
        }

        SyncCustomPresetSelection();
    }

    /// <summary>
    /// 让卡片的选中态跟随 <see cref="AudioEffectSettings.PresetId"/>，
    /// 并在生效的自定义预设变化时重载连着的十根滑杆。
    /// </summary>
    private void SyncCustomPresetSelection()
    {
        CustomEqPresetItem? selected = null;
        foreach (var item in CustomPresets)
        {
            item.IsSelected = string.Equals(item.Id, _settings.PresetId, StringComparison.OrdinalIgnoreCase);
            if (item.IsSelected)
            {
                selected = item;
            }
        }

        if (!ReferenceEquals(selected, _loadedCustomPreset))
        {
            _loadedCustomPreset = selected;
            LoadCustomBands(selected);
        }

        OnPropertyChanged(nameof(SelectedCustomPreset));
        OnPropertyChanged(nameof(HasCustomPreset));
        OnPropertyChanged(nameof(CanEditCustomPreset));
        OnPropertyChanged(nameof(HasAnyCustomPreset));
        OnPropertyChanged(nameof(CustomPresetPlaceholder));
    }

    private string NextCustomPresetName()
    {
        for (var index = 1; index <= AudioEffectCatalog.MaxCustomPresets; index++)
        {
            var name = $"自定义 {index}";
            if (!_settings.CustomPresets.Any(preset =>
                    string.Equals(preset.Name, name, StringComparison.Ordinal)))
            {
                return name;
            }
        }

        return "自定义";
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

        // 编辑期间把页面钉在自定义页签，避免托盘等处切换后编辑器与可见内容对不上。
        if (IsEditingCustomPreset && value != 2)
        {
            _loading = true;
            SelectedTabIndex = 2;
            _loading = false;
            return;
        }

        // 自定义页签不占用 VisiblePresets；托盘面板仍按上一次的经典/增强列表显示。
        if (value == 2)
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

        SyncCustomPresetSelection();
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

    /// <summary>编辑态只预览、不落盘；保存或取消时由命令显式写入。</summary>
    private void Persist()
    {
        if (IsEditingCustomPreset)
        {
            return;
        }

        _store?.Save(_settings);
    }

    private static int ResolveInitialTab(AudioEffectSettings settings)
    {
        if (AudioEffectCatalog.FindCustomPreset(settings.CustomPresets, settings.PresetId) is not null)
        {
            return 2;
        }

        return settings.PresetId.StartsWith("viper.", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
    }
}
