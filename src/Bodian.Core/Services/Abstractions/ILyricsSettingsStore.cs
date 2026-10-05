using Bodian.Core.Models;

namespace Bodian.Core.Services.Abstractions;

/// <summary>
/// 歌词页偏好的本地记忆。
/// </summary>
/// <remarks>
/// <b>接口是同步的</b>，与 <see cref="IThemeSettingsStore"/>、<see cref="IDesktopLyricsSettingsStore"/>
/// 同一个理由：歌词页要在第一帧就按用户的选择排版，异步读会先闪一下「译文开着」再关掉。
/// </remarks>
public interface ILyricsSettingsStore
{
    /// <summary>读上次的选择。没有记录或记录不可用时返回 <see cref="LyricsSettings.Default"/>。</summary>
    LyricsSettings Load();

    /// <summary>写回当前选择。写失败只记日志，不抛。</summary>
    void Save(LyricsSettings settings);
}
