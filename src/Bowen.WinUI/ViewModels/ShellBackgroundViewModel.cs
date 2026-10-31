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
    private uint _customDarkSolidColorArgb;
    private uint _customLightSolidColorArgb;
    private bool _solidColorAuto;
    private IReadOnlyList<RgbColor> _autoSeeds = [];
    private IReadOnlyList<ShellColorOption> _solidColorOptions;
    private AmbientTheme _currentTheme = AmbientTheme.Dark;
    private string _imageFile = string.Empty;
    private double _imageDim;
    private double _imageBlur;
    private double _imageFocusX;
    private double _imageFocusY;
    private double _imageZoom;
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
        _customDarkSolidColorArgb = settings.CustomSolidColorArgb;
        _customLightSolidColorArgb = settings.LightCustomSolidColorArgb;

        // 旧文件没有自定义色字段：手动色不是预设时，把它迁移成自定义色，
        // 否则升级后下拉框里的自定义项会退回默认色。
        if (!IsPreset(_manualDarkSolidColorArgb, ShellBackgroundSettings.DarkSolidPresets)
            && _customDarkSolidColorArgb == ShellBackgroundSettings.DefaultSolidColorArgb)
        {
            _customDarkSolidColorArgb = _manualDarkSolidColorArgb;
        }

        if (!IsPreset(_manualLightSolidColorArgb, ShellBackgroundSettings.LightSolidPresets)
            && _customLightSolidColorArgb == ShellBackgroundSettings.DefaultLightSolidColorArgb)
        {
            _customLightSolidColorArgb = _manualLightSolidColorArgb;
        }

        _solidColorAuto = settings.SolidColorAuto;
        _imageFile = settings.ImageFile;
        _imageDim = settings.ImageDim;
        _imageBlur = settings.ImageBlur;
        _imageFocusX = settings.ImageFocusX;
        _imageFocusY = settings.ImageFocusY;
        _imageZoom = settings.ImageZoom;
        _solidColorOptions = BuildSolidColorOptions(_currentTheme, _customDarkSolidColorArgb);

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

    /// <summary>实际生效的模式；不可用的 Mica、未选择图片的自定义图片回退封面取色。</summary>
    public ShellBackgroundMode EffectiveMode => _mode switch
    {
        ShellBackgroundMode.Mica when !IsMicaSupported => ShellBackgroundMode.CoverAmbient,
        ShellBackgroundMode.CustomImage when _imageFile.Length == 0 => ShellBackgroundMode.CoverAmbient,
        _ => _mode,
    };

    /// <summary>给下拉框用的下标：0 封面取色，1 Mica，2 自定义图片，3 纯色。</summary>
    public int ModeIndex
    {
        get => _mode switch
        {
            ShellBackgroundMode.Mica => 1,
            ShellBackgroundMode.CustomImage => 2,
            ShellBackgroundMode.SolidColor => 3,
            _ => 0,
        };
        set
        {
            var mode = value switch
            {
                1 => ShellBackgroundMode.Mica,
                2 => ShellBackgroundMode.CustomImage,
                3 => ShellBackgroundMode.SolidColor,
                _ => ShellBackgroundMode.CoverAmbient,
            };

            SetMode(mode);
        }
    }

    /// <summary>当前是否使用纯色背景。</summary>
    public bool IsSolid => _mode == ShellBackgroundMode.SolidColor;

    /// <summary>当前是否使用自定义图片；没有图片时 <see cref="EffectiveMode"/> 仍回退封面取色。</summary>
    public bool IsCustomImage => _mode == ShellBackgroundMode.CustomImage;

    /// <summary>是否已经选过图片。</summary>
    public bool HasImage => _imageFile.Length > 0;

    /// <summary>已选图片的文件名。</summary>
    public string ImageFile => _imageFile;

    /// <summary>给设置页显示的选择状态。</summary>
    public string ImageStatus => _imageFile.Length > 0 ? "已选择" : "未选择";

    /// <summary>已选图片的完整路径；没有图片时为空串。</summary>
    public string ImageFilePath
        => _imageFile.Length == 0 ? string.Empty : Bowen.Core.Services.AppPaths.BackgroundFile(_imageFile);

    /// <summary>遮罩强度，0–0.8。</summary>
    public double ImageDim => _imageDim;

    /// <summary>模糊量，0–40 DIP。</summary>
    public double ImageBlur => _imageBlur;

    /// <summary>图片的水平裁剪中心，0–1。</summary>
    public double ImageFocusX => _imageFocusX;

    /// <summary>图片的垂直裁剪中心，0–1。</summary>
    public double ImageFocusY => _imageFocusY;

    /// <summary>图片的缩放，1–3。</summary>
    public double ImageZoom => _imageZoom;

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
        _solidColorOptions = BuildSolidColorOptions(
            theme,
            theme == AmbientTheme.Dark ? _customDarkSolidColorArgb : _customLightSolidColorArgb);
        OnPropertyChanged(nameof(SolidColorOptions));
        OnPropertyChanged(nameof(SolidColorIndex));
        OnPropertyChanged(nameof(ManualSolidColorArgb));
        OnPropertyChanged(nameof(CustomSolidColorArgb));
        SolidColorChanged?.Invoke(this, EventArgs.Empty);
    }

    private static IReadOnlyList<ShellColorOption> BuildSolidColorOptions(
        AmbientTheme theme,
        uint customArgb)
    {
        var options = ShellBackgroundSettings.SolidPresets(theme)
            .Select(entry => new ShellColorOption(
                entry.Name,
                entry.Argb,
                new SolidColorBrush(ToColor(entry.Argb))))
            .ToList();

        // 最后一项是保存的自定义色：名称带十六进制，色块就是保存的颜色。
        var color = Color.FromArgb(
            (byte)(customArgb >> 24),
            (byte)(customArgb >> 16),
            (byte)(customArgb >> 8),
            (byte)customArgb);

        options.Add(new ShellColorOption(
            $"自定义颜色 #{color.R:X2}{color.G:X2}{color.B:X2}",
            customArgb,
            new SolidColorBrush(color)));

        return options;
    }

    private static bool IsPreset(uint argb, (uint Argb, string Name)[] presets)
    {
        foreach (var preset in presets)
        {
            if (preset.Argb == argb)
            {
                return true;
            }
        }

        return false;
    }

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

    /// <summary>当前主题保存的自定义色；下拉框选中它时直接应用。</summary>
    public uint CustomSolidColorArgb => _currentTheme == AmbientTheme.Dark
        ? _customDarkSolidColorArgb
        : _customLightSolidColorArgb;

    /// <summary>当前主题的手动色，<c>0xAARRGGBB</c>。</summary>
    public uint ManualSolidColorArgb => _currentTheme == AmbientTheme.Dark
        ? _manualDarkSolidColorArgb
        : _manualLightSolidColorArgb;

    /// <summary>当前主题的手动色在预设里的下标；自定义色返回最后一项。</summary>
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

            return presets.Length;
        }
        set
        {
            var presets = ShellBackgroundSettings.SolidPresets(_currentTheme);

            if (value < 0 || value > presets.Length)
            {
                return;
            }

            // 最后一项是「自定义颜色」：不在这里改色，由设置页弹出色盘。
            if (value == presets.Length)
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
        OnPropertyChanged(nameof(IsCustomImage));

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
        OnPropertyChanged(nameof(ManualSolidColorArgb));
        SolidColorChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 选中下拉框里的「自定义颜色」：直接应用保存的自定义色，不打开色盘。
    /// </summary>
    public void ApplyCustomSolidColor()
    {
        var custom = CustomSolidColorArgb;
        var current = _currentTheme == AmbientTheme.Dark
            ? _manualDarkSolidColorArgb
            : _manualLightSolidColorArgb;

        if (current == custom)
        {
            return;
        }

        if (_currentTheme == AmbientTheme.Dark)
        {
            _manualDarkSolidColorArgb = custom;
        }
        else
        {
            _manualLightSolidColorArgb = custom;
        }

        Persist();
        OnPropertyChanged(nameof(SolidColorIndex));
        OnPropertyChanged(nameof(ManualSolidColorArgb));
        SolidColorChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 应用一个自定义色：关闭跟随封面，写入当前主题的自定义色与手动色，并落盘。
    /// </summary>
    public void SetSolidColorCustom(uint argb)
    {
        var fallback = _currentTheme == AmbientTheme.Dark
            ? ShellBackgroundSettings.DefaultSolidColorArgb
            : ShellBackgroundSettings.DefaultLightSolidColorArgb;

        argb = argb == 0 ? fallback : argb | 0xFF000000;

        if (_currentTheme == AmbientTheme.Dark)
        {
            _customDarkSolidColorArgb = argb;
            _manualDarkSolidColorArgb = argb;
        }
        else
        {
            _customLightSolidColorArgb = argb;
            _manualLightSolidColorArgb = argb;
        }

        _solidColorAuto = false;
        Persist();
        _solidColorOptions = BuildSolidColorOptions(_currentTheme, argb);
        OnPropertyChanged(nameof(SolidColorOptions));
        OnPropertyChanged(nameof(SolidColorIndex));
        OnPropertyChanged(nameof(SolidColorAuto));
        OnPropertyChanged(nameof(IsManualSolidColor));
        OnPropertyChanged(nameof(ManualSolidColorArgb));
        OnPropertyChanged(nameof(CustomSolidColorArgb));
        SolidColorChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 记录一张新图片及其裁剪参数。调用方负责删除被替换的旧文件。
    /// </summary>
    public void SetImage(string fileName, double dim, double blur, double focusX, double focusY, double zoom)
    {
        _imageFile = string.IsNullOrWhiteSpace(fileName)
            ? string.Empty
            : Path.GetFileName(fileName.Trim());

        _imageDim = double.IsFinite(dim)
            ? Math.Clamp(dim, ShellBackgroundSettings.MinimumImageDim, ShellBackgroundSettings.MaximumImageDim)
            : ShellBackgroundSettings.DefaultImageDim;

        _imageBlur = double.IsFinite(blur)
            ? Math.Clamp(blur, ShellBackgroundSettings.MinimumImageBlur, ShellBackgroundSettings.MaximumImageBlur)
            : ShellBackgroundSettings.DefaultImageBlur;

        _imageFocusX = double.IsFinite(focusX) ? Math.Clamp(focusX, 0, 1) : 0.5;
        _imageFocusY = double.IsFinite(focusY) ? Math.Clamp(focusY, 0, 1) : 0.5;
        _imageZoom = double.IsFinite(zoom)
            ? Math.Clamp(zoom, ShellBackgroundSettings.MinimumImageZoom, ShellBackgroundSettings.MaximumImageZoom)
            : 1;

        Persist();
        OnPropertyChanged(nameof(ImageFile));
        OnPropertyChanged(nameof(ImageStatus));
        OnPropertyChanged(nameof(ImageFilePath));
        OnPropertyChanged(nameof(HasImage));
        OnPropertyChanged(nameof(ImageDim));
        OnPropertyChanged(nameof(ImageBlur));
        OnPropertyChanged(nameof(ImageFocusX));
        OnPropertyChanged(nameof(ImageFocusY));
        OnPropertyChanged(nameof(ImageZoom));
        OnPropertyChanged(nameof(EffectiveMode));
    }

    /// <summary>调整图片遮罩强度。</summary>
    public void SetImageDim(double dim)
    {
        if (!double.IsFinite(dim))
        {
            return;
        }

        dim = Math.Clamp(dim, ShellBackgroundSettings.MinimumImageDim, ShellBackgroundSettings.MaximumImageDim);

        if (Math.Abs(_imageDim - dim) < 0.001)
        {
            return;
        }

        _imageDim = dim;
        Persist();
        OnPropertyChanged(nameof(ImageDim));
    }

    /// <summary>更新模糊量。</summary>
    public void SetImageBlur(double blur)
    {
        if (!double.IsFinite(blur))
        {
            return;
        }

        blur = Math.Clamp(blur, ShellBackgroundSettings.MinimumImageBlur, ShellBackgroundSettings.MaximumImageBlur);

        if (Math.Abs(_imageBlur - blur) < 0.01)
        {
            return;
        }

        _imageBlur = blur;
        Persist();
        OnPropertyChanged(nameof(ImageBlur));
    }

    /// <summary>更新图片的裁剪中心与缩放。</summary>
    public void SetImageCrop(double focusX, double focusY, double zoom)
    {
        _imageFocusX = double.IsFinite(focusX) ? Math.Clamp(focusX, 0, 1) : 0.5;
        _imageFocusY = double.IsFinite(focusY) ? Math.Clamp(focusY, 0, 1) : 0.5;
        _imageZoom = double.IsFinite(zoom)
            ? Math.Clamp(zoom, ShellBackgroundSettings.MinimumImageZoom, ShellBackgroundSettings.MaximumImageZoom)
            : 1;

        Persist();
        OnPropertyChanged(nameof(ImageFocusX));
        OnPropertyChanged(nameof(ImageFocusY));
        OnPropertyChanged(nameof(ImageZoom));
    }

    private void Persist()
        => _store.Save(new ShellBackgroundSettings
        {
            Mode = _mode,
            ImageFile = _imageFile,
            ImageDim = _imageDim,
            ImageBlur = _imageBlur,
            ImageFocusX = _imageFocusX,
            ImageFocusY = _imageFocusY,
            ImageZoom = _imageZoom,
            SolidColorArgb = _manualDarkSolidColorArgb,
            LightSolidColorArgb = _manualLightSolidColorArgb,
            CustomSolidColorArgb = _customDarkSolidColorArgb,
            LightCustomSolidColorArgb = _customLightSolidColorArgb,
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
