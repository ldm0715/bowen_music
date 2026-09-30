namespace Bodian.Core.Models;

/// <summary>
/// 一首歌能不能播、能播多少 —— 一次点播的完整结论。
/// </summary>
/// <remarks>
/// <para>
/// 用「抽象 record + 派生」而不是「枚举 + 一堆可空字段」，是为了让
/// <b>「把试听当成完整歌曲」变成类型层面表达不出来的错误</b>：只有
/// <see cref="AuditionOnly"/> 带时间窗，消费处必须 <c>switch</c> 出那个分支才拿得到
/// <see cref="AuditionOnly.Start"/> / <see cref="AuditionOnly.End"/>。
/// </para>
/// <para>
/// 授权完全交给服务端的 <c>checkRight</c>，客户端**不做任何权限推断**，也不因为
/// <c>Denied</c> 就去换档位重试。
/// </para>
/// </remarks>
public abstract record PlaybackResolution
{
    /// <summary>有完整播放权限，可以整曲播放。</summary>
    /// <param name="Source">音源地址。</param>
    public sealed record Playable(AudioSource Source) : PlaybackResolution;

    /// <summary>
    /// 只能试听 <paramref name="Start"/> 到 <paramref name="End"/> 这一段。
    /// </summary>
    /// <remarks>
    /// <b>不得当作完整歌曲</b>：不能按整曲计入播放历史，也不能写入完整曲目的缓存
    /// （见 <see cref="PlaybackPolicy.MayPersistAsCompleteTrack"/>）。
    /// </remarks>
    /// <param name="Source">试听片段的音源地址。</param>
    /// <param name="Start">试听区间起点（通常为 0）。</param>
    /// <param name="End">试听区间终点，播到这里必须停。</param>
    public sealed record AuditionOnly(AudioSource Source, TimeSpan Start, TimeSpan End) : PlaybackResolution;

    /// <summary>不能播。原因见 <paramref name="Reason"/>。</summary>
    /// <param name="Reason">不可播的原因。</param>
    public sealed record Denied(PlaybackDenialReason Reason) : PlaybackResolution;
}

/// <summary>不可播的原因。UI 据此决定提示文案与是否引导登录。</summary>
public enum PlaybackDenialReason
{
    /// <summary>未登录，且这首歌需要登录才能播。UI 应引导登录。</summary>
    NotAuthenticated = 0,

    /// <summary>已登录但没有这首歌的播放权限。UI 应如实告知，不引导登录。</summary>
    NoPermission = 1,

    /// <summary>拿到了授权，但服务端没给出合法的音源地址。</summary>
    NoStreamUrl = 2,

    /// <summary>这首歌没有本项目可播的档位。</summary>
    NoUsableQuality = 3,

    /// <summary>服务端判定只能试听，但没给出试听片段信息。</summary>
    AuditionUnavailable = 4,

    /// <summary>曲目不可用（下架等）。</summary>
    TrackUnavailable = 5,
}
