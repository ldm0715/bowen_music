namespace Bodian.Core.Models;

/// <summary>
/// 播放音量偏好。
/// </summary>
/// <remarks>
/// <para>
/// <b>只管音乐播放那一套音量（0–100），不管 MV。</b> MV 走系统媒体播放器、标度是 0–1，
/// 且 MV 页的视图模型每次进页都是新实例，今天本来就不记 —— 把它并进来会变成一个新的行为变化，
/// 而不是「持久化」。
/// </para>
/// <para>
/// <b>静音刻意不在这里。</b> 静音是会话级标记：落盘会造出「上次静音退出、这次启动没声音，
/// 用户以为应用坏了」这一后果，多数播放器也不记。
/// </para>
/// </remarks>
public sealed record VolumeSettings(double Level = 100)
{
    /// <summary>音量下限。</summary>
    public const double MinimumLevel = 0;

    /// <summary>音量上限。</summary>
    public const double MaximumLevel = 100;

    /// <summary>默认音量。</summary>
    public const double DefaultLevel = 100;

    /// <summary>默认设置。</summary>
    public static VolumeSettings Default { get; } = new();

    /// <summary>
    /// 把越界的值收进 [0,100]。
    /// </summary>
    /// <remarks>
    /// <b>逐项夹，不整份退回默认</b>：坏值（NaN、无穷）只影响音量本身，没有别的字段可连累。
    /// 与桌面歌词设置的收口方式同款。
    /// </remarks>
    public VolumeSettings Normalized() => this with
    {
        Level = double.IsFinite(Level) ? Math.Clamp(Level, MinimumLevel, MaximumLevel) : DefaultLevel,
    };
}
