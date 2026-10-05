using Bodian.Core.Models;

namespace Bodian.Core.Services.Abstractions;

/// <summary>
/// 桌面歌词条外观偏好的本地记忆。
/// </summary>
/// <remarks>
/// <b>接口是同步的</b>，与 <see cref="IThemeSettingsStore"/> 同一个理由：
/// 歌词条要在显示出来之前就按用户选的字号与颜色摆好，异步读会先闪一下默认观感。
/// </remarks>
public interface IDesktopLyricsSettingsStore
{
    /// <summary>读上次的选择。没有记录或记录不可用时返回 <see cref="DesktopLyricsSettings.Default"/>。</summary>
    DesktopLyricsSettings Load();

    /// <summary>写回当前选择。写失败只记日志，不抛。</summary>
    void Save(DesktopLyricsSettings settings);
}
