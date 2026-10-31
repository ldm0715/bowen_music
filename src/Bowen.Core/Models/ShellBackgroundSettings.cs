using Bowen.Core.Media;

namespace Bowen.Core.Models;

/// <summary>主窗口外壳的背景来源。</summary>
public enum ShellBackgroundMode
{
    /// <summary>封面驱动的氛围色。</summary>
    CoverAmbient = 0,

    /// <summary>系统 Mica 材质，仅 Windows 11 支持。</summary>
    Mica = 1,

    /// <summary>自定义图片。阶段 C 实现，当前回退封面取色。</summary>
    CustomImage = 2,

    /// <summary>纯色，可跟随当前封面自动取色。</summary>
    SolidColor = 3,
}

/// <summary>
/// 主窗口外壳背景的偏好。
/// </summary>
/// <remarks>
/// <para>
/// <b>与 <see cref="ThemeSettings"/> 分开一份文件</b>：主题是「浅色 / 深色 / 跟随系统」，
/// 背景是「用哪种材质」。两者改动的时机与失败后果不同，混在一起会让一个坏字段连带重置另一个。
/// </para>
/// <para>
/// <b>只影响常规页面。</b> 全屏歌词与 MV 沉浸页有自己的背景设置，不读这份。
/// </para>
/// <para>
/// <b>深浅主题各记一份手动色。</b> 浅色主题不该沿用深色主题的「暗绿 / 深蓝」，
/// 因此预设与选中的颜色都按主题分开；切换主题时各回各的选择。
/// </para>
/// </remarks>
public sealed record ShellBackgroundSettings
{
    /// <summary>深色主题的默认纯色：深蓝，够暗且能看出颜色。</summary>
    public const uint DefaultSolidColorArgb = 0xFF24324A;

    /// <summary>浅色主题的默认纯色：雾蓝白，比卡片底色略深。</summary>
    public const uint DefaultLightSolidColorArgb = 0xFFF2F5F9;

    /// <summary>背景来源；默认封面取色。</summary>
    public ShellBackgroundMode Mode { get; init; } = ShellBackgroundMode.CoverAmbient;

    /// <summary>深色主题的手动纯色，<c>0xAARRGGBB</c>。</summary>
    public uint SolidColorArgb { get; init; } = DefaultSolidColorArgb;

    /// <summary>浅色主题的手动纯色，<c>0xAARRGGBB</c>。</summary>
    public uint LightSolidColorArgb { get; init; } = DefaultLightSolidColorArgb;

    /// <summary>纯色是否跟随当前封面自动取色。</summary>
    public bool SolidColorAuto { get; init; }

    /// <summary>深色主题的纯色预设。全部压到低明度，保证白色文字可读。</summary>
    public static readonly (uint Argb, string Name)[] DarkSolidPresets =
    [
        (0xFF24324A, "深蓝"), (0xFF2B2B31, "石墨"), (0xFF1F3B33, "墨绿"), (0xFF3A2E1C, "琥珀棕"),
        (0xFF3A2230, "梅红"), (0xFF2C2542, "深紫"), (0xFF1E3A44, "青灰"), (0xFF33302A, "暖灰"),
    ];

    /// <summary>浅色主题的纯色预设。全部保持高明度，保证深色文字可读。</summary>
    public static readonly (uint Argb, string Name)[] LightSolidPresets =
    [
        (0xFFF2F5F9, "雾蓝"), (0xFFF0F1F2, "云灰"), (0xFFEAF3E9, "豆绿"), (0xFFF6F0E4, "浅沙"),
        (0xFFF8EAEE, "浅樱"), (0xFFF0EBF7, "浅紫"), (0xFFE8F3F4, "浅青"), (0xFFF5F2EC, "米白"),
    ];

    /// <summary>默认设置：封面取色、深色深蓝、浅色雾蓝、不跟随封面。</summary>
    public static ShellBackgroundSettings Default { get; } = new();

    /// <summary>按主题取对应的预设列表。</summary>
    public static (uint Argb, string Name)[] SolidPresets(AmbientTheme theme)
        => theme == AmbientTheme.Dark ? DarkSolidPresets : LightSolidPresets;

    /// <summary>规范化枚举与颜色。</summary>
    /// <remarks>
    /// 逐项夹取，不整份退回默认：某一项写坏了，不该把模式与纯色一起抹掉。
    /// 未定义的数字枚举仍然能反序列化，所以这里要挡一道。
    /// </remarks>
    public ShellBackgroundSettings Normalized() => this with
    {
        Mode = Enum.IsDefined(Mode) ? Mode : ShellBackgroundMode.CoverAmbient,

        // 0 不是合法的颜色；其余颜色把 alpha 顶回不透明（预设不提供透明度）。
        SolidColorArgb = NormalizeColor(SolidColorArgb, DefaultSolidColorArgb),
        LightSolidColorArgb = NormalizeColor(LightSolidColorArgb, DefaultLightSolidColorArgb),
    };

    private static uint NormalizeColor(uint value, uint fallback)
        => value == 0 ? fallback : value | 0xFF000000;
}
