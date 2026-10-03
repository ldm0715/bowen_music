namespace Bodian.Core.Models;

/// <summary>
/// 播放模式。
/// </summary>
/// <remarks>
/// 只有三个：顺序播放、列表循环、列表随机。**没有单曲循环** ——
/// 真要做，这里加一个成员、<c>PlayQueue.MoveNext</c> 加一个分支即可，图标现成（Segoe <c>E8ED</c> RepeatOne）。
/// <para>
/// <b>不要和 <see cref="PlaybackPolicy"/> 混为一谈</b>：那个名字里也有「Playback」，
/// 但它是「这一次能不能算完整一首」的试听规则，与循环/随机无关。
/// </para>
/// </remarks>
public enum PlayMode
{
    /// <summary>顺序播放：按队列顺序放到底就停。</summary>
    Sequential = 0,

    /// <summary>列表循环：放完最后一首回到第一首。</summary>
    ListLoop = 1,

    /// <summary>列表随机：洗牌后按随机顺序放，一轮走完重新洗牌继续。</summary>
    Shuffle = 2,
}

/// <summary>
/// <see cref="PlayMode"/> 的显示名与切换顺序。
/// </summary>
public static class PlayModeExtensions
{
    /// <summary>模式按钮点一下切到的下一个模式。</summary>
    public static PlayMode Next(this PlayMode mode) => mode switch
    {
        PlayMode.Sequential => PlayMode.ListLoop,
        PlayMode.ListLoop => PlayMode.Shuffle,
        PlayMode.Shuffle => PlayMode.Sequential,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "未知的播放模式"),
    };

    /// <summary>按钮上提示的当前模式名。</summary>
    public static string DisplayName(this PlayMode mode) => mode switch
    {
        PlayMode.Sequential => "顺序播放",
        PlayMode.ListLoop => "列表循环",
        PlayMode.Shuffle => "列表随机",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "未知的播放模式"),
    };
}
