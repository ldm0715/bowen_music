namespace Bodian.Core.Models;

/// <summary>
/// 播放偏好：播放模式 + 「重启后恢复播放列表」。
/// </summary>
/// <remarks>
/// <b>这两项都跨账号共用</b>（「个人级」）。换个账号就得重设播放模式，或者当初为隐私关掉的
/// 「记住播放列表」又自己打开 —— 都会被当成 bug。所以它们待在 <c>playback.json</c> 里，
/// 而不是跟着按账号分开的播放队列走。
/// </remarks>
public sealed record PlaybackSettings(PlayMode Mode, bool RestoreQueue)
{
    /// <summary>顺序播放 + 记住播放列表。</summary>
    public static PlaybackSettings Default { get; } = new(PlayMode.Sequential, true);
}
