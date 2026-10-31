using System.ComponentModel;
using Bowen.Core.Media;
using Bowen.Core.Models;
using Bowen.Core.Services.Abstractions;
using Bowen.WinUI.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Bowen.WinUI.ViewModels;

/// <summary>纯色预设的一项：名称、色值与用于色块的画刷。</summary>
public sealed record ShellColorOption(string Name, uint Argb, SolidColorBrush Brush);

/// <summary>
/// 主窗口外壳背景：模式、纯色与「跟随封面」。
/// </summary>
/// <remarks>
/// <para>
/// <b>只影响常规页面。</b> 全屏歌词与 MV 沉浸页有自己的背景设置，不归这里管。
/// </para>
/// <para>
/// <b>深浅主题各记一份手动色。</b> 浅色主题不该沿用深色主题的「暗绿 / 深蓝」，
/// 因此预设与选中的颜色都按主题分开；切换主题时各回各的选择。
/// </para>
/// <para>
/// <b>纯色跟随封面只影响运行时。</b> 自动取出来的颜色不落盘，落盘的是手动色；
/// 关掉跟随后回到当前主题的手动色。
/// </para>
/// <para>
/// <b>Mica 与自定义图片在不可用时回退封面取色。</b> Mica 需要 Windows 11；
/// 自定义图片要到阶段 C 才实现，这里先保留枚举值，避免以后改存储格式。
/// </para>
/// </remarks>
public sealed partial class ShellBackgroundViewModel : ObservableObject
{
    private readonly IShellBackgroundSettingsStore _store;
    private readonly PlayerViewModel _player;
    private readonly CoverPaletteLoader _loader;

    private ShellBackgroundMode _mode;
    private uint _manualDarkSolidColorArgb;
    private uint _manualLightSolidColorArgb;
    private bool _solidColorAuto;
    private IReadOnlyList<RgbColor> _autoSeeds = [];
    private IReadOnlyList<ShellColorOption> _solidColorOptions;
    private AmbientTheme _currentTheme = AmbientTheme.Dark;
    private int _autoVersion;

    /// <summary>自动纯色或手动纯色变化。窗口据此重绘纯色层。</summary>
    public event EventHandler? SolidColorChanged;

    public ShellBackgroundViewModel(
        IShellBackgroundSettingsStore store,
        PlayerViewModel player,
        ILogger<CoverPaletteLoader> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(logger);

        _store = store;
        _player = player;
        _loader = new CoverPaletteLoader(logger);

        var settings = store.Load();
        _mode = settings.Mode;
        _manualDarkSolidColorArgb = settings.SolidColorArgb;
        _manualLightSolidColorArgb = settings.LightSolidColorArgb;
        _solidColorAuto = settings.SolidColorAuto;
        _solidColorOptions = BuildSolidColorOptions(_currentTheme);

        _player.PropertyChanged += OnPlayerChanged;

        if (_solidColorAuto)
        {
            _ = RefreshAutoColorAsync();
        }
    }

    /// <summary>当前系统是否支持 Mica。</summary>
    public bool IsMicaSupported => MainWindow.IsMicaSupported;

    /// <summary>持久化的模式；可能包含当前系统不可用的值。</summary>
    public ShellBackgroundMode Mode => _mode;

    /// <summary>实际生效的模式；不可用的 Mica、未实现的自定义图片回退封面取色。</summary>
    public ShellBackgroundMode EffectiveMode => _mode switch
    {
        ShellBackgroundMode.Mica when !IsMicaSupported => ShellBackgroundMode.CoverAmbient,
        ShellBackgroundMode.CustomImage => ShellBackgroundMode.CoverAmbient,
        _ => _mode,
    };

    /// <summary>给下拉框用的下标：0 封面取色，1 Mica，2 纯色。</summary>
    public int ModeIndex
    {
        get => _mode switch
        {
            ShellBackgroundMode.Mica => 1,
            ShellBackgroundMode.SolidColor => 2,
            _ => 0,
        };
        set
        {
            var mode = value switch
            {
                1 => ShellBackgroundMode.Mica,
                2 => ShellBackgroundMode.SolidColor,
                _ => ShellBackgroundMode.CoverAmbient,
            };

            SetMode(mode);
        }
    }

    /// <summary>当前是否使用纯色背景。</summary>
    public bool IsSolid => _mode == ShellBackgroundMode.SolidColor;

    /// <summary>当前主题的纯色预设。浅色与深色是两套独立色板。</summary>
    public IReadOnlyList<ShellColorOption> SolidColorOptions => _solidColorOptions;

    /// <summary>
    /// 主题变化时换一套预设，并通知窗口纯色需要重绘。
    /// </summary>
    /// <remarks>
    /// 由 <c>MainWindow</c> 在 <c>ActualThemeChanged</c> 时调用。放在这里而不是注入
    /// <see cref="ThemeViewModel"/>，是为了让「跟随系统」的深夜切换也能走同一条通知。
    /// </remarks>
    public void UpdateTheme(AmbientTheme theme)
    {
        if (_currentTheme == theme)
        {
            return;
        }

        _currentTheme = theme;
        _solidColorOptions = BuildSolidColorOptions(theme);
        OnPropertyChanged(nameof(SolidColorOptions));
        OnPropertyChanged(nameof(SolidColorIndex));
        SolidColorChanged?.Invoke(this, EventArgs.Empty);
    }

    private static IReadOnlyList<ShellColorOption> BuildSolidColorOptions(AmbientTheme theme)
        => [.. ShellBackgroundSettings.SolidPresets(theme).Select(entry =>
            new ShellColorOption(entry.Name, entry.Argb, new SolidColorBrush(ToColor(entry.Argb))))];

    /// <summary>
    /// 当前主题下实际应用的纯色。
    /// </summary>
    /// <param name="theme">当前深色 / 浅色主题。</param>
    /// <remarks>
    /// 自动色走 <see cref="ColorPalette.SolidBackground"/> 按主题映射；
    /// 手动色直接使用当前主题自己那一份，不再从另一套主题的预设推导。
    /// </remarks>
    public uint SolidColorFor(AmbientTheme theme)
        => _solidColorAuto && _autoSeeds.Count > 0
            ? ToArgb(ColorPalette.SolidBackground(_autoSeeds, theme))
            : theme == AmbientTheme.Dark
                ? _manualDarkSolidColorArgb
                : _manualLightSolidColorArgb;

    /// <summary>当前主题的手动色在预设里的下标；不在预设中时返回 -1。</summary>
    public int SolidColorIndex
    {
        get
        {
            var presets = ShellBackgroundSettings.SolidPresets(_currentTheme);
            var current = _currentTheme == AmbientTheme.Dark
                ? _manualDarkSolidColorArgb
                : _manualLightSolidColorArgb;

            for (var i = 0; i < presets.Length; i++)
            {
                if (presets[i].Argb == current)
                {
                    return i;
                }
            }

            return -1;
        }
        set
        {
            var presets = ShellBackgroundSettings.SolidPresets(_currentTheme);

            if (value < 0 || value >= presets.Length)
            {
                return;
            }

            SetSolidColor(presets[value].Argb);
        }
    }

    /// <summary>纯色是否跟随当前封面自动取色。</summary>
    public bool SolidColorAuto
    {
        get => _solidColorAuto;
        set
        {
            if (_solidColorAuto == value)
            {
                return;
            }

            _solidColorAuto = value;

            if (!value)
            {
                _autoSeeds = [];
            }

            Persist();
            OnPropertyChanged(nameof(SolidColorAuto));
            OnPropertyChanged(nameof(IsManualSolidColor));
            SolidColorChanged?.Invoke(this, EventArgs.Empty);

            if (value)
            {
                _ = RefreshAutoColorAsync();
            }
        }
    }

    /// <summary>手动选色的下拉框是否可用；跟随封面时置灰。</summary>
    public bool IsManualSolidColor => !_solidColorAuto;

    private void SetMode(ShellBackgroundMode mode)
    {
        if (mode == ShellBackgroundMode.Mica && !IsMicaSupported)
        {
            return;
        }

        if (_mode == mode)
        {
            return;
        }

        _mode = mode;
        Persist();
        OnPropertyChanged(nameof(Mode));
        OnPropertyChanged(nameof(EffectiveMode));
        OnPropertyChanged(nameof(ModeIndex));
        OnPropertyChanged(nameof(IsSolid));

        // 从别的模式切回纯色时补一次自动取色；跟随开关打开时构造期可能还没有歌。
        if (mode == ShellBackgroundMode.SolidColor && _solidColorAuto)
        {
            _ = RefreshAutoColorAsync();
        }
    }

    private void SetSolidColor(uint argb)
    {
        var fallback = _currentTheme == AmbientTheme.Dark
            ? ShellBackgroundSettings.DefaultSolidColorArgb
            : ShellBackgroundSettings.DefaultLightSolidColorArgb;

        argb = argb == 0 ? fallback : argb | 0xFF000000;

        var current = _currentTheme == AmbientTheme.Dark
            ? _manualDarkSolidColorArgb
            : _manualLightSolidColorArgb;

        if (current == argb)
        {
            return;
        }

        if (_currentTheme == AmbientTheme.Dark)
        {
            _manualDarkSolidColorArgb = argb;
        }
        else
        {
            _manualLightSolidColorArgb = argb;
        }

        Persist();
        OnPropertyChanged(nameof(SolidColorIndex));
        SolidColorChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Persist()
        => _store.Save(new ShellBackgroundSettings
        {
            Mode = _mode,
            SolidColorArgb = _manualDarkSolidColorArgb,
            LightSolidColorArgb = _manualLightSolidColorArgb,
            SolidColorAuto = _solidColorAuto,
        });

    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(PlayerViewModel.CurrentTrackId)
            or nameof(PlayerViewModel.CurrentCoverUri))
        {
            _ = RefreshAutoColorAsync();
        }
    }

    /// <summary>
    /// 用当前封面重新取种子。真正的表面色在应用时按主题映射。
    /// </summary>
    /// <remarks>
    /// <b>结果不落盘。</b> 落盘的是手动色；关掉跟随后回到手动色。
    /// 切歌竞态用版本号挡住：旧的解码结果不能覆盖新歌的颜色。
    /// </remarks>
    private async Task RefreshAutoColorAsync()
    {
        if (!_solidColorAuto || _mode != ShellBackgroundMode.SolidColor)
        {
            return;
        }

        var version = ++_autoVersion;
        var seeds = await _loader.LoadAsync(_player.CurrentTrackId ?? 0, _player.CurrentCoverUri);

        if (version != _autoVersion || !_solidColorAuto)
        {
            return;
        }

        _autoSeeds = seeds;
        SolidColorChanged?.Invoke(this, EventArgs.Empty);
    }

    private static Color ToColor(uint argb)
        => Color.FromArgb(
            (byte)(argb >> 24),
            (byte)(argb >> 16),
            (byte)(argb >> 8),
            (byte)argb);

    private static uint ToArgb(RgbColor color)
        => 0xFF000000u | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
}
