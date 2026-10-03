using System.Net.Http.Headers;
using Bodian.Core.Services.Abstractions;

namespace Bodian.Core.Api;

/// <summary>
/// 请求头表。**这是官方 PC 客户端实测的那一套**，缺项会让搜索接口返回业务码 <c>402</c>。
/// </summary>
/// <remarks>
/// 单独拉出来是为了能直接断言，不用通过一次真实请求去验。
/// </remarks>
internal static class BodianHeaders
{
    /// <summary>不含身份信息的那部分。顺序固定，便于比对。</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Common(
        BodianTransportOptions options,
        string deviceId) =>
    [
        new("User-Agent", options.UserAgent),
        new("plat", options.Platform),
        new("channel", options.Channel),
        new("ver", options.Version),
        new("svrver", options.ServerVersion),
        new("api-ver", options.ApiVersion),
        new("brand", options.Brand),
        new("net", options.Network),
        new("devid", deviceId),
        new("qimei36", deviceId),
        new("Accept-Encoding", "gzip"),
    ];

    /// <summary>
    /// 把请求头挂到消息上，已登录时**再以请求头形式传一遍** <c>uid</c> 与 <c>token</c>
    /// （query 里已经有一份，这是 PC 端的实际行为）。
    /// </summary>
    public static void Apply(
        HttpRequestMessage request,
        BodianTransportOptions options,
        IDeviceIdentity device,
        BodianSession session)
    {
        foreach (var (name, value) in Common(options, device.Value))
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        if (session.IsAuthenticated)
        {
            request.Headers.TryAddWithoutValidation("uid", session.Uid);
            request.Headers.TryAddWithoutValidation("token", session.Token);
        }

        // ★ 只在 ContentType 还空着时才补 application/json。
        //   multipart 的 ContentType 自带 boundary，无条件覆写会把它毁掉，服务端就解析不出文件。
        if (request.Content is { Headers.ContentType: null })
        {
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }
    }
}
