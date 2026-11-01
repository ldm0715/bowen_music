using Bowen.Core.Models;

namespace Bowen.Core.Services.Abstractions;

/// <summary>音频效果偏好的读写。保存失败时返回 false。</summary>
public interface IAudioEffectSettingsStore
{
    /// <returns>读出来的值已归一化；读不出来时是 <see cref="AudioEffectSettings.Default"/>。</returns>
    AudioEffectSettings Load();

    bool Save(AudioEffectSettings settings);
}
