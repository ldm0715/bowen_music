namespace Bowen.WinUI.Playback;

/// <summary>播放引擎的状态。</summary>
public enum PlaybackState
{
    /// <summary>没有加载任何东西。</summary>
    Idle = 0,

    /// <summary>正在打开/缓冲。</summary>
    Loading = 1,

    Playing = 2,

    Paused = 3,

    /// <summary>已停止，可以重新加载。</summary>
    Stopped = 4,
}

/// <summary>
/// 要播放的东西。
/// </summary>
/// <remarks>
/// <see cref="Start"/> / <see cref="End"/> 就是试听窗口。引擎不知道「试听」这个概念，
/// 它只认识一个播放区间 —— 试听的语义在 <c>Bowen.Core.Models.PlaybackPolicy</c> 里。
/// </remarks>
/// <param name="StreamUrl">音频直链。</param>
/// <param name="Start">起始位置；为空则从头播。</param>
/// <param name="End">结束位置；为空则播到文件结尾。</param>
/// <param name="Title">用于日志与界面显示。</param>
public sealed record PlaybackSource(Uri StreamUrl, TimeSpan? Start = null, TimeSpan? End = null, string? Title = null, bool InitiallyPaused = false);

/// <summary>播放状态变化。</summary>
public sealed class PlaybackStateChangedEventArgs(PlaybackState state) : EventArgs
{
    public PlaybackState State { get; } = state;
}

/// <summary>进度变化。</summary>
public sealed class PlaybackPositionChangedEventArgs(TimeSpan position, TimeSpan duration) : EventArgs
{
    public TimeSpan Position { get; } = position;

    public TimeSpan Duration { get; } = duration;
}

/// <summary>引擎出错。</summary>
public sealed class PlaybackFailedEventArgs(string message) : EventArgs
{
    public string Message { get; } = message;
}

/// <summary>
/// 跳转请求已发出、音频还没接上。
/// </summary>
/// <remarks>
/// 带上 <see cref="Target"/> 是因为 mpv 在 seek 完成前就会把 <c>time-pos</c> 报成目标值：
/// 订阅方只有知道目标，才能区分「位置跳到了目标」和「声音真的开始前进了」。
/// </remarks>
public sealed class SeekPendingEventArgs(TimeSpan target) : EventArgs
{
    public TimeSpan Target { get; } = target;
}
