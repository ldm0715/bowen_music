using System.Net.Http;
using System.Text.Json;
using Bodian.Core.Models;
using Bodian.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.Core.Services.Implementations;

/// <inheritdoc cref="IAppUpdateService" />
/// <remarks>
/// <para>
/// <b>未配置时立刻返回，不建连接、不发任何请求。</b>
/// <see cref="AppUpdateOptions.Default"/> 已指向公开仓库，所以正常构建下走的是配置好的那条路；
/// 未配置那条留着是为了让 <c>AppUpdateOptions</c> 能被替换成空的（测试与本地私有构建）。
/// </para>
/// <para>
/// <b>不打包自动更新</b>：本项目是 unpackaged，没有 MSIX 身份，装不了
/// <c>PackageManager</c> 那套。查到新版本只会打开浏览器让用户自己去下载。
/// </para>
/// </remarks>
public sealed class GitHubReleaseUpdateService : IAppUpdateService, IDisposable
{
    /// <summary>GitHub 的 API 强制要求带 User-Agent，缺了直接 403。</summary>
    private const string UserAgent = "Bodian-WinUI";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    private readonly HttpClient _http;
    private readonly ILogger _logger;

    public GitHubReleaseUpdateService(
        HttpMessageHandler handler,
        AppUpdateOptions? options = null,
        ILogger<GitHubReleaseUpdateService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _http = new HttpClient(handler, disposeHandler: false) { Timeout = Timeout };
        _logger = logger ?? NullLogger<GitHubReleaseUpdateService>.Instance;
        Options = options ?? AppUpdateOptions.Default;
    }

    public AppUpdateOptions Options { get; }

    public async Task<AppUpdateCheckResult> CheckAsync(
        Version currentVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentVersion);

        if (Options.ReleaseApiUri is not { } api)
        {
            return AppUpdateCheckResult.NotConfigured(
                "尚未配置更新源。这是本地私有构建，不检查更新。");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, api);
            request.Headers.UserAgent.ParseAdd(UserAgent);

            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("检查更新被拒：{Status}", (int)response.StatusCode);
                return AppUpdateCheckResult.Failed($"检查失败：更新源返回 {(int)response.StatusCode}");
            }

            var json = await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            return ParseLatestRelease(json, currentVersion, Options.ProjectUrl);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _logger.LogDebug(ex, "检查更新失败");
            return AppUpdateCheckResult.Failed("检查失败：网络不可达");
        }
    }

    public void Dispose() => _http.Dispose();

    /// <summary>
    /// 解析 GitHub 的 <c>releases/latest</c> 响应。
    /// </summary>
    /// <remarks>
    /// <b>抽成静态纯函数是为了能离线单测</b>：喂一段 fixture JSON 就能覆盖
    /// 「tag 带不带 v」「版本相同 / 更新 / 更旧」「字段缺失」这几条，
    /// 不必真的联网、也不必造一个 GitHub 账号。
    /// </remarks>
    /// <param name="json">响应体。</param>
    /// <param name="current">当前版本。</param>
    /// <param name="fallbackDownloadUrl">响应里没有 <c>html_url</c> 时用哪个地址。</param>
    public static AppUpdateCheckResult ParseLatestRelease(
        string json, Version current, string fallbackDownloadUrl)
    {
        ArgumentNullException.ThrowIfNull(current);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return AppUpdateCheckResult.Failed("检查失败：更新源返回的内容看不懂");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("tag_name", out var tagElement)
                || tagElement.GetString() is not { Length: > 0 } tag)
            {
                return AppUpdateCheckResult.Failed("检查失败：更新源返回的内容看不懂");
            }

            // tag 常见写 `v0.2.0`，也可能不带前缀。
            if (!Version.TryParse(tag.TrimStart('v', 'V').Trim(), out var latest))
            {
                return AppUpdateCheckResult.Failed($"检查失败：更新源的版本号无法解析（{tag}）");
            }

            if (latest <= current)
            {
                return AppUpdateCheckResult.UpToDate(current);
            }

            var url = document.RootElement.TryGetProperty("html_url", out var urlElement)
                && urlElement.GetString() is { Length: > 0 } html
                    ? html
                    : fallbackDownloadUrl;

            if (!Uri.TryCreate(url, UriKind.Absolute, out var download))
            {
                return AppUpdateCheckResult.Failed("发现新版本，但更新源没有给出可用的下载地址");
            }

            return AppUpdateCheckResult.Available(
                latest, download, $"发现新版本 {AppUpdateCheckResult.Format(latest)}");
        }
    }
}
