using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Bodian.Core.Diagnostics;
using Bodian.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace Bodian.Core.Api;

/// <summary>
/// 传输层：请求头、签名、信封解析、限额、超时。**唯一碰 <see cref="HttpClient"/> 的地方。**
/// </summary>
/// <remarks>
/// <para>
/// <b>本类只服务 API JSON，音频字节流不归它管。</b> <c>audioUrl</c> 拿到的是 CDN 直链，
/// 下载走独立路径（P9）。否则这里的「总超时 30 秒」会把大文件下载掐断。
/// </para>
/// <para>
/// <b>日志里只有 <see cref="SafeUrl"/>（scheme + host + path）、方法、耗时、业务码。</b>
/// query 与 body 从头到尾不进入任何格式化字符串——这样即使
/// <c>RedactingLoggerFactory</c> 那一层被绕过，也不会有凭据泄露。
/// </para>
/// </remarks>
public sealed class BodianHttpTransport : IBodianTransport, IDisposable
{
    private readonly HttpClient _http;
    private readonly BodianTransportOptions _options;
    private readonly BodianSession _session;
    private readonly IDeviceIdentity _device;
    private readonly TimeProvider _time;
    private readonly ILogger<BodianHttpTransport> _logger;

    public BodianHttpTransport(
        HttpMessageHandler handler,
        BodianTransportOptions options,
        BodianSession session,
        IDeviceIdentity device,
        TimeProvider? timeProvider = null,
        ILogger<BodianHttpTransport>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(device);

        _options = options;
        _session = session;
        _device = device;
        _time = timeProvider ?? TimeProvider.System;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<BodianHttpTransport>.Instance;

        // 超时由本类用 linked CTS 统一管，覆盖「发请求 + 读 body」全程；
        // HttpClient.Timeout 只管到响应头（因为用了 ResponseHeadersRead），所以关掉它。
        _http = new HttpClient(handler, disposeHandler: false)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    /// <summary>
    /// 生产用的 handler。**这是唯一配置 handler 的地方**，单独写测试断言这几个属性。
    /// </summary>
    public static SocketsHttpHandler CreateHandler(BodianTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var handler = new SocketsHttpHandler
        {
            // 请求头里也显式带了 Accept-Encoding: gzip，这里负责真正的解压。
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = options.PooledConnectionLifetime,
        };

        if (options.Proxy is { } proxy)
        {
            handler.Proxy = new WebProxy(proxy);
            handler.UseProxy = true;
        }

        return handler;
    }

    public Task<BodianEnvelope<T>> SendAsync<T>(
        BodianRequest request,
        JsonTypeInfo<T> dataTypeInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(dataTypeInfo);

        var pairs = new List<KeyValuePair<string, string>>(request.Query)
        {
            new("uid", _session.Uid),
            new("token", _session.Token),
        };

        var seedQuery = string.Empty;

        if (request.Signed)
        {
            var stamp = _time.GetUtcNow().ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
            pairs.Add(new KeyValuePair<string, string>("timestamp", stamp));
            pairs.Add(new KeyValuePair<string, string>("sign", string.Empty));

            // 签名覆盖「含空 sign 位」的 query 串——与 P0 探针、与 fixtures/sign-golden.json 一致。
            seedQuery = BodianSigner.FormUrlEncode(pairs);

            var sign = BodianSigner.SignRaw(request.Path, seedQuery, request.JsonBody);
            pairs[^1] = new KeyValuePair<string, string>("sign", sign);
        }

        var url = _options.BaseAddress + request.Path + "?" + BodianSigner.FormUrlEncode(pairs);

        return SendCoreAsync(
            request.Path,
            request.Verb == BodianHttpVerb.Post ? HttpMethod.Post : HttpMethod.Get,
            url,
            request.JsonBody,
            request.AcceptedCodes,
            dataTypeInfo,
            cancellationToken);
    }

    /// <summary>歌词站在另一个域，不走 <c>/api</c> 前缀、不签名、不带身份 query。</summary>
    public Task<BodianEnvelope<T>> SendAbsoluteAsync<T>(
        Uri url,
        JsonTypeInfo<T> dataTypeInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(dataTypeInfo);

        return SendCoreAsync(
            url.AbsolutePath,
            HttpMethod.Get,
            url.OriginalString,
            body: null,
            acceptedCodes: [],
            dataTypeInfo,
            cancellationToken);
    }

    private async Task<BodianEnvelope<T>> SendCoreAsync<T>(
        string path,
        HttpMethod method,
        string url,
        string? body,
        IReadOnlyCollection<BodianErrorCode> acceptedCodes,
        JsonTypeInfo<T> dataTypeInfo,
        CancellationToken cancellationToken)
    {
        var safeUrl = SafeUrl.From(url);
        var revision = _session.Revision;
        var started = _time.GetTimestamp();

        using var request = new HttpRequestMessage(method, url);

        if (body is not null)
        {
            // 签名覆盖的是这份精确字节，序列化必须发生在调用方，这里只做搬运。
            request.Content = new ByteArrayContent(new UTF8Encoding(false).GetBytes(body));
        }

        // 只挂一次：TryAddWithoutValidation 对同名头是追加，调两次会发出重复头。
        BodianHeaders.Apply(request, _options, _device, _session);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.RequestTimeout);

        try
        {
            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);

            // HTTP 状态码非 2xx 按网络错误处理，不要去解析它的 body——
            // 那通常是一张网关错误页，把它当业务码解析只会给出误导性的报错。
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("{Method} {Url} 返回 HTTP {Status}", method.Method, safeUrl, (int)response.StatusCode);
                throw new HttpRequestException(
                    $"{safeUrl} 返回 HTTP {(int)response.StatusCode}",
                    inner: null,
                    response.StatusCode);
            }

            var raw = await ReadLimitedAsync(response, path, timeout.Token).ConfigureAwait(false);

            var envelope = ParseEnvelope(raw, path, dataTypeInfo, _session.Revision);

            _logger.LogDebug(
                "{Method} {Url} → HTTP {Status} code {Code}，{Elapsed} ms",
                method.Method,
                safeUrl,
                (int)response.StatusCode,
                envelope.Code,
                _time.GetElapsedTime(started).TotalMilliseconds);

            if (!envelope.IsSuccess && !acceptedCodes.Contains(envelope.ErrorCode))
            {
                ThrowFor(envelope, path, raw);
            }

            return envelope;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // 外层没取消，那就是我们的超时。抛 TimeoutException 更好排查，
            // 也避免调用方把超时误当成「用户取消」。
            _logger.LogWarning("{Method} {Url} 超时（{Seconds}s）", method.Method, safeUrl, _options.RequestTimeout.TotalSeconds);
            throw new TimeoutException($"{safeUrl} 在 {_options.RequestTimeout.TotalSeconds} 秒内没有完成");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "{Method} {Url} 网络错误", method.Method, safeUrl);
            throw;
        }
        finally
        {
            _logger.LogTrace("{Method} {Url} 会话版本 {Revision}", method.Method, safeUrl, revision);
        }
    }

    /// <summary>
    /// 读响应体并施加上限。**必须用 <c>ResponseHeadersRead</c> 之后再读流**——
    /// 用默认的 <c>ResponseContentRead</c> 会让缓冲发生在限额之前。
    /// </summary>
    /// <remarks>
    /// 读的是解压后的流，所以限额天然作用于解压后字节，**顺带挡住 gzip 炸弹**。
    /// </remarks>
    private async Task<string> ReadLimitedAsync(HttpResponseMessage response, string path, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;

        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > _options.MaxResponseBytes)
            {
                throw new BodianPayloadTooLargeException(path, _options.MaxResponseBytes);
            }

            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private static BodianEnvelope<T> ParseEnvelope<T>(
        string raw,
        string path,
        JsonTypeInfo<T> dataTypeInfo,
        int sessionRevision)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new BodianEnvelope<T>((int)BodianErrorCode.MalformedResponse, null, null, default, sessionRevision);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(raw);
        }
        catch (JsonException ex)
        {
            // 网关错误页这类非 JSON 响应：保留原文（已脱敏）供排查，不要吞掉。
            throw new BodianMalformedResponseException(path, raw, ex);
        }

        using (document)
        {
            var root = document.RootElement;

            var code = root.TryGetProperty("code", out var codeElement) && codeElement.TryGetInt32(out var parsedCode)
                ? parsedCode
                : (int)BodianErrorCode.MalformedResponse;

            var message = root.TryGetProperty("msg", out var msgElement) && msgElement.ValueKind == JsonValueKind.String
                ? msgElement.GetString()
                : null;

            var requestId = root.TryGetProperty("reqId", out var reqIdElement) && reqIdElement.ValueKind == JsonValueKind.String
                ? reqIdElement.GetString()
                : null;

            T? data = default;
            if (root.TryGetProperty("data", out var dataElement)
                && dataElement.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
            {
                data = dataElement.Deserialize(dataTypeInfo);
            }

            return new BodianEnvelope<T>(code, message, requestId, data, sessionRevision);
        }
    }

    private void ThrowFor<T>(BodianEnvelope<T> envelope, string path, string raw)
    {
        if (envelope.ErrorCode == BodianErrorCode.NeedAuth)
        {
            // 清会话语义收敛在 BodianSession 里，这里只通知。
            _session.NotifyUnauthorized();
        }

        throw new BodianApiException(
            envelope.ErrorCode,
            envelope.Code,
            path,
            envelope.Message,
            envelope.RequestId,
            raw);
    }

    /// <summary>只释放这里建的 <see cref="HttpClient"/>；传入的 handler 由调用方（DI）负责。</summary>
    public void Dispose() => _http.Dispose();
}
