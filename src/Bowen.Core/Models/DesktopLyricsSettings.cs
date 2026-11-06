using System.Text.Json.Serialization;
using Bowen.Core.Services;

namespace Bowen.Core.Models;

/// <summary>桌面歌词条双行时的对齐方式。</summary>
[JsonConverter(typeof(TolerantEnumConverter<DesktopLyricsAlignment>))]
public enum DesktopLyricsAlignment
{
    /// <summary>两句都居中。</summary>
    Center = 0,

    /// <summary>当前句居左，下一句居右。</summary>
    Staggered = 1,
}

/// <summary>
/// 桌面歌词条的外观偏好。
/// </summary>
/// <remarks>
/// <para>
/// <b>不含窗口几何。</b> 位置与宽度属于「这台机器」的事，落在
/// <c>desktop-lyrics-window.json</c>；这里只有「这个人选了什么」，落在
/// <c>desktop-lyrics.json</c>。与外观设置 / 窗口记录的拆法一致。
/// </para>
/// <para>
/// <b>不做竖排。</b> WinUI 没有竖排排版，一字一行的近似对英文与标点都不成立，要真做只能自绘。
/// 这一条是明确推迟的，不是遗漏。
/// </para>
/// </remarks>
public sealed record DesktopLyricsSettings
{
    /// <summary>字号下限（DIP）。再小就完全看不清了。</summary>
    public const double MinimumFontSize = 20;

    /// <summary>字号上限（DIP）。</summary>
    public const double MaximumFontSize = 96;

    /// <summary>默认字号（DIP）。</summary>
    public const double DefaultFontSize = 42;

    /// <summary>默认高亮颜色：不透明青绿。</summary>
    public const uint DefaultTextColor = 0xFF00E5BF;

    /// <summary>
    /// 可选的高亮文字颜色。**只给基础色，不做取色板** —— 摆一个色轮太重了，
    /// 而这几个色在深浅壁纸上都能读。
    /// </summary>
    /// <remarks>
    /// 定义放在这里而不是留在窗口里：桌面歌词窗口内的设置面板与设置页共用同一份，
    /// 加一个颜色不该要改两个地方。元组的第二项是给提示与无障碍用的显示名。
    /// </remarks>
    public static readonly (uint Argb, string Name)[] Palette =
    [
        (0xFF00E5BF, "青绿"), (0xFF000000, "黑色"), (0xFFFF4D4F, "红色"),
        (0xFFFF922B, "橙色"), (0xFFFADB14, "黄色"), (0xFF37D67A, "绿色"),
        (0xFF4D9CFF, "蓝色"), (0xFFFF6FB5, "粉色"),
    ];

    /// <summary>
    /// 桌面歌词条是否开着。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>与外观那几项不同，它是一个「当前状态」而不是「默认值」</b>：歌词条被打开时它为 true，
    /// 设置页的开关跟着亮，关掉窗口它变回 false —— 两处是同一个值的两个入口，不存在谁覆盖谁。
    /// </para>
    /// <para>
    /// <b>要落盘</b>：上次退出时开着，下次启动就自动出现。默认关，首次使用不会平白多出一个浮窗。
    /// </para>
    /// </remarks>
    public bool IsEnabled { get; set; }

    /// <summary>字号，DIP。</summary>
    public double FontSize { get; set; } = DefaultFontSize;

    /// <summary>
    /// 字体族名；空串表示跟随渲染层默认（<c>LyricsRenderSettings.Default.FontFamily</c>）。
    /// </summary>
    /// <remarks>
    /// <b>只存名字，不校验是否存在。</b> 缺字体时渲染层回退默认，改它即时生效。
    /// </remarks>
    public string FontFamily { get; set; } = string.Empty;

    /// <summary>已唱部分的高亮色，<c>0xAARRGGBB</c>；未唱文字为不透明白色。</summary>
    public uint TextColorArgb { get; set; } = DefaultTextColor;

    /// <summary>是否显示下一句。</summary>
    public bool DualLine { get; set; }

    /// <summary>双行时的对齐方式。单行时无意义。</summary>
    public DesktopLyricsAlignment Alignment { get; set; } = DesktopLyricsAlignment.Center;

    /// <summary>是否锁定：锁上后不能拖动，也不能拉伸宽度。</summary>
    public bool Locked { get; set; }

    /// <summary>
    /// 透明处是否让鼠标穿透。
    /// </summary>
    /// <remarks>
    /// <b>默认关</b>：第一次打开桌面歌词时最常做的是拖动摆位置；默认穿透会让窗口看起来
    /// 点不动、拖不动，容易被当成故障。需要不挡住下方窗口时，在歌词条工具栏或设置页打开。
    /// </remarks>
    public bool PassThrough { get; set; } = false;

    /// <summary>默认设置。</summary>
    public static DesktopLyricsSettings Default { get; } = new();

    /// <summary>
    /// 把越界的值收进合法范围。
    /// </summary>
    /// <remarks>
    /// <b>逐项夹，不整份退回默认</b>：字号写坏了不该把颜色、双行、锁定的选择一起抹掉。
    /// 只有整份文件读不出来时才退回 <see cref="Default"/>。
    /// </remarks>
    public DesktopLyricsSettings Normalized() => this with
    {
        FontSize = double.IsFinite(FontSize)
            ? Math.Clamp(FontSize, MinimumFontSize, MaximumFontSize)
            : DefaultFontSize,

        FontFamily = FontFamily?.Trim() ?? string.Empty,

        // 颜色选择器不提供透明度，写盘时把 alpha 顶回不透明 ——
        // 未唱文字固定白色，旧版本的白色默认高亮改为青绿，避免两态无法区分。
        TextColorArgb = (TextColorArgb | 0xFF000000) == 0xFFFFFFFF
            ? DefaultTextColor
            : TextColorArgb | 0xFF000000,

        Alignment = Enum.IsDefined(Alignment) ? Alignment : DesktopLyricsAlignment.Center,
    };
}
