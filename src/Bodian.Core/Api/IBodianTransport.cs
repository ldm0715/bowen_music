using System.Text.Json.Serialization.Metadata;

namespace Bodian.Core.Api;

/// <summary>
/// 发请求的唯一入口。
/// </summary>
public interface IBodianTransport
{
    /// <summary>
    /// 发一个 API 请求并解析信封。
    /// </summary>
    /// <param name="request">请求形状。见其类型注释里关于 <c>Path</c> 与 <c>JsonBody</c> 的约束。</param>
    /// <param name="dataTypeInfo">
    /// <c>data</c> 的类型信息，**必须从 <c>BodianJsonContext</c> 取**。
    /// 这个参数是故意的：它让任何想退化成反射序列化的写法编译不过。
    /// </param>
    Task<BodianEnvelope<T>> SendAsync<T>(
        BodianRequest request,
        JsonTypeInfo<T> dataTypeInfo,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 发一个绝对 URL（歌词站在 <c>mlyric.kuwo.cn</c>，不走 <c>/api</c> 前缀、不签名）。
    /// </summary>
    Task<BodianEnvelope<T>> SendAbsoluteAsync<T>(
        Uri url,
        JsonTypeInfo<T> dataTypeInfo,
        CancellationToken cancellationToken = default);
}
