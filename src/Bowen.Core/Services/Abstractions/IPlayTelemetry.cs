using Bowen.Core.Models;

namespace Bowen.Core.Services.Abstractions;

/// <summary>
/// 把播放行为同步到账号的播放统计（次数 / 时长 / 排行榜）。
/// </summary>
/// <remarks>
/// <para>
/// <b>这是写操作，失败必须静默。</b> 它不参与播放决策，也不提供界面状态；
/// 调用方只需要在「播完」或「切歌」时调用一次，其余交给实现。
/// </para>
/// <para>
/// 未登录、曲目无有效时长或 id 时什么都不做。协议与规则见 <c>docs/play-stats.md</c>。
/// </para>
/// </remarks>
public interface IPlayTelemetry
{
    /// <summary>整曲播完。固定按 <c>operate = 0</c> 上报，已播位置等于总时长。</summary>
    void ReportCompleted(Track track);

    /// <summary>
    /// 没播完就切走。<paramref name="position"/> 是离开时的播放位置，
    /// 实现按「已播 / 总时长」选择 <c>operate</c>。
    /// </summary>
    void ReportSwitched(Track track, TimeSpan position);
}
