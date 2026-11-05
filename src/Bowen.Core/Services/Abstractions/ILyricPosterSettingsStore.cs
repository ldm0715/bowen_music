using Bowen.Core.Models;

namespace Bowen.Core.Services.Abstractions;

/// <summary>
/// 歌词海报偏好（字体、背景、文字颜色）的本地记忆。
/// </summary>
/// <remarks>
/// 与 <see cref="ILyricsSettingsStore"/> 一样是同步的：海报对话框打开时就要按上次的
/// 选择排水印，异步读会先闪一版默认样式。
/// </remarks>
public interface ILyricPosterSettingsStore
{
    /// <summary>读上次的选择。没有记录或记录不可用时返回 <see cref="LyricPosterSettings.Default"/>。</summary>
    LyricPosterSettings Load();

    /// <summary>写回当前选择。写失败只记日志，不抛。</summary>
    void Save(LyricPosterSettings settings);
}
