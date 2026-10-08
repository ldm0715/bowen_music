using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace Bodian.Probe;

/// <summary>path 参与签名时的形态。文档写的是「不含 /api 前缀的业务路径」，实测对照用得上。</summary>
internal enum PathForm
{
    Bare,
    LeadingSlash,
    WithApiPrefix,
}

/// <summary>签名时 query 里是否已经含一个空值的 sign 参数。文档字面描述是「含」。</summary>
internal enum SignKeyForm
{
    Included,
    Omitted,
}

internal sealed record ProbeResponse(
    int Status,
    string RawBody,
    JsonNode? Envelope,
    string Url,
    string SignedQuery,
    string SeedQuery)
{
    public int Code => Envelope?["code"]?.GetValue<int>() ?? -1;

    public JsonNode? Data => Envelope?["data"];

    public string Describe(string label = "业务码")
        => $"HTTP {Status}  {label} {Code}";
}

/// <summary>
/// 传输层：请求头、签名、信封解析。P0 阶段唯一碰 HttpClient 的地方。
/// </summary>
internal sealed class ProbeClient : IDisposable
{
    private const string BaseUrl = "https://bd-api.kuwo.cn/api/";

    private readonly HttpClient _http;
    private readonly string _devid;

    /// <summary>未登录为 -1。</summary>
    public string Uid { get; set; } = "-1";

    /// <summary>未登录为空串。</summary>
    public string Token { get; set; } = "";

    /// <summary>设备标识。audioUrl 的 devId 参数要用它。</summary>
    public string DeviceId => _devid;

    /// <summary>
    /// 客户端版本头。默认 PC 端的 1.1.7——但 <c>service/advert/config</c> 里有一条
    /// <c>highQuality.reason = "the version is too low or payVip!!"</c>，
    /// 说明某些档位会被版本门挡住，所以这里要能改。
    /// </summary>
    public string Version { get; set; } = "1.1.7";

    public bool Verbose { get; set; }

    /// <summary>
    /// <c>plat</c> 请求头。默认 <c>win</c>（桌面协议）。
    /// </summary>
    /// <remarks>
    /// 有些端点是移动端专属的（如 <c>play/music/library/*</c>），
    /// 用桌面头打过去服务端会回 500 <c>Service error</c>。要试这些端点就换成 <c>android</c>。
    /// </remarks>
    public string Plat { get; set; } = "win";

    public ProbeClient(string? proxy, bool verbose = false)
    {
        Verbose = verbose;
        _devid = DeviceIdentity.GetOrCreate();

        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        };

        if (!string.IsNullOrWhiteSpace(proxy))
        {
            handler.Proxy = new WebProxy(proxy);
            handler.UseProxy = true;
        }

        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<ProbeResponse> SendAsync(
        string path,
        IEnumerable<KeyValuePair<string, string>>? query = null,
        string? body = null,
        bool signed = false,
        SignKeyForm signKeyForm = SignKeyForm.Included,
        PathForm pathForm = PathForm.Bare,
        HttpMethod? method = null,
        string? overrideSign = null,
        long? fixedTimestamp = null,
        bool mobileSign = false)
    {
        var pairs = new List<KeyValuePair<string, string>>(query ?? [])
        {
            new("uid", Uid),
            new("token", Token),
        };

        var seedQuery = "";
        var verb = method ?? HttpMethod.Get;

        if (signed)
        {
            var stamp = fixedTimestamp ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            pairs.Add(new KeyValuePair<string, string>("timestamp", stamp.ToString()));

            if (mobileSign)
            {
                // ★ 移动端签的是**整条 URL**，所以要先按「没有 sign」的参数表把 URL 拼出来，
                //   签完再补 sign —— 桌面版那条路签的是 query 串 + 裸路径，两者不可混用。
                var unsignedUrl = BaseUrl + path + "?" + BodianSigner.FormUrlEncode(pairs);

                var mobileSignature = overrideSign ?? BodianMobileSigner.Sign(unsignedUrl, verb != HttpMethod.Get ? body : null);

                pairs.Add(new KeyValuePair<string, string>("sign", mobileSignature));
            }
            else
            {
                pairs.Add(new KeyValuePair<string, string>("sign", ""));

                var seedPairs = signKeyForm == SignKeyForm.Included
                    ? pairs
                    : pairs.Where(p => p.Key != "sign");

                seedQuery = BodianSigner.FormUrlEncode(seedPairs);

                // overrideSign 是给 signtest 做对照组用的：请求形状完全正确，只有 sign 的值是垃圾。
                var sign = overrideSign ?? BodianSigner.SignRaw(SignPath(path, pathForm), seedQuery, body);
                pairs[^1] = new KeyValuePair<string, string>("sign", sign);
            }
        }

        var signedQuery = BodianSigner.FormUrlEncode(pairs);
        var url = BaseUrl + path + "?" + signedQuery;

        if (Verbose)
        {
            Console.Error.WriteLine($"> {verb.Method} {Sanitizer.ForLogQuery(url)}");
            if (body is not null)
            {
                Console.Error.WriteLine($"> body {Sanitizer.ForLogBody(body)}");
            }
        }

        using var request = BuildRequest(verb, url, body);
        using var response = await _http.SendAsync(request).ConfigureAwait(false);
        var (raw, envelope) = await ReadBodyAsync(response).ConfigureAwait(false);

        return new ProbeResponse((int)response.StatusCode, raw, envelope, url, signedQuery, seedQuery);
    }

    /// <summary>
    /// 发一个 <c>multipart/form-data</c> 请求——封面之类的二进制上传走这条。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>签名默认只覆盖 path 与 query，不碰二进制 body</b>：桌面签名对 body 做的是
    /// <c>md5(body + "kuwotest")</c>，那是针对 JSON 字符串的，二进制没有良定义的字符串形态。
    /// 这一条<b>未实测</b>（见 <c>docs/edit-playlist.md</c>）；<paramref name="signBodyBytes"/>
    /// 打开时把原始字节按 Latin-1 当作字符串进 md5，用来验证服务端到底签不签 body。
    /// </para>
    /// <para>
    /// 风险窗口很小：<c>ver ≤ 3.0.0</c> 时服务端根本不校验签名。
    /// </para>
    /// </remarks>
    public async Task<ProbeResponse> SendMultipartAsync(
        string path,
        IEnumerable<KeyValuePair<string, string>>? query,
        string fieldName,
        string fileName,
        string contentType,
        byte[] content,
        bool signed = false,
        HttpMethod? method = null,
        bool signBodyBytes = false)
    {
        var pairs = new List<KeyValuePair<string, string>>(query ?? [])
        {
            new("uid", Uid),
            new("token", Token),
        };

        var seedQuery = "";
        var verb = method ?? HttpMethod.Post;

        if (signed)
        {
            var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            pairs.Add(new KeyValuePair<string, string>("timestamp", stamp.ToString()));
            pairs.Add(new KeyValuePair<string, string>("sign", ""));

            seedQuery = BodianSigner.FormUrlEncode(pairs);
            var signBody = signBodyBytes ? Encoding.Latin1.GetString(content) : null;
            pairs[^1] = new KeyValuePair<string, string>("sign", BodianSigner.SignRaw(path, seedQuery, signBody));
        }

        var signedQuery = BodianSigner.FormUrlEncode(pairs);
        var url = BaseUrl + path + "?" + signedQuery;

        if (Verbose)
        {
            Console.Error.WriteLine($"> {verb.Method} {Sanitizer.ForLogQuery(url)}");
            Console.Error.WriteLine($"> multipart {fieldName}={fileName}（{contentType}，{content.Length} 字节）"
                                    + $"　签 body：{(signBodyBytes ? "是" : "否")}");
        }

        using var request = new HttpRequestMessage(verb, url);
        AddCommonHeaders(request);

        // 独立构造，不复用 BuildRequest —— 它会无条件把 ContentType 设成 application/json，
        // 那会盖掉 MultipartFormDataContent 自带的 boundary。
        var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        multipart.Add(fileContent, fieldName, fileName);
        request.Content = multipart;

        using var response = await _http.SendAsync(request).ConfigureAwait(false);
        var (raw, envelope) = await ReadBodyAsync(response).ConfigureAwait(false);

        return new ProbeResponse((int)response.StatusCode, raw, envelope, url, signedQuery, seedQuery);
    }

    /// <summary>发一个绝对 URL（歌词站在 mlyric.kuwo.cn，不走 API 主站的 /api 前缀，也不签名）。</summary>
    public async Task<ProbeResponse> SendAbsoluteAsync(string url)
    {
        if (Verbose)
        {
            Console.Error.WriteLine($"> GET {Sanitizer.ForLogQuery(url)}");
        }

        using var request = BuildRequest(HttpMethod.Get, url, null);
        using var response = await _http.SendAsync(request).ConfigureAwait(false);
        var (raw, envelope) = await ReadBodyAsync(response).ConfigureAwait(false);

        return new ProbeResponse((int)response.StatusCode, raw, envelope, url, "", "");
    }

    /// <summary>读响应体并尝试解析信封。非 JSON（网关错误页等）保留原文供排查。</summary>
    private static async Task<(string Raw, JsonNode? Envelope)> ReadBodyAsync(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        JsonNode? envelope = null;
        if (!string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                envelope = JsonNode.Parse(raw);
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or ArgumentException)
            {
                // 解析不了就留 null，raw 已经带回去了。
            }
        }

        return (raw, envelope);
    }

    private static string SignPath(string path, PathForm form) => form switch
    {
        PathForm.LeadingSlash => "/" + path,
        PathForm.WithApiPrefix => "/api/" + path,
        _ => path,
    };

    private HttpRequestMessage BuildRequest(HttpMethod method, string url, string? body)
    {
        var request = new HttpRequestMessage(method, url);
        AddCommonHeaders(request);

        if (body is not null)
        {
            // 签名覆盖的是这份精确字节，序列化必须发生在调用方，这里只做搬运。
            request.Content = new ByteArrayContent(new UTF8Encoding(false).GetBytes(body));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        return request;
    }

    /// <summary>JSON 与 multipart 两条路共用的请求头。</summary>
    private void AddCommonHeaders(HttpRequestMessage request)
    {
        var headers = request.Headers;

        headers.TryAddWithoutValidation("User-Agent", "Dart/3.3 (dart:io)");
        headers.TryAddWithoutValidation("plat", Plat);
        headers.TryAddWithoutValidation("channel", "W1");
        headers.TryAddWithoutValidation("ver", Version);
        headers.TryAddWithoutValidation("svrver", "13");
        headers.TryAddWithoutValidation("api-ver", "application/json");
        headers.TryAddWithoutValidation("brand", "Windows");
        headers.TryAddWithoutValidation("net", "wifi");
        headers.TryAddWithoutValidation("devid", _devid);
        headers.TryAddWithoutValidation("qimei36", _devid);
        headers.TryAddWithoutValidation("Accept-Encoding", "gzip");

        if (Uid != "-1")
        {
            headers.TryAddWithoutValidation("uid", Uid);
            headers.TryAddWithoutValidation("token", Token);
        }
    }

    public void Dispose() => _http.Dispose();
}
