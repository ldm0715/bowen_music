using Bodian.Core.Models;

namespace Bodian.Core.Services.Abstractions;

/// <summary>
/// 显示方式偏好的本地记忆。
/// </summary>
/// <remarks>
/// <b>接口是同步的</b>，与 <see cref="IThemeSettingsStore"/>、<see cref="ILyricsSettingsStore"/>
/// 同一个理由：页面要在第一帧就按用户的选择排版，异步读会先铺一遍行列表、再翻成卡片，
/// 那一下闪动比「晚一点读出来」更显眼。文件只有一个字段，读一次不到 1 毫秒。
/// </remarks>
public interface IViewModeSettingsStore
{
    /// <summary>读上次的选择。没有记录或记录不可用时返回 <see cref="ViewModeSettings.Default"/>。</summary>
    ViewModeSettings Load();

    /// <summary>写回当前选择。写失败只记日志，不抛。</summary>
    void Save(ViewModeSettings settings);
}
