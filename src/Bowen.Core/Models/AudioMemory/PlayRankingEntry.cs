namespace Bowen.Core.Models.AudioMemory;

/// <summary>
/// 本周最爱排行的一项：曲目 + 该周播放次数。
/// </summary>
/// <remarks>
/// 曲目来自 <c>play_ranking</c> 的完整歌曲对象，可以直接进播放队列。
/// </remarks>
public sealed record PlayRankingEntry(Track Track, long PlayCount);
