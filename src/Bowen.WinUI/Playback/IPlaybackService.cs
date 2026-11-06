namespace Bowen.WinUI.Playback;

/// <summary>
/// 音频播放引擎。
/// </summary>
/// <remarks>
/// <para>
/// 这是**哑引擎**：它不认识「试听」「无权限」「降级」这些领域概念，只知道播放一个地址的一段区间。
/// 所有领域判断都在 <c>Bowen.Core</c> 与协调器里，这样换后端（libmpv → 别的）时上层零改动。
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

    /// <summary>
    /// 跳转请求已发出、音频还没接上。
    /// </summary>
    /// <remarks>
    /// <b>mpv 在 seek 完成前就会把 <c>time-pos</c> 报成目标位置</b>，只看进度事件的话，
    /// 歌词会立刻从新位置按时钟外推，而声音还要等重新拉流 —— 表现是「歌词先滚、没声音」。
    /// 订阅方收到这个事件后应当先停住时钟，等 <see cref="PositionChanged"/> 真的报出前进再恢复。
    /// </remarks>
    event EventHandler<SeekPendingEventArgs>? SeekPending;

    /// <summary>整段播完（到文件结尾，或到了 <see cref="PlaybackSource.End"/>）。</summary>
    event EventHandler? Ended;

    /// <summary>
    /// 拉流停住 / 恢复。
    /// </summary>
    /// <remarks>
    /// <b>与 <see cref="State"/> 分开</b>：引擎仍然报 <see cref="PlaybackState.Playing"/> ——
    /// 它并没有暂停，只是数据拉不下来，声音和进度一起停住。界面收到 <c>true</c> 后应当挂一条
    /// 不自动消失的提示并停住歌词时钟，收到 <c>false</c>（或新的 <see cref="PositionChanged"/>）再撤掉。
    /// </remarks>
    event EventHandler<PlaybackStalledEventArgs>? Stalled;

    event EventHandler<PlaybackFailedEventArgs>? Failed;

    /// <summary>加载成功后才返回；失败抛异常。InitiallyPaused 指定初始暂停状态。</summary>
    Task LoadAsync(PlaybackSource source, CancellationToken cancellationToken = default);

    Task PlayAsync(CancellationToken cancellationToken = default);

    Task PauseAsync(CancellationToken cancellationToken = default);

    Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 取消正在进行的断线重连，<b>不打断正常播放</b>。
    /// </summary>
    /// <remarks>
    /// <b>用户要换歌时先调这个。</b> 解析新音源可能要等 API，这期间不能让旧歌自己重连回来 ——
    /// 否则会出现「点了新歌，界面是新歌，耳朵里还是旧歌」。
    /// </remarks>
    void CancelRecovery();

    /// <summary>设置音量，0 到 100。</summary>
    void SetVolume(double volume);

    /// <summary>
    /// 设置 mpv 的 <c>af</c> 滤镜链；<c>null</c> 或空串表示清空。
    /// </summary>
    /// <remarks>
    /// <b>这是整条链，不是追加。</b> 调用方每次都要给完整值；写不存在的滤镜会让 mpv
    /// 拒绝整串，所以构建方必须保证当前 libmpv 真的编进了那些滤镜。
    /// </remarks>
    void SetAudioFilter(string? filter);

    /// <summary>
    /// 向带标签的音频滤镜发 <c>af-command</c>，用于不重建整条链地调参数。
    /// </summary>
    /// <param name="label"><c>af</c> 里的 <c>@label</c>。</param>
    /// <param name="command">滤镜命令，例如 <c>amix</c> 的 <c>weights</c>。</param>
    /// <param name="argument">命令参数。</param>
    /// <param name="target">lavfi 图里的目标滤镜名，例如 <c>amix</c>。</param>
    /// <remarks>引擎未初始化或滤镜不在链上时静默忽略，不打断播放。</remarks>
    void SendAudioFilterCommand(string label, string command, string argument, string? target = null);
}
