using Bodian.Core.Models;

namespace Bodian.Core.Services.Abstractions;

/// <summary>
/// 播放偏好（播放模式、「记住播放列表」）的读写。
/// </summary>
/// <remarks>
/// 与 <see cref="IAudioQualitySettingsStore"/> 分开：音质那一项已经单独落盘，
/// 两个互不相干的小偏好共用一个文件只会让读写互相牵制。
/// </remarks>
public interface IPlaybackSettingsStore
{
    PlaybackSettings Load();

    void Save(PlaybackSettings settings);
}
