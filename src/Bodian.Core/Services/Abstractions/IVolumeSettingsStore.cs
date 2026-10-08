using Bodian.Core.Models;

namespace Bodian.Core.Services.Abstractions;

/// <summary>播放音量偏好的读写。保存失败时返回 false。</summary>
public interface IVolumeSettingsStore
{
    /// <returns>读出来的值已收进 [0,100]；读不出来时是 <see cref="VolumeSettings.Default"/>。</returns>
    VolumeSettings Load();

    bool Save(VolumeSettings settings);
}
