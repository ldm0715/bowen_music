using System.Text.Json.Serialization;

namespace Bodian.Core.Api.Dto.Requests;

/// <summary>
/// <c>play/music/v2/checkRight</c> 的请求体。**这个端点没有 <c>br</c> / <c>format</c> 参数** ——
/// 授权结果与档位无关，所以每首歌只调一次，不要放进逐档循环。
/// </summary>
/// <remarks>
/// 请求形态特殊：**GET 但必须带 JSON body**，且签名覆盖这份 body 的精确字节。
/// </remarks>
internal sealed class CheckRightBody
{
    [JsonPropertyName("musicId")] public long MusicId { get; init; }

    /// <summary>服务端在 <c>payInfo</c> 里下发的防盗链串，客户端只透传。默认空串即可跑通主流程。</summary>
    [JsonPropertyName("freeSign")] public string FreeSign { get; init; } = "";
}
