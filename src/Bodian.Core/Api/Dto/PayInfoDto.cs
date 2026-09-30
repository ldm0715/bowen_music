using System.Text.Json.Serialization;

namespace Bodian.Core.Api.Dto;

/// <summary>
/// 曲目的授权信息。
/// </summary>
/// <remarks>
/// <para>
/// <b>本层最大的陷阱：同一个字段在不同接口里的 JSON 类型不同。</b>
/// <see cref="RefrainStartMs"/> / <see cref="RefrainEndMs"/> / <see cref="LimitFree"/>
/// 在 <c>service/music/info</c> 里是**字符串**（<c>"84346"</c>），
/// 在 <c>search/music/list</c> 里是**数字**（<c>84346</c>）。
/// </para>
/// <para>
/// 靠 <c>BodianJsonContext</c> 的 <c>NumberHandling.AllowReadingFromString</c> 一行解决，
/// **不要为这几个字段写自定义转换器**。这个不一致只在运行时炸、而且只在跑到第二个接口时才暴露，
/// 所以配套有一条双 fixture 对照测试守着它。
/// </para>
/// <para>
/// <b>注意与本类同名不同构的 <see cref="AccountPayInfoDto"/></b>（登录响应里的账号级 payInfo）。
/// 两者唯一相同的是 JSON 字段名，字段集合完全不同，**必须是两个类型**。
/// </para>
/// </remarks>
internal sealed class PayInfoDto
{
    // ── 权限开关（int） ─────────────────────────────────────────────────────

    [JsonPropertyName("cannotDownload")] public int CannotDownload { get; init; }

    [JsonPropertyName("cannotOnlinePlay")] public int CannotOnlinePlay { get; init; }

    [JsonPropertyName("extendAttr")] public int ExtendAttr { get; init; }

    [JsonPropertyName("paytype")] public int PayType { get; init; }

    /// <summary>类型跨接口不一致，见类型注释。</summary>
    [JsonPropertyName("limitfree")] public int LimitFree { get; init; }

    // ── 终端开关串（string） ────────────────────────────────────────────────

    /// <summary>4 字符串，内容为 <c>"1"</c> 重复。</summary>
    [JsonPropertyName("play")] public string? Play { get; init; }

    /// <summary>4 字符串。</summary>
    [JsonPropertyName("down")] public string? Down { get; init; }

    /// <summary>4 字符串。</summary>
    [JsonPropertyName("download")] public string? Download { get; init; }

    /// <summary>12 字符串。</summary>
    [JsonPropertyName("nplay")] public string? NPlay { get; init; }

    /// <summary>12 字符串。</summary>
    [JsonPropertyName("ndown")] public string? NDown { get; init; }

    [JsonPropertyName("overseas_nplay")] public string? OverseasNPlay { get; init; }

    [JsonPropertyName("overseas_ndown")] public string? OverseasNDown { get; init; }

    /// <summary>试听片段开关。</summary>
    [JsonPropertyName("listen_fragment")] public string? ListenFragment { get; init; }

    /// <summary>客户端零消费点（只出现在 <c>PayInfo</c> 的 toJson/fromJson 里），按纯透传处理。</summary>
    [JsonPropertyName("local_encrypt")] public string? LocalEncrypt { get; init; }

    [JsonPropertyName("tips_intercept")] public string? TipsIntercept { get; init; }

    // ── 试听区间（毫秒） ────────────────────────────────────────────────────

    /// <summary>
    /// 试听片段起点，**单位毫秒**（不是秒——曲目级 <c>duration</c> 才是秒）。
    /// </summary>
    [JsonPropertyName("refrain_start")] public long RefrainStartMs { get; init; }

    /// <summary>试听片段终点，**单位毫秒**。</summary>
    [JsonPropertyName("refrain_end")] public long RefrainEndMs { get; init; }

    // ── 映射表 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 档位名 → 序号。**不是账号权限表**，客户端全工程零消费点，
    /// **不要拿它推断账号权限**。
    /// </summary>
    [JsonPropertyName("paytagindex")] public Dictionary<string, int>? PayTagIndex { get; init; }

    /// <summary>
    /// 费用类型。值是字符串（<c>"1"</c> / <c>"0"</c>）。
    /// 实测键集合会变：<c>music/info</c> 有 <c>vip</c>/<c>song</c>/<c>album</c>/<c>bookvip</c>/<c>bodianAlbum</c>，
    /// <c>search/music/list</c> 只有 <c>vip</c>/<c>song</c>/<c>bodianAlbum</c>。所以用字典而不是固定属性。
    /// </summary>
    [JsonPropertyName("feeType")] public Dictionary<string, string>? FeeType { get; init; }

    // ── 凭据 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 防盗链参数。服务端在看广告之后才下发，客户端**只透传**——它没有签发接口，
    /// 是 <c>payInfo</c> 里的一个字段而已。
    /// <para>
    /// <b>凭据字段，绝不入日志。</b>
    /// </para>
    /// </summary>
    [JsonPropertyName("freeSign")] public string? FreeSign { get; init; }
}
