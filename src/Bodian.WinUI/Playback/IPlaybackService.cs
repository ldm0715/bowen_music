namespace Bodian.WinUI.Playback;

/// <summary>
/// 音频播放引擎。
/// </summary>
/// <remarks>
/// <para>
/// 这是**哑引擎**：它不认识「试听」「无权限」「降级」这些领域概念，只知道播放一个地址的一段区间。
/// 所有领域判断都在 <c>Bodian.Core</c> 与协调器里，这样换后端（libmpv → 别的）时上层零改动。
/// </para>
/// <para>
/// 原生库缺失或初始化失败时**不抛异常**，而是 <see cref="IsAvailable"/> 为 <c>false</c> 并给出
/// <see cref="UnavailableReason"/> —— 界面显示一条提示即可，不该因为缺个 dll 就崩溃。
/// </para>
/// </remarks>
public interface IPlaybackService : IAsyncDisposable
{
    /// <summary>原生库是否可用。首次播放时才真正尝试加载。</summary>
    bool IsAvailable { get; }

    /// <summary>不可用的原因，供界面显示。可用时为 <c>null</c>。</summary>
    string? UnavailableReason { get; }

    PlaybackState State { get; }

    TimeSpan Position { get; }

    TimeSpan Duration { get; }

    /// <summary>音量，0 到 100。</summary>
    double Volume { get; }

    event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;

    /// <summary>进度变化。**在 UI 线程上触发。**</summary>
    event EventHandler<PlaybackPositionChangedEventArgs>? PositionChanged;

    /// <summary>整段播完（到文件结尾，或到了 <see cref="PlaybackSource.End"/>）。</summary>
    event EventHandler? Ended;

    event EventHandler<PlaybackFailedEventArgs>? Failed;

    /// <summary>加载成功后才返回；失败抛异常。InitiallyPaused 指定初始暂停状态。</summary>
    Task LoadAsync(PlaybackSource source, CancellationToken cancellationToken = default);

    Task PlayAsync(CancellationToken cancellationToken = default);

    Task PauseAsync(CancellationToken cancellationToken = default);

    Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>设置音量，0 到 100。</summary>
    void SetVolume(double volume);
}
