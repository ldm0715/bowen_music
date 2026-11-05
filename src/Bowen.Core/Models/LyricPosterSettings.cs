namespace Bowen.Core.Models;

/// <summary>海报背景色的来源。</summary>
public enum LyricPosterBackgroundSource
{
    /// <summary>跟随当前封面自动取色。</summary>
    Auto = 0,

    /// <summary>色板里的预设颜色。</summary>
    Preset = 1,

    /// <summary>色轮里选的颜色。</summary>
    Custom = 2,
}

/// <summary>海报文字颜色的来源。</summary>
public enum LyricPosterTextColorMode
{
    /// <summary>按背景亮度自动取白色或近黑色。</summary>
    Auto = 0,

    /// <summary>固定白色。</summary>
    White = 1,

    /// <summary>固定近黑色。</summary>
    Black = 2,

    /// <summary>色板或色轮选的颜色。</summary>
    Custom = 3,
}

/// <summary>
/// 歌词海报的自定义偏好。
/// </summary>
/// <remarks>
/// <b>独立于 <see cref="LyricsSettings"/></b>：歌词页与海报的字体、背景是两套东西，
/// 海报换字体不应该把歌词页的花式设置带过去，反之亦然。
/// 颜色都存 <c>0xAARRGGBB</c>，写盘前 alpha 统一置为不透明。
/// </remarks>
public sealed record LyricPosterSettings
{
    public const uint DefaultBackgroundColor = 0xFF24324A;
    public const uint DefaultTextColor = 0xFFFFFFFF;

    /// <summary>歌词字体族；空串表示海报默认字体。</summary>
    public string FontFamily { get; init; } = string.Empty;

    public LyricPosterBackgroundSource BackgroundSource { get; init; } = LyricPosterBackgroundSource.Auto;

    /// <summary>实际生效的背景色（预设或自定义）。</summary>
    public uint BackgroundColorArgb { get; init; } = DefaultBackgroundColor;

    /// <summary>色轮最近一次选中的背景色，供「自定义」圆点显示。</summary>
    public uint BackgroundCustomColorArgb { get; init; } = DefaultBackgroundColor;

    public LyricPosterTextColorMode TextColorMode { get; init; } = LyricPosterTextColorMode.Auto;

    /// <summary>自定义文字颜色；只在 <see cref="TextColorMode"/> 为 Custom 时生效。</summary>
    public uint TextColorArgb { get; init; } = DefaultTextColor;

    /// <summary>色轮最近一次选中的文字颜色，供「自定义」圆点显示。</summary>
    public uint TextCustomColorArgb { get; init; } = DefaultTextColor;

    public static LyricPosterSettings Default { get; } = new();

    /// <summary>
    /// 背景预设：沿用歌词页的深色板，另加白与暖白，供浅色底 + 深色字使用。
    /// </summary>
    public static readonly (uint Argb, string Name)[] BackgroundPalette =
    [
        .. LyricsSettings.BackgroundPalette,
        (0xFFFFFFFF, "白色"),
        (0xFFF4F1EA, "暖白"),
    ];

    /// <summary>文字色板：高亮色板去掉白色（白色有独立的 White 选项）。</summary>
    public static readonly (uint Argb, string Name)[] TextColorPalette =
        LyricsSettings.HighlightPalette[1..];

    /// <summary>把缺字段 / 非法值收敛到可用值。</summary>
    public LyricPosterSettings Normalized()
    {
        var backgroundSource = Enum.IsDefined(BackgroundSource)
            ? BackgroundSource
            : LyricPosterBackgroundSource.Auto;
        var textColorMode = Enum.IsDefined(TextColorMode)
            ? TextColorMode
            : LyricPosterTextColorMode.Auto;

        return this with
        {
            FontFamily = string.IsNullOrWhiteSpace(FontFamily) ? string.Empty : FontFamily.Trim(),
            BackgroundSource = backgroundSource,
            BackgroundColorArgb = NormalizeColor(BackgroundColorArgb, DefaultBackgroundColor),
            BackgroundCustomColorArgb = NormalizeColor(BackgroundCustomColorArgb, DefaultBackgroundColor),
            TextColorMode = textColorMode,
            TextColorArgb = NormalizeColor(TextColorArgb, DefaultTextColor),
            TextCustomColorArgb = NormalizeColor(TextCustomColorArgb, DefaultTextColor),
        };
    }

    private static uint NormalizeColor(uint argb, uint fallback)
        => (argb == 0 ? fallback : argb) | 0xFF000000;
}
