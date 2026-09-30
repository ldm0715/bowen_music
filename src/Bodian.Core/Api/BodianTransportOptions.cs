namespace Bodian.Core.Api;

/// <summary>
/// 传输层配置。默认值就是官方 PC 客户端实测的那一套。
/// </summary>
public sealed record BodianTransportOptions
{
    /// <summary>API 主站。<b>注意末尾的 <c>/api/</c></b>——业务路径要拼在它后面。</summary>
    public Uri BaseAddress { get; init; } = new("https://bd-api.kuwo.cn/api/");

    public string UserAgent { get; init; } = "Dart/3.3 (dart:io)";

    public string Platform { get; init; } = "win";

    public string Channel { get; init; } = "W1";

    /// <summary>
    /// 客户端版本头。
    /// </summary>
    /// <remarks>
    /// <b>不要随手跟版本。</b> 校验签名与否由这个头控制：<c>≤ 3.0.0</c> 服务端完全不校验签名，
    /// <c>≥ 3.5</c> 强制校验、且走的是移动端另一套算法（本项目没实现移动端签名）。
    /// 跟到 3.5 以上会让**所有**请求一起挂掉，而错误信息只有一句 <c>sign invalid</c>。
    /// </remarks>
    public string Version { get; init; } = "1.1.7";

    public string ServerVersion { get; init; } = "13";

    public string ApiVersion { get; init; } = "application/json";

    public string Brand { get; init; } = "Windows";

    public string Network { get; init; } = "wifi";

    /// <summary>覆盖「发请求 + 读 body」的全程超时。</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan PooledConnectionLifetime { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// 响应体上限（**解压后**的字节数，因此顺带挡住 gzip 炸弹）。
    /// </summary>
    public long MaxResponseBytes { get; init; } = 8L * 1024 * 1024;

    /// <summary>代理。本机开发在 7890 端口时传 <c>http://127.0.0.1:7890</c>。</summary>
    public Uri? Proxy { get; init; }
}
