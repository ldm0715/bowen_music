using Bodian.Core.Models;

namespace Bodian.Core.Services.Abstractions;

/// <summary>
/// 快捷键键位的本地记忆。
/// </summary>
/// <remarks>
/// <b>接口是同步的</b>，与其余几份设置同一个理由：加速器要在窗口建起来之前就装好，
/// 异步读会让头几百毫秒里按键没反应。文件很小，读一次不到 1 毫秒。
/// </remarks>
public interface IShortcutSettingsStore
{
    /// <summary>读上次的键位。没有记录或记录不可用时返回 <see cref="ShortcutSettings.Default"/>。</summary>
    ShortcutSettings Load();

    /// <summary>写回键位。写失败只记日志，不抛。</summary>
    void Save(ShortcutSettings settings);
}
