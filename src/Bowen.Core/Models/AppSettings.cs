namespace Bowen.Core.Models;

/// <summary>
/// 统一配置文件（<c>config.json</c>）的根对象。
/// </summary>
/// <remarks>
/// <para>
/// <b>所有属性都是 <c>get; set;</c>，不要改回 <c>init</c>。</b>
/// 源生成器对 <c>init</c> 属性走「有参构造」快路径，缺字段会被塞 CLR 默认值；
/// 只有可写属性才走无参构造，属性初始化器才会参与。见 <c>docs/settings-storage.md</c> §6。
/// </para>
/// <para>
/// <b>加设置项 = 往对应 section 加一个带默认值的属性。</b> 不需要新文件、新 store、
/// 新 JSON 上下文。只有 section 改名、字段搬家这类结构变化才升 <see cref="CurrentSchemaVersion"/>。
/// </para>
/// </remarks>
public sealed record AppSettings
{
    /// <summary>当前配置文件的结构版本。只加字段不升版本。</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>配置文件的结构版本。</summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>外观：主题、亚克力开关、启动页。</summary>
    public ThemeSettings Appearance { get; set; } = new();

    /// <summary>界面字体。</summary>
    public FontSettings Fonts { get; set; } = new();

    /// <summary>外壳背景。</summary>
    public ShellBackgroundSettings ShellBackground { get; set; } = new();

    /// <summary>播放模式与「记住播放列表」。</summary>
    public PlaybackSettings Playback { get; set; } = new();

    /// <summary>播放音量。</summary>
    public VolumeSettings Volume { get; set; } = new();

    /// <summary>默认音质。</summary>
    public AudioQualityPreferences AudioQuality { get; set; } = new();

    /// <summary>音效与环境音。</summary>
    public AudioEffectSettings AudioEffects { get; set; } = new();

    /// <summary>全屏歌词页与小窗歌词。</summary>
    public LyricsSettings Lyrics { get; set; } = new();

    /// <summary>桌面歌词条外观。</summary>
    public DesktopLyricsSettings DesktopLyrics { get; set; } = new();

    /// <summary>歌词海报。</summary>
    public LyricPosterSettings LyricPoster { get; set; } = new();

    /// <summary>列表用封面卡片还是行列表。</summary>
    public ViewModeSettings ViewMode { get; set; } = new();

    /// <summary>应用内快捷键。</summary>
    public ShortcutSettings Shortcuts { get; set; } = new();

    /// <summary>更新偏好。</summary>
    public UpdatePreferences Update { get; set; } = new();

    /// <summary>更新通道的代理。</summary>
    public ProxySettings Proxy { get; set; } = new();

    /// <summary>输入法兼容模式。</summary>
    public InputMethodSettings InputMethod { get; set; } = new();

    /// <summary>主窗口、桌面歌词条、小窗的位置与大小。</summary>
    public WindowSettings Windows { get; set; } = new();

    /// <summary>
    /// 默认配置。**每次返回新实例** —— 这些属性是可变对象，
    /// 共享一个静态实例会让一个 store 的修改污染另一个。
    /// </summary>
    public static AppSettings Default => new();

    /// <summary>
    /// 逐 section 收口：坏字段只影响自己，不整份退回默认。
    /// </summary>
    /// <remarks>
    /// JSON 里把整个 section 写成 <c>null</c> 时，属性会被赋成 <c>null</c>，
    /// 所以这里每个 section 都要先 <c>?? new()</c> 再规范化。
    /// </remarks>
    public AppSettings Normalized() => this with
    {
        SchemaVersion = SchemaVersion > 0 ? SchemaVersion : CurrentSchemaVersion,
        Appearance = (Appearance ?? new()).Normalized(),
        Fonts = (Fonts ?? new()).Normalized(),
        ShellBackground = (ShellBackground ?? new()).Normalized(),
        Playback = (Playback ?? new()).Normalized(),
        Volume = (Volume ?? new()).Normalized(),
        AudioQuality = (AudioQuality ?? new()).Normalized(),
        AudioEffects = (AudioEffects ?? new()).Normalized(),
        Lyrics = (Lyrics ?? new()).Normalized(),
        DesktopLyrics = (DesktopLyrics ?? new()).Normalized(),
        LyricPoster = (LyricPoster ?? new()).Normalized(),
        ViewMode = (ViewMode ?? new()).Normalized(),
        Shortcuts = (Shortcuts ?? new()).Normalized(),
        Update = Update ?? new(),
        Proxy = (Proxy ?? new()).WithKnownMode(),
        InputMethod = InputMethod ?? new(),
        Windows = (Windows ?? new()).Normalized(),
    };
}

/// <summary>默认音质偏好。</summary>
public sealed record AudioQualityPreferences
{
    /// <summary>设置页选的默认音质。当前曲目的实际档位不写回这里。</summary>
    public AudioQuality Preferred { get; set; } = AudioQuality.Lossless;

    /// <summary>把未定义的枚举值收回默认档位。</summary>
    public AudioQualityPreferences Normalized() => this with
    {
        Preferred = Enum.IsDefined(Preferred) ? Preferred : AudioQuality.Lossless,
    };
}

/// <summary>三份窗口几何。</summary>
public sealed record WindowSettings
{
    /// <summary>主窗口。只记普通状态，最大化不记。</summary>
    public WindowPlacement? Main { get; set; }

    /// <summary>桌面歌词条。</summary>
    public WindowPlacement? DesktopLyrics { get; set; }

    /// <summary>小窗。</summary>
    public WindowPlacement? MiniPlayer { get; set; }

    /// <summary>宽高非正的记录没法用来设窗口，按「没有记录」处理。位置为负是合法的（副屏）。</summary>
    public WindowSettings Normalized() => this with
    {
        Main = Usable(Main),
        DesktopLyrics = Usable(DesktopLyrics),
        MiniPlayer = Usable(MiniPlayer),
    };

    private static WindowPlacement? Usable(WindowPlacement? placement) =>
        placement is { Width: > 0, Height: > 0 } ? placement : null;
}
