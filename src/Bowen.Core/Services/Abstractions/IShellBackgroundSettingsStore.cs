using Bowen.Core.Models;

namespace Bowen.Core.Services.Abstractions;

/// <summary>
/// 主窗口外壳背景偏好的本地持久化。
/// </summary>
/// <remarks>
/// <b>接口是同步的</b>，与主题、歌词那几份同一个理由：窗口建立第一帧之前就要知道
/// 该不该收起底色层、该不该挂 Mica，异步读会先闪一下错误的背板。
/// </remarks>
public interface IShellBackgroundSettingsStore
{
    /// <summary>读当前设置。没有记录或记录不可用时返回 <see cref="ShellBackgroundSettings.Default"/>。</summary>
    ShellBackgroundSettings Load();

    /// <summary>写回设置。写失败只记日志，不抛。</summary>
    void Save(ShellBackgroundSettings settings);
}
