using Bodian.Core.Models;

namespace Bodian.Core.Services.Abstractions;

/// <summary>
/// 窗口位置与大小的本地记忆。
/// </summary>
/// <remarks>
/// <b>接口是同步的</b>，与 <see cref="IThemeSettingsStore"/> 同一个理由：
/// 窗口必须在显示出来之前就摆好，否则会先闪一下默认位置再跳过去。
/// </remarks>
public interface IWindowPlacementStore
{
    /// <summary>
    /// 读上次的位置。<b>没有记录、或记录不可用时返回 <c>null</c></b>，
    /// 由调用方回落到默认尺寸 + 居中。
    /// </summary>
    WindowPlacement? Load();

    /// <summary>
    /// 写回当前位置。写失败只记日志，不抛 —— 下次启动回落到默认值而已。
    /// </summary>
    void Save(WindowPlacement placement);
}
