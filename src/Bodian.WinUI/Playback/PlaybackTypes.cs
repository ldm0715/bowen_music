namespace Bodian.WinUI.Playback;

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
/// 它只认识一个播放区间 —— 试听的语义在 <c>Bodian.Core.Models.PlaybackPolicy</c> 里。
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
