using Bowen.Core.Models;
using Bowen.Core.Services.Implementations;

namespace Bowen.Core.Services;

/// <summary>
/// 用旧版 store 的 <c>Load()</c> 读 18 份旧设置文件。
/// </summary>
/// <remarks>
/// <para>
/// <b>刻意复用现有 <c>Load()</c>，不另写一套解析。</b> 旧文件里有几处历史格式
/// （<c>playback.json</c> 的裸枚举、<c>lyrics.json</c> 缺外观字段、音质裸枚举），
/// 这些逻辑已经各自写在 store 里；迁移再抄一遍，两套迟早会漂移。
/// </para>
/// <para>
/// 文件不存在时对应的 store 返回默认值，所以缺失的旧文件自然落到默认 section，
/// 不需要调用方逐份判断。
/// </para>
/// </remarks>
internal static class LegacySettingsReader
{
    /// <summary>读取全部旧设置。没有的项用默认值。</summary>
    public static AppSettings Read(string rootDirectory)
    {
        string PathOf(string fileName) => Path.Combine(rootDirectory, fileName);

        var settings = new AppSettings
        {
            Appearance = new JsonThemeSettingsStore(
                PathOf(LegacySettingsCatalog.Theme)).Load(),
            Fonts = new JsonFontSettingsStore(
                PathOf(LegacySettingsCatalog.Fonts)).Load(),
            ShellBackground = new JsonShellBackgroundSettingsStore(
                PathOf(LegacySettingsCatalog.ShellBackground)).Load(),
            Playback = new JsonPlaybackSettingsStore(
                PathOf(LegacySettingsCatalog.Playback)).Load(),
            Volume = new JsonVolumeSettingsStore(
                PathOf(LegacySettingsCatalog.Volume)).Load(),
            AudioQuality = new AudioQualityPreferences
            {
                Preferred = new JsonAudioQualitySettingsStore(
                    PathOf(LegacySettingsCatalog.AudioQuality)).Load(),
            },
            AudioEffects = new JsonAudioEffectSettingsStore(
                PathOf(LegacySettingsCatalog.AudioEffects)).Load(),
            Lyrics = new JsonLyricsSettingsStore(
                PathOf(LegacySettingsCatalog.Lyrics)).Load(),
            DesktopLyrics = new JsonDesktopLyricsSettingsStore(
                PathOf(LegacySettingsCatalog.DesktopLyrics)).Load(),
            LyricPoster = new JsonLyricPosterSettingsStore(
                PathOf(LegacySettingsCatalog.LyricPoster)).Load(),
            ViewMode = new JsonViewModeSettingsStore(
                PathOf(LegacySettingsCatalog.ViewMode)).Load(),
            Shortcuts = new JsonShortcutSettingsStore(
                PathOf(LegacySettingsCatalog.Shortcuts)).Load(),
            Update = new JsonUpdatePreferencesStore(
                PathOf(LegacySettingsCatalog.Update)).Load(),
            Proxy = new JsonProxySettingsStore(
                PathOf(LegacySettingsCatalog.Proxy)).Load(),
            InputMethod = new JsonInputMethodSettingsStore(
                PathOf(LegacySettingsCatalog.InputMethod)).Load(),
            Windows = new WindowSettings
            {
                Main = new JsonWindowPlacementStore(
                    PathOf(LegacySettingsCatalog.Window)).Load(),
                DesktopLyrics = new DesktopLyricsPlacementStore(
                    PathOf(LegacySettingsCatalog.DesktopLyricsWindow)).Load(),
                MiniPlayer = new MiniPlayerPlacementStore(
                    PathOf(LegacySettingsCatalog.MiniPlayerWindow)).Load(),
            },
        };

        return settings.Normalized();
    }
}
