using System.Net;
using Bodian.Core.Models;
using Bodian.Core.Services.Implementations;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 检查更新。**零真实网络** —— 处理器是桩，解析走静态纯函数。
/// </summary>
public sealed class AppUpdateTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Version Current => new(0, 1, 0);

    private static AppUpdateOptions Configured => new()
    {
        Owner = "someone",
        Repository = "bodian",
        ProjectUrl = "https://github.com/someone/bodian",
    };

    private const string LatestRelease = """
        { "tag_name": "v0.2.0", "html_url": "https://github.com/someone/bodian/releases/tag/v0.2.0" }
        """;

    // ── 配置 ───────────────────────────────────────────────────────────────

    [Fact]
    public void DefaultOptions_PointAtThePublicRepository()
    {
        // 应用实际使用的就是这份配置，「关于」页两个按钮的可用性由它决定。
        // 改了仓库地址这里会红 —— 这条就是防止配置点和测试各说各话。
        Assert.True(AppUpdateOptions.Default.IsConfigured);
        Assert.Equal(
            "https://api.github.com/repos/ldm0715/bowen_music/releases/latest",
            AppUpdateOptions.Default.ReleaseApiUri!.AbsoluteUri);
        Assert.Equal(
            "https://github.com/ldm0715/bowen_music",
            AppUpdateOptions.Default.ProjectUri!.AbsoluteUri);
    }

    [Fact]
    public void BlankOptions_AreNotConfigured()
    {
        // 空配置这条路必须留着：测试与本地私有构建会把它换进去。
        var blank = new AppUpdateOptions();
        Assert.False(blank.IsConfigured);
        Assert.Null(blank.ReleaseApiUri);
        Assert.Null(blank.ProjectUri);
    }

    [Fact]
    public void ConfiguredOptions_ProduceTheReleaseApiUri()
    {
        Assert.True(Configured.IsConfigured);
        Assert.Equal(
            "https://api.github.com/repos/someone/bodian/releases/latest",
            Configured.ReleaseApiUri!.AbsoluteUri);
    }

    [Fact]
    public async Task Check_WhenNotConfigured_ReturnsNotConfiguredWithoutAnyRequest()
    {
        var handler = StubHandler.Responding(OK(LatestRelease));
        // 显式传空配置：不能靠「Default 恰好是空的」来摆出未配置状态，
        // Default 指向公开仓库，那样这条用例会变成在测已配置的分支。
        using var service = new GitHubReleaseUpdateService(handler, new AppUpdateOptions());

        var result = await service.CheckAsync(Current, Ct);

        Assert.Equal(AppUpdateStatus.NotConfigured, result.Status);
        Assert.NotEmpty(result.Message);

        // ★ 一条出站请求都不能发。这条是刻意验证的：未配置时进程不该产生任何网络活动。
        Assert.Equal(0, handler.RequestCount);
    }

    // ── 解析 ───────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_NewerTag_ReportsAnUpdate()
    {
        var result = GitHubReleaseUpdateService.ParseLatestRelease(LatestRelease, Current, "");

        Assert.Equal(AppUpdateStatus.UpdateAvailable, result.Status);
        Assert.Equal(new Version(0, 2, 0), result.LatestVersion);
        Assert.Equal("https://github.com/someone/bodian/releases/tag/v0.2.0", result.DownloadUri!.AbsoluteUri);
    }

    [Fact]
    public void Parse_TagWithoutTheVPrefix_StillParses()
    {
        var json = """{ "tag_name": "0.3.1" }""";

        var result = GitHubReleaseUpdateService.ParseLatestRelease(json, Current, "https://example.invalid/dl");

        Assert.Equal(AppUpdateStatus.UpdateAvailable, result.Status);
        Assert.Equal(new Version(0, 3, 1), result.LatestVersion);

        // 没有 html_url 时退回给它的地址。
        Assert.Equal("https://example.invalid/dl", result.DownloadUri!.AbsoluteUri);
    }

    [Theory]
    [InlineData("v0.1.0")]
    [InlineData("v0.0.9")]
    [InlineData("0.1.0")]
    public void Parse_SameOrOlderTag_ReportsUpToDate(string tag)
    {
        var json = $$"""{ "tag_name": "{{tag}}" }""";

        var result = GitHubReleaseUpdateService.ParseLatestRelease(json, Current, "");

        Assert.Equal(AppUpdateStatus.UpToDate, result.Status);
        Assert.Contains("0.1.0", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_BrokenJson_FailsWithoutThrowing()
    {
        var result = GitHubReleaseUpdateService.ParseLatestRelease("{ not json", Current, "");

        Assert.Equal(AppUpdateStatus.Failed, result.Status);
        Assert.NotEmpty(result.Message);
    }

    [Fact]
    public void Parse_MissingTagName_FailsWithoutThrowing()
    {
        var result = GitHubReleaseUpdateService.ParseLatestRelease("""{ "name": "v0.2.0" }""", Current, "");

        Assert.Equal(AppUpdateStatus.Failed, result.Status);
    }

    [Fact]
    public void Parse_UnparseableVersion_FailsWithoutThrowing()
    {
        var result = GitHubReleaseUpdateService.ParseLatestRelease("""{ "tag_name": "nightly" }""", Current, "");

        Assert.Equal(AppUpdateStatus.Failed, result.Status);
        Assert.Contains("nightly", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_NoUsableDownloadUrl_FailsInsteadOfHandingOutABrokenLink()
    {
        var json = """{ "tag_name": "v9.9.9" }""";

        var result = GitHubReleaseUpdateService.ParseLatestRelease(json, Current, fallbackDownloadUrl: "");

        Assert.Equal(AppUpdateStatus.Failed, result.Status);
    }

    // ── 端到端（处理器仍是桩） ──────────────────────────────────────────────

    [Fact]
    public async Task Check_WhenConfigured_ParsesTheResponse()
    {
        var handler = StubHandler.Responding(OK(LatestRelease));
        using var service = new GitHubReleaseUpdateService(handler, Configured);

        var result = await service.CheckAsync(Current, Ct);

        Assert.Equal(AppUpdateStatus.UpdateAvailable, result.Status);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Check_OnHttpError_ReportsAFriendlyFailure()
    {
        var handler = StubHandler.Responding(new HttpResponseMessage(HttpStatusCode.Forbidden));
        using var service = new GitHubReleaseUpdateService(handler, Configured);

        var result = await service.CheckAsync(Current, Ct);

        Assert.Equal(AppUpdateStatus.Failed, result.Status);

        // 给用户看的是人话，不是异常串。
        Assert.DoesNotContain("Exception", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Check_OnNetworkFailure_ReportsAFriendlyFailure()
    {
        var handler = StubHandler.Throwing(new HttpRequestException("断网了"));
        using var service = new GitHubReleaseUpdateService(handler, Configured);

        var result = await service.CheckAsync(Current, Ct);

        Assert.Equal(AppUpdateStatus.Failed, result.Status);
        Assert.DoesNotContain("Exception", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_ShowsThreeSegmentsOnly()
    {
        Assert.Equal("0.1.0", AppUpdateCheckResult.Format(new Version(0, 1, 0, 7)));
        Assert.Equal("1.2.3", AppUpdateCheckResult.Format(new Version(1, 2, 3)));
    }

    private static HttpResponseMessage OK(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body) };

    /// <summary>可编程的 HTTP 桩：要什么响应给什么，并数一数被请求了几次。</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _respond;

        private StubHandler(Func<HttpResponseMessage> respond) => _respond = respond;

        public int RequestCount { get; private set; }

        public static StubHandler Responding(HttpResponseMessage response) => new(() => response);

        public static StubHandler Throwing(Exception exception) => new(() => throw exception);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(_respond());
        }
    }
}
