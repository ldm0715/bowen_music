namespace Bowen.Core.Models;

/// <summary>全屏歌词的横向对齐方式。</summary>
public enum LyricsAlignment
{
    /// <summary>居左。</summary>
    Left = 0,

    /// <summary>居中。</summary>
    Center = 1,

    /// <summary>居右。</summary>
    Right = 2,
}

/// <summary>全屏歌词页的背景来源。</summary>
public enum LyricsBackgroundMode
{
    /// <summary>当前封面的模糊背景。</summary>
    CoverBlur = 0,

    /// <summary>系统 Mica 材质，仅 Windows 11 支持。</summary>
    Mica = 1,

    /// <summary>自定义图片。尚未实现，仅占位。</summary>
    CustomImage = 2,

    /// <summary>纯色。尚未实现，仅占位。</summary>
    SolidColor = 3,
}

/// <summary>
/// 歌词相关界面的偏好：全屏歌词页与小窗歌词。
/// </summary>
/// <remarks>
/// <para>
/// <b>与 <see cref="DesktopLyricsSettings"/> 分开一份文件</b>：那份管桌面歌词条的外观，
/// 这份管全屏歌词页显示什么 —— 不同界面的偏好挤进一个文件，只会让读写互相牵制。
/// </para>
/// <para>
/// <b>译文不是单独一条轨。</b> 外文歌那份歌词内容里原文与译文成对出现，解析时就分好了
/// （<see cref="Lyrics.LyricLine.IsTranslation"/>）。这里的开关只决定要不要把它们
/// 从文档里剔掉，<b>不产生任何网络请求</b>。
/// </para>
/// </remarks>
public sealed record LyricsSettings
{
    /// <summary>
    /// 歌词页是否显示译文。
    /// </summary>
    /// <remarks>
    /// <b>默认开。</b> 译文本来就在歌词内容里，客户端此前一直显示着；
    /// 这个开关的意义是给用户一个关掉它的办法，不是新增一种显示。
    /// </remarks>
    public bool ShowTranslation { get; init; } = true;

    /// <summary>
    /// 全屏歌词的字体族名；空串表示跟随系统默认。
    /// </summary>
    /// <remarks>
    /// <b>只存名字，不校验是否存在。</b> 缺字体时渲染层回退系统默认，与界面字体同一条规矩。
    /// 改它即时生效（重排当前文档），不需要重启。
    /// </remarks>
    public string FontFamily { get; init; } = string.Empty;

    /// <summary>
    /// 小窗歌词的字体族名；空串表示跟随界面字体。
    /// </summary>
    /// <remarks>
    /// 小窗那一行歌词是 XAML TextBlock，默认吃全局界面字体；这里单独存一份，
    /// 选了字体就覆盖它。只存名字，缺字体时窗口回退系统默认。
    /// </remarks>
    public string MiniPlayerFontFamily { get; init; } = string.Empty;

    /// <summary>全屏歌词字号下限（DIP）。</summary>
    public const double MinimumFontSize = 24;

    /// <summary>全屏歌词字号上限（DIP）。</summary>
    public const double MaximumFontSize = 80;

    /// <summary>全屏歌词默认字号（DIP）。</summary>
    public const double DefaultFontSize = 48;

    /// <summary>行距下限（DIP）。0 表示只保留文本自身行高。</summary>
    public const double MinimumLineGap = 0;

    /// <summary>行距上限（DIP）。</summary>
    public const double MaximumLineGap = 64;

    /// <summary>默认行距（DIP）。</summary>
    public const double DefaultLineGap = 24;

    /// <summary>译文相对歌词字号的缩放下限。</summary>
    public const double MinimumTranslationFontScale = 0.5;

    /// <summary>译文相对歌词字号的缩放上限。</summary>
    public const double MaximumTranslationFontScale = 1;

    /// <summary>译文默认按歌词字号的 72% 排版。</summary>
    public const double DefaultTranslationFontScale = 0.72;

    /// <summary>译文与原文之间的间距下限（DIP）。</summary>
    public const double MinimumTranslationLineGap = 0;

    /// <summary>译文与原文之间的间距上限（DIP）。</summary>
    public const double MaximumTranslationLineGap = 32;

    /// <summary>译文与原文之间的默认间距（DIP）。</summary>
    public const double DefaultTranslationLineGap = 6;

    /// <summary>视口边缘模糊范围下限，占视口高度的比例；0 表示关闭边缘模糊。</summary>
    public const double MinimumEdgeBlurRatio = 0;

    /// <summary>视口边缘模糊范围上限，占视口高度的比例。</summary>
    public const double MaximumEdgeBlurRatio = 0.5;

    /// <summary>默认让视口上下各 20% 的范围逐渐模糊。</summary>
    public const double DefaultEdgeBlurRatio = 0.2;

    /// <summary>自定义背景图片的暗度下限。</summary>
    public const double MinimumBackgroundDim = 0;

    /// <summary>自定义背景图片的暗度上限。</summary>
    public const double MaximumBackgroundDim = 0.8;

    /// <summary>自定义背景图片的默认暗度。</summary>
    public const double DefaultBackgroundDim = 0.6;

    /// <summary>自定义背景图片的模糊下限（DIP）。</summary>
    public const double MinimumBackgroundBlur = 0;

    /// <summary>自定义背景图片的模糊上限（DIP）。</summary>
    public const double MaximumBackgroundBlur = 40;

    /// <summary>自定义背景图片的默认模糊（DIP）。</summary>
    public const double DefaultBackgroundBlur = 8;

    /// <summary>水平偏移下限，占歌词区域宽度的百分比。</summary>
    public const double MinimumHorizontalOffsetPercent = -30;

    /// <summary>水平偏移上限，占歌词区域宽度的百分比。</summary>
    public const double MaximumHorizontalOffsetPercent = 30;

    /// <summary>垂直偏移下限，占歌词区域高度的百分比。</summary>
    public const double MinimumVerticalOffsetPercent = -25;

    /// <summary>垂直偏移上限，占歌词区域高度的百分比。</summary>
    public const double MaximumVerticalOffsetPercent = 25;

    /// <summary>默认位置偏移百分比。</summary>
    public const double DefaultOffsetPercent = 0;

    /// <summary>默认高亮色：不透明白色。</summary>
    public const uint DefaultHighlightColor = 0xFFFFFFFF;

    /// <summary>默认纯色背景：深蓝，够暗且能看出颜色。</summary>
    public const uint DefaultBackgroundColor = 0xFF24324A;

    /// <summary>
    /// 纯色背景的预设。压到低亮度保证白色歌词可读，但保留明显的色相差异。
    /// </summary>
    public static readonly (uint Argb, string Name)[] BackgroundPalette =
    [
        (0xFF24324A, "深蓝"), (0xFF2B2B31, "石墨"), (0xFF1F3B33, "墨绿"), (0xFF3A2E1C, "琥珀棕"),
        (0xFF3A2230, "梅红"), (0xFF2C2542, "深紫"), (0xFF1E3A44, "青灰"), (0xFF33302A, "暖灰"),
    ];

    /// <summary>
    /// 可选的高亮色。与桌面歌词一样只给基础色，不做色轮。
    /// </summary>
    /// <remarks>
    /// 默认白色放在首位：未唱部分固定白色半透明，高亮保持白色时两者只差透明度。
    /// </remarks>
    public static readonly (uint Argb, string Name)[] HighlightPalette =
    [
        (0xFFFFFFFF, "白色"), (0xFF00E5BF, "青绿"), (0xFF4D9CFF, "蓝色"),
        (0xFFFF6FB5, "粉色"), (0xFFFF922B, "橙色"), (0xFFFADB14, "黄色"),
        (0xFF37D67A, "绿色"), (0xFFFF4D4F, "红色"),
    ];

    /// <summary>全屏歌词字号，DIP。</summary>
    public double FontSize { get; init; } = DefaultFontSize;

    /// <summary>全屏歌词的横向对齐方式。</summary>
    public LyricsAlignment Alignment { get; init; } = LyricsAlignment.Left;

    /// <summary>已唱部分的高亮色，<c>0xAARRGGBB</c>；未唱部分固定白色半透明。</summary>
    public uint HighlightColorArgb { get; init; } = DefaultHighlightColor;

    /// <summary>行与行之间的额外间距，DIP；0 表示只保留文本自身行高。</summary>
    public double LineGap { get; init; } = DefaultLineGap;

    /// <summary>
    /// 译文是否跟随原文同步扫色。默认开。
    /// </summary>
    /// <remarks>
    /// 关掉后译文仍按原文下方显示，但不再跟随演唱进度染色，保持未播放行的暗色。
    /// </remarks>
    public bool SyncTranslationHighlight { get; init; } = true;

    /// <summary>译文相对歌词字号的缩放，0.5–1；默认 0.72。</summary>
    public double TranslationFontScale { get; init; } = DefaultTranslationFontScale;

    /// <summary>译文与原文之间的间距，DIP；默认 6。</summary>
    public double TranslationLineGap { get; init; } = DefaultTranslationLineGap;

    /// <summary>
    /// 视口边缘模糊范围，占视口高度的比例，0–0.5；默认 0.2。
    /// </summary>
    /// <remarks>
    /// 歌词在视口中部保持清晰，越靠近上下边缘越模糊；0 表示不按边缘模糊。
    /// </remarks>
    public double EdgeBlurRatio { get; init; } = DefaultEdgeBlurRatio;

    /// <summary>全屏歌词页的背景来源；默认封面模糊。</summary>
    public LyricsBackgroundMode BackgroundMode { get; init; } = LyricsBackgroundMode.CoverBlur;

    /// <summary>
    /// 自定义背景图片的文件名，位于应用数据目录的 <c>backgrounds</c> 下。
    /// </summary>
    /// <remarks>只存文件名，不存绝对路径；换机器或改目录时不会失效。</remarks>
    public string BackgroundImageFile { get; init; } = string.Empty;

    /// <summary>自定义背景图片上叠加的黑色暗度，0–0.8；默认 0.6。</summary>
    public double BackgroundImageDim { get; init; } = DefaultBackgroundDim;

    /// <summary>自定义背景图片的模糊量，DIP；默认 8。</summary>
    public double BackgroundImageBlur { get; init; } = DefaultBackgroundBlur;

    /// <summary>自定义背景图片的水平裁剪中心，0–1；0.5 为居中。</summary>
    public double BackgroundImageFocusX { get; init; } = 0.5;

    /// <summary>自定义背景图片的垂直裁剪中心，0–1；0.5 为居中。</summary>
    public double BackgroundImageFocusY { get; init; } = 0.5;

    /// <summary>自定义背景图片在 cover 基础上的缩放，1–3；1 表示刚好铺满。</summary>
    public double BackgroundImageZoom { get; init; } = 1;

    /// <summary>纯色背景的颜色，<c>0xAARRGGBB</c>。</summary>
    public uint BackgroundColorArgb { get; init; } = DefaultBackgroundColor;

    /// <summary>自定义取色最近一次选的颜色，供预设行末尾的「自定义」圆点使用。</summary>
    public uint BackgroundCustomColorArgb { get; init; } = DefaultBackgroundColor;

    /// <summary>纯色背景是否跟随当前封面自动取色。</summary>
    public bool BackgroundColorAuto { get; init; }

    /// <summary>
    /// 水平偏移，占歌词区域宽度的百分比；在对齐结果之上继续左右移动。
    /// </summary>
    public double HorizontalOffsetPercent { get; init; } = DefaultOffsetPercent;

    /// <summary>
    /// 垂直偏移，占歌词区域高度的百分比；正数向下移动。
    /// </summary>
    public double VerticalOffsetPercent { get; init; } = DefaultOffsetPercent;

    /// <summary>默认设置：显示译文、跟随系统字体、白色高亮。</summary>
    public static LyricsSettings Default { get; } = new();

    /// <summary>规范化字体名与数值范围。</summary>
    /// <remarks>
    /// 逐项夹取，不整份退回默认：某一项写坏了，不该把字体、对齐、颜色一起抹掉。
    /// 保留这个入口与其他设置同一个形状：新增需要夹取范围的偏好时不必改 store 的调用点。
    /// </remarks>
    public LyricsSettings Normalized() => this with
    {
        FontFamily = FontFamily?.Trim() ?? string.Empty,
        MiniPlayerFontFamily = MiniPlayerFontFamily?.Trim() ?? string.Empty,

        // 0 不是合法的字号，只可能来自旧文件缺字段，按默认处理。
        FontSize = double.IsFinite(FontSize) && FontSize > 0
            ? Math.Clamp(FontSize, MinimumFontSize, MaximumFontSize)
            : DefaultFontSize,

        LineGap = double.IsFinite(LineGap)
            ? Math.Clamp(LineGap, MinimumLineGap, MaximumLineGap)
            : DefaultLineGap,

        TranslationFontScale = double.IsFinite(TranslationFontScale) && TranslationFontScale > 0
            ? Math.Clamp(TranslationFontScale, MinimumTranslationFontScale, MaximumTranslationFontScale)
            : DefaultTranslationFontScale,

        TranslationLineGap = double.IsFinite(TranslationLineGap)
            ? Math.Clamp(TranslationLineGap, MinimumTranslationLineGap, MaximumTranslationLineGap)
            : DefaultTranslationLineGap,

        EdgeBlurRatio = double.IsFinite(EdgeBlurRatio)
            ? Math.Clamp(EdgeBlurRatio, MinimumEdgeBlurRatio, MaximumEdgeBlurRatio)
            : DefaultEdgeBlurRatio,

        // 0 不是合法的高亮色；其余颜色把 alpha 顶回不透明（色板不提供透明度）。
        HighlightColorArgb = HighlightColorArgb == 0
            ? DefaultHighlightColor
            : HighlightColorArgb | 0xFF000000,

        HorizontalOffsetPercent = double.IsFinite(HorizontalOffsetPercent)
            ? Math.Clamp(HorizontalOffsetPercent, MinimumHorizontalOffsetPercent, MaximumHorizontalOffsetPercent)
            : DefaultOffsetPercent,

        VerticalOffsetPercent = double.IsFinite(VerticalOffsetPercent)
            ? Math.Clamp(VerticalOffsetPercent, MinimumVerticalOffsetPercent, MaximumVerticalOffsetPercent)
            : DefaultOffsetPercent,

        Alignment = Enum.IsDefined(Alignment) ? Alignment : LyricsAlignment.Left,
        BackgroundMode = Enum.IsDefined(BackgroundMode) ? BackgroundMode : LyricsBackgroundMode.CoverBlur,

        // 只保留文件名，拒绝目录穿越；空串表示还没选图片。
        BackgroundImageFile = string.IsNullOrWhiteSpace(BackgroundImageFile)
            ? string.Empty
            : Path.GetFileName(BackgroundImageFile.Trim()),

        BackgroundImageDim = double.IsFinite(BackgroundImageDim)
            ? Math.Clamp(BackgroundImageDim, MinimumBackgroundDim, MaximumBackgroundDim)
            : DefaultBackgroundDim,

        BackgroundImageBlur = double.IsFinite(BackgroundImageBlur)
            ? Math.Clamp(BackgroundImageBlur, MinimumBackgroundBlur, MaximumBackgroundBlur)
            : DefaultBackgroundBlur,

        BackgroundImageFocusX = double.IsFinite(BackgroundImageFocusX)
            ? Math.Clamp(BackgroundImageFocusX, 0, 1)
            : 0.5,

        BackgroundImageFocusY = double.IsFinite(BackgroundImageFocusY)
            ? Math.Clamp(BackgroundImageFocusY, 0, 1)
            : 0.5,

        BackgroundImageZoom = double.IsFinite(BackgroundImageZoom)
            ? Math.Clamp(BackgroundImageZoom, 1, 3)
            : 1,

        // 0 不是合法颜色；纯色背景不提供透明度，统一顶回不透明。
        BackgroundColorArgb = BackgroundColorArgb == 0
            ? DefaultBackgroundColor
            : BackgroundColorArgb | 0xFF000000,

        BackgroundCustomColorArgb = BackgroundCustomColorArgb == 0
            ? DefaultBackgroundColor
            : BackgroundCustomColorArgb | 0xFF000000,
    };
}
