using System.ComponentModel;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 音量来源：音量按钮与它那颗竖向滑条要的两个值。
/// </summary>
/// <remarks>
/// <para>
/// 播放条、歌词页走 libmpv，MV 页走 <c>MediaPlayer</c>，<b>音量互不相干</b> ——
/// 但<b>交互必须一样</b>：一颗图标按钮，悬停才在上方展开竖向滑条，**不许写成横向外显的滑条**。
/// 让三个视图模型实现同一个接口，<c>VolumeButton</c> 就不必绑死在
/// <see cref="PlayerViewModel"/> 上，MV 页也不必另写一套。
/// </para>
/// <para>
/// 继承 <see cref="INotifyPropertyChanged"/> 不是装饰：<c>x:Bind</c> 要能静态看到通知，
/// 否则降级成一次性求值（音量图标不会跟着静音状态翻）。
/// </para>
/// </remarks>
public interface IVolumeSource : INotifyPropertyChanged
{
    /// <summary>
    /// 音量，**0–100**。
    /// </summary>
    /// <remarks>
    /// 与 <c>IPlaybackService.Volume</c> 同标度，也是滑条 <c>Maximum</c> 与
    /// <c>Formats.Percent</c> 假定的大小。MV 那边的 <c>MediaPlayer.Volume</c> 是 0–1，
    /// 换算在 <c>MvViewModel</c> 的显式实现里做，**只此一处**。
    /// </remarks>
    double Volume { get; set; }

    /// <summary>是否静音。音量按钮据此在中/静音两只图标之间切。</summary>
    bool IsMuted { get; }
}
