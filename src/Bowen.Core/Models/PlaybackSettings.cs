namespace Bowen.Core.Models;

/// <summary>
/// 播放偏好：播放模式 + 「重启后恢复播放列表」。
/// </summary>
/// <remarks>
/// <b>这两项都跨账号共用</b>（「个人级」）。换个账号就得重设播放模式，或者当初为隐私关掉的
/// 「记住播放列表」又自己打开 —— 都会被当成 bug。所以它们待在 <c>playback.json</c> 里，
/// 而不是跟着按账号分开的播放队列走。
/// </remarks>
public sealed record PlaybackSettings
{
    /// <summary>播放模式。</summary>
    public PlayMode Mode { get; set; } = PlayMode.Sequential;

    /// <summary>重启后是否恢复上次的播放列表。</summary>
    public bool RestoreQueue { get; set; } = true;

    /// <summary>顺序播放 + 记住播放列表。</summary>
    public static PlaybackSettings Default { get; } = new();

    /// <summary>把未定义的播放模式收回顺序播放。</summary>
    public PlaybackSettings Normalized() => this with
    {
        Mode = Enum.IsDefined(Mode) ? Mode : PlayMode.Sequential,
    };

    /// <summary>保留旧的构造签名，供现有调用点使用。</summary>
    public PlaybackSettings()
    {
    }

    /// <summary>保留旧的构造签名，供现有调用点使用。</summary>
    public PlaybackSettings(PlayMode Mode, bool RestoreQueue)
    {
        this.Mode = Mode;
        this.RestoreQueue = RestoreQueue;
    }
}
