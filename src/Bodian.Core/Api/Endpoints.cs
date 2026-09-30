namespace Bodian.Core.Api;

/// <summary>
/// 已核实的业务路径常量。
/// </summary>
/// <remarks>
/// <para>
/// <b>路径不含 <c>/api</c> 前缀</b>——签名覆盖的就是这个形态。
/// </para>
/// <para>
/// <b>这里只列 P0 已实测或静态确认过的路径</b>，不留猜测项。加新路径前先确认它来自
/// <c>bodian-api-reference.md</c> 的已验证小节或 <c>reverse/findings/</c> 的结论。
/// </para>
/// </remarks>
internal static class Endpoints
{
    // ── 已实测 ✅ ───────────────────────────────────────────────────────────

    public const string MusicInfo = "service/music/info";

    public const string SearchMusicList = "search/music/list";

    /// <summary><b>GET 且必须带 JSON body，签名覆盖该 body。</b></summary>
    public const string CheckRight = "play/music/v2/checkRight";

    /// <summary>与 <see cref="CheckRight"/> 请求形态相同。</summary>
    public const string AudioUrl = "play/music/v2/audioUrl";

    public const string LoginQrCode = "ucenter/login/qrCode";

    public const string LoginQrCodeStatus = "ucenter/login/qrCodeStatus";

    /// <summary>扫码登录的最后一步，响应见 <c>Dto/LoginDto.cs</c>。</summary>
    public const string UsersLogin = "ucenter/users/login";

    // ── 静态确认，参数已解 🟡 ───────────────────────────────────────────────

    /// <summary>
    /// 收藏写入（移动端报文，PC 端实现不同且无法分析）。
    /// body 形如 <c>{"source":6,"sourceId":[228908],"op":1,"uid":...}</c>；
    /// <c>op</c> 的 1/2 方向仍未被证实，**写入前必须先读回确认**。
    /// </summary>
    public const string Collect = "service/collect";

    // ── 歌词站（另一个域，不走 /api 前缀、不签名）────────────────────────────

    public const string LyricHost = "https://mlyric.kuwo.cn";

    public const string LyricPath = "mobi.s";
}
