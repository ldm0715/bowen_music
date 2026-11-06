namespace Bowen.Core.Services;

/// <summary>
/// 旧版 18 份设置文件的文件名。
/// </summary>
/// <remarks>
/// 名字从 <see cref="AppPaths"/> 取，不在这里另写一份字符串 —— 两处各写一遍迟早会漂移。
/// <see cref="All"/> 同时用于迁移读取与归档：只搬这 18 份，凭据、账号级数据、缓存一概不碰。
/// </remarks>
internal static class LegacySettingsCatalog
{
    public static string Theme { get; } = Name(AppPaths.SettingsFile);
    public static string Fonts { get; } = Name(AppPaths.FontSettingsFile);
    public static string ShellBackground { get; } = Name(AppPaths.ShellBackgroundSettingsFile);
    public static string Playback { get; } = Name(AppPaths.PlaybackSettingsFile);
    public static string Volume { get; } = Name(AppPaths.VolumeFile);
    public static string AudioQuality { get; } = Name(AppPaths.AudioQualityFile);
    public static string AudioEffects { get; } = Name(AppPaths.AudioEffectSettingsFile);
    public static string Lyrics { get; } = Name(AppPaths.LyricsSettingsFile);
    public static string DesktopLyrics { get; } = Name(AppPaths.DesktopLyricsSettingsFile);
    public static string LyricPoster { get; } = Name(AppPaths.LyricPosterSettingsFile);
    public static string ViewMode { get; } = Name(AppPaths.ViewModeFile);
    public static string Shortcuts { get; } = Name(AppPaths.ShortcutFile);
    public static string Update { get; } = Name(AppPaths.UpdatePreferencesFile);
    public static string Proxy { get; } = Name(AppPaths.ProxySettingsFile);
    public static string InputMethod { get; } = Name(AppPaths.InputMethodSettingsFile);
    public static string Window { get; } = Name(AppPaths.WindowFile);
    public static string DesktopLyricsWindow { get; } = Name(AppPaths.DesktopLyricsWindowFile);
    public static string MiniPlayerWindow { get; } = Name(AppPaths.MiniPlayerWindowFile);

    /// <summary>全部 18 份旧设置文件。</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Theme,
        Fonts,
        ShellBackground,
        Playback,
        Volume,
        AudioQuality,
        AudioEffects,
        Lyrics,
        DesktopLyrics,
        LyricPoster,
        ViewMode,
        Shortcuts,
        Update,
        Proxy,
        InputMethod,
        Window,
        DesktopLyricsWindow,
        MiniPlayerWindow,
    ];

    private static string Name(string path) => Path.GetFileName(path)!;
}
