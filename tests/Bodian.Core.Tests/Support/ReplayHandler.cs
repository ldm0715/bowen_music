using System.Net;
using System.Text;

namespace Bodian.Core.Tests.Support;

/// <summary>捕获下来的请求。</summary>
internal sealed record CapturedRequest(
    string Method,
    string Url,
    string? Body,
    IReadOnlyDictionary<string, string[]> Headers)
{
    /// <summary>同名头的出现次数。用来发现「加了两次」这类问题。</summary>
    public int HeaderCount(string name)
        => Headers.TryGetValue(name, out var values) ? values.Length : 0;

    public string? Header(string name)
        => Headers.TryGetValue(name, out var values) ? values[0] : null;
}

/// <summary>
/// 回放用的 <see cref="HttpMessageHandler"/>：捕获请求、按配置返回响应。测试里零真实网络。
/// </summary>
/// <remarks>
/// 断言 URL 时必须用 <see cref="HttpRequestMessage.RequestUri"/> 的 <c>OriginalString</c>——
/// 它原样返回构造时传入的字符串，不会被 <see cref="Uri"/> 规范化，
/// 所以能精确比对签名前的 query 串（query 顺序、编码、空值都保持原样）。
/// </remarks>
internal sealed class ReplayHandler : HttpMessageHandler
{
    private readonly List<CapturedRequest> _requests = [];

    /// <summary>按请求返回响应。默认返回 <c>{"code":200,"msg":"success","data":null}</c>。</summary>
    public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; } =
        _ => Json("""{"code":200,"msg":"success"}""");

    /// <summary>返回响应前的延迟。用来测超时。</summary>
    public TimeSpan Delay { get; set; }

    public IReadOnlyList<CapturedRequest> Requests => _requests;

    public CapturedRequest LastRequest => _requests[^1];

    public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Raw(string body, string contentType, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, contentType) };

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        Collect(headers, request.Headers.NonValidated);
        if (request.Content is not null)
        {
            Collect(headers, request.Content.Headers.NonValidated);
        }

        _requests.Add(new CapturedRequest(
            request.Method.Method,
            request.RequestUri!.OriginalString,
            body,
            headers));

        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken).ConfigureAwait(false);
        }

        return Responder(request);
    }

    /// <summary>
    /// 用 <c>NonValidated</c> 读**未经解析的原始值**。
    /// </summary>
    /// <remarks>
    /// 直接用 <c>request.Headers</c> 会拿到解析后的形式，信息有损——
    /// 例如 <c>Dart/3.3 (dart:io)</c> 会被当成 product + comment，枚举时只剩 <c>Dart/3.3</c>。
    /// 那样断言的就是「解析器怎么理解」而不是「实际发出去了什么」。
    /// </remarks>
    private static void Collect(
        Dictionary<string, string[]> target,
        System.Net.Http.Headers.HttpHeadersNonValidated source)
    {
        foreach (var (name, values) in source)
        {
            target[name] = [.. values];
        }
    }
}
