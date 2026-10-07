namespace Bodian.Core.Models;

/// <summary>
/// 一个可以被快捷键触发的动作。
/// </summary>
public enum ShortcutAction
{
    TogglePlayPause = 0,

    PreviousTrack = 1,

    NextTrack = 2,

    VolumeUp = 3,

    VolumeDown = 4,

    ToggleFavorite = 5,

    /// <summary>
    /// 静音开关。
    /// </summary>
    /// <remarks>
    /// <b>值取 6 而不是插在 VolumeDown 后面。</b> 枚举值虽按名字落盘（见
    /// <c>ShortcutSettingsJsonContext</c>），没必要时就更不该动已有成员的值 ——
    /// 设置页里的行序由 <see cref="ShortcutActions.All"/> 决定，与这里的数值无关。
    /// </remarks>
    ToggleMute = 6,
}

/// <summary>
/// <see cref="ShortcutAction"/> 的清单与文案。
/// </summary>
/// <remarks>
/// 文案放在 Core 而不是界面层：设置页的行标题与录制对话框的标题要用同一份，
/// 两处各写一遍会在改措辞时漏掉一处。
/// </remarks>
public static class ShortcutActions
{
    /// <summary>全部动作，<b>顺序即设置页里的行序</b>。</summary>
    public static readonly ShortcutAction[] All =
    [
        ShortcutAction.TogglePlayPause,
        ShortcutAction.PreviousTrack,
        ShortcutAction.NextTrack,
        ShortcutAction.VolumeUp,
        ShortcutAction.VolumeDown,
        ShortcutAction.ToggleMute,
        ShortcutAction.ToggleFavorite,
    ];

    /// <summary>行标题。</summary>
    public static string DisplayName(this ShortcutAction action) => action switch
    {
        ShortcutAction.TogglePlayPause => "播放 / 暂停",
        ShortcutAction.PreviousTrack => "上一首",
        ShortcutAction.NextTrack => "下一首",
        ShortcutAction.VolumeUp => "音量 +",
        ShortcutAction.VolumeDown => "音量 -",
        ShortcutAction.ToggleMute => "静音开关",
        ShortcutAction.ToggleFavorite => "收藏歌曲",
        _ => action.ToString(),
    };
}
