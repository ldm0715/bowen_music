namespace Bodian.Core.Models;

/// <summary>
/// 一首歌的 MV，已解析成可直接播放的形态。
/// </summary>
/// <remarks>
/// 由 <c>service/mv/info</c> 得到。**曲目详情里没有 MV 直链**（桌面协议不下放 <c>mvInfo</c>），
/// 所以这是取 MV 的唯一途径，见 <c>reverse/findings/15-mv.md</c>。
/// </remarks>
public sealed record MvInfo
{
    /// <summary>视频地址。**带签名，绝不入日志。**</summary>
    public required Uri VideoUrl { get; init; }

    /// <summary>
    /// 试看上限。<c>null</c> 表示不限。
    /// </summary>
    /// <remarks>
    /// 服务端用 <c>playLimitTime = 0</c> 表达「不限」，不是「零秒」，所以映射时转成 <c>null</c>。
    /// 实测那个账号（有 VIP）拿到的都是 0；非会员是否会被限时**未验证**。
    /// </remarks>
    public TimeSpan? PreviewLimit { get; init; }

    /// <summary>
    /// 这段 MV 只能试看。为真时播放到 <see cref="PreviewLimit"/> 就该停，不能当完整 MV 播下去。
    /// </summary>
    public bool IsPreviewOnly => PreviewLimit > TimeSpan.Zero;
}
