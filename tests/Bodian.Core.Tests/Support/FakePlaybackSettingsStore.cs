using Bodian.Core.Models;
using Bodian.Core.Services.Abstractions;

namespace Bodian.Core.Tests.Support;

/// <summary>
/// 内存版播放偏好仓库。
/// </summary>
/// <remarks>
/// <see cref="Mode"/> 与 <see cref="RestoreQueue"/> 是 <see cref="Stored"/> 的两个字段的便捷入口，
/// 免得到处写 <c>Stored with { ... }</c>。
/// </remarks>
internal sealed class FakePlaybackSettingsStore : IPlaybackSettingsStore
{
    public PlaybackSettings Stored { get; set; } = PlaybackSettings.Default;

    public PlayMode Mode
    {
        get => Stored.Mode;
        set => Stored = Stored with { Mode = value };
    }

    public bool RestoreQueue
    {
        get => Stored.RestoreQueue;
        set => Stored = Stored with { RestoreQueue = value };
    }

    public PlaybackSettings Load() => Stored;

    public void Save(PlaybackSettings settings) => Stored = settings;
}
