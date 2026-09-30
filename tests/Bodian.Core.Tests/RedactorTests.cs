using Bodian.Core.Api;
using Bodian.Core.Diagnostics;
using Bodian.Core.Tests.Support;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 日志脱敏。两条要求：凭据一个都不能漏，正常的调试信息不能被误伤。
/// </summary>
public sealed class RedactorTests
{
    // ── 字符串级 ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("token=tok_secret", "token=<redacted>")]
    [InlineData("freeSign=abcdef", "freeSign=<redacted>")]
    [InlineData("sign=bb658ff99a35b3d19570e0d783e9143e", "sign=<redacted>")]
    [InlineData("uid=50303440", "uid=<redacted>")]
    [InlineData("devid=0123456789abcdef0123456789abcdef", "devid=<redacted>")]
    [InlineData("qimei36=0123456789abcdef0123456789abcdef", "qimei36=<redacted>")]
    public void Redact_ReplacesCredentialValues(string input, string expected)
        => Assert.Equal(expected, LogRedactor.Redact(input));

    /// <summary>
    /// **空值不动。** 未登录时 <c>token=</c>、黄金用例里的 <c>sign=</c> 都必须保持原样——
    /// 否则脱敏后的 query 串就没法与代码里的常量逐字比对了。
    /// </summary>
    [Fact]
    public void Redact_LeavesEmptyValuesAlone()
    {
        const string query = "musicId=228908&uid=-1&token=&timestamp=1790750540920&sign=";

        var redacted = LogRedactor.Redact(query);

        Assert.Equal(query, redacted);
        Assert.DoesNotContain("<redacted>", redacted);
    }

    /// <summary><c>uid=-1</c> 是匿名标记，不是凭据，留着便于看日志。</summary>
    [Fact]
    public void Redact_KeepsAnonymousUid()
    {
        Assert.Equal("uid=-1&rn=30", LogRedactor.Redact("uid=-1&rn=30"));
    }

    [Fact]
    public void Redact_ReplacesEveryOccurrence()
    {
        var redacted = LogRedactor.Redact("token=a&x=1 token=b");

        Assert.DoesNotContain("token=a", redacted);
        Assert.DoesNotContain("token=b", redacted);
        Assert.Contains("x=1", redacted);
    }

    [Fact]
    public void Redact_HandlesJsonShape()
    {
        const string body = """{"musicId":228908,"freeSign":"deadbeef","token":"tok_x"}""";

        var redacted = LogRedactor.Redact(body);

        Assert.DoesNotContain("deadbeef", redacted);
        Assert.DoesNotContain("tok_x", redacted);
        Assert.Contains("\"musicId\":228908", redacted);                    // 非凭据字段不动
        Assert.Contains("\"freeSign\":\"<redacted>\"", redacted);
    }

    [Fact]
    public void Redact_HandlesUrlWithQuery()
    {
        var redacted = LogRedactor.Redact(
            "https://ga-sycdn.kuwo.cn/resource/228908.mp3?token=secret&sign=abc&musicId=228908");

        Assert.DoesNotContain("secret", redacted);
        Assert.DoesNotContain("sign=abc", redacted);
        Assert.Contains("musicId=228908", redacted);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Redact_HandlesEmptyInput(string? input)
        => Assert.Equal(string.Empty, LogRedactor.Redact(input));

    /// <summary>不含凭据的普通文本不该被改动。</summary>
    [Fact]
    public void Redact_LeavesOrdinaryTextAlone()
    {
        const string text = "GET service/music/info → HTTP 200 code 200，42 ms";

        Assert.Equal(text, LogRedactor.Redact(text));
    }

    // ── SafeUrl ─────────────────────────────────────────────────────────────

    [Fact]
    public void SafeUrl_DropsQueryEntirely()
    {
        var url = SafeUrl.From(new Uri(
            "https://bd-api.kuwo.cn/api/service/music/info?uid=50303440&token=tok_secret&sign=abc"));

        Assert.Equal("https://bd-api.kuwo.cn/api/service/music/info", url.ToString());
        Assert.DoesNotContain("token", url.ToString());
        Assert.DoesNotContain("?", url.ToString());
    }

    [Fact]
    public void SafeUrl_DropsFragmentAndPortlessHostIsKept()
    {
        var url = SafeUrl.From(new Uri("https://mlyric.kuwo.cn/mobi.s?f=bodian&q=AbCd#frag"));

        Assert.Equal("https://mlyric.kuwo.cn/mobi.s", url.ToString());
    }

    /// <summary>解析不了就返回占位串——**原始文本可能就是泄露源**，不能原样回吐。</summary>
    [Fact]
    public void SafeUrl_DoesNotEchoUnparsableInput()
    {
        const string notAUrl = "token=secret not a url";

        var url = SafeUrl.From(notAUrl);

        Assert.DoesNotContain("secret", url.ToString());
        Assert.Equal("?://unparsable", url.ToString());
    }

    // ── 工厂级 ──────────────────────────────────────────────────────────────

    private static (ILoggerFactory Factory, CapturingLoggerProvider Provider) BuildRedactingFactory()
    {
        var provider = new CapturingLoggerProvider();
        var inner = LoggerFactory.Create(builder => builder.AddProvider(provider));
        return (new RedactingLoggerFactory(inner), provider);
    }

    /// <summary>结构化日志的字符串值也必须脱敏——不能只靠消息文本那一层。</summary>
    [Fact]
    public void LoggerFactory_RedactsStructuredStringValues()
    {
        var (factory, provider) = BuildRedactingFactory();
        using var _ = factory;
        var logger = factory.CreateLogger("test");

        logger.LogInformation("取音源 {Url}", "https://cdn/x.mp3?token=tok_secret&musicId=228908");

        var entry = Assert.Single(provider.Entries);
        Assert.DoesNotContain("tok_secret", entry.Message);
        Assert.DoesNotContain("tok_secret", string.Join('|', entry.State.Select(p => p.Value?.ToString())));
        Assert.Contains("musicId=228908", entry.Message);
    }

    /// <summary>非字符串属性与键名不能被改动，否则结构化查询就废了。</summary>
    [Fact]
    public void LoggerFactory_KeepsNonStringValuesAndKeys()
    {
        var (factory, provider) = BuildRedactingFactory();
        using var _ = factory;
        var logger = factory.CreateLogger("test");

        logger.LogInformation("业务码 {Code}，耗时 {Elapsed} ms", 200, 42);

        var entry = Assert.Single(provider.Entries);
        Assert.Equal(200, entry.State.Single(p => p.Key == "Code").Value);
        Assert.Equal(42, entry.State.Single(p => p.Key == "Elapsed").Value);
    }

    /// <summary>这是这条防线存在的理由：调用点**什么都没做**，凭据也不该漏出去。</summary>
    [Fact]
    public void LoggerFactory_RedactsEvenWhenCallSiteIsCareless()
    {
        var (factory, provider) = BuildRedactingFactory();
        using var _ = factory;
        var logger = factory.CreateLogger("careless");

        // 模拟「有人顺手把整个 URL 塞进日志」——这一层就是为这种事故准备的
        logger.LogDebug("请求 {Url}", "https://bd-api.kuwo.cn/api/play/music/v2/audioUrl?uid=50303440&token=tok_secret&sign=deadbeef");

        Assert.DoesNotContain("tok_secret", provider.AllText);
        Assert.DoesNotContain("deadbeef", provider.AllText);
        Assert.DoesNotContain("50303440", provider.AllText);
    }

    [Fact]
    public void LoggerFactory_ReportsEnabledStateFromInner()
    {
        var (factory, provider) = BuildRedactingFactory();
        using var _ = factory;

        Assert.True(factory.CreateLogger("test").IsEnabled(LogLevel.Information));
        Assert.NotEmpty(provider.Entries.Count.ToString());   // 只为了用一下 provider，避免未使用告警
    }

    /// <summary>
    /// 传输层的日志语句里不该出现 query 或 body——这一层是第一道防线，上面那条工厂是第二道。
    /// </summary>
    [Fact]
    public async Task Transport_DoesNotLogQueryOrBody()
    {
        var provider = new CapturingLoggerProvider();
        using var inner = LoggerFactory.Create(b => b.AddProvider(provider).SetMinimumLevel(LogLevel.Trace));
        using var factory = new RedactingLoggerFactory(inner);

        var handler = new ReplayHandler
        {
            Responder = _ => ReplayHandler.Json("""{"code":200,"msg":"success"}"""),
        };

        var session = BodianSession.CreateAnonymous();
        session.Set("50303440", "tok_secret");

        using var transport = new BodianHttpTransport(
            handler,
            new BodianTransportOptions(),
            session,
            new FakeDeviceIdentity(),
            FixedTimeProvider.Golden,
            factory.CreateLogger<BodianHttpTransport>());

        await transport.SendAsync(
            new BodianRequest
            {
                Path = "play/music/v2/checkRight",
                Query = [new("musicId", "228908")],
                JsonBody = """{"musicId":228908,"freeSign":"deadbeef"}""",
                Signed = true,
            },
            BodianJsonContext.Default.CheckRightDto,
            TestContext.Current.CancellationToken);

        Assert.NotEmpty(provider.Entries);                     // 确实记了日志
        Assert.DoesNotContain("tok_secret", provider.AllText);
        Assert.DoesNotContain("deadbeef", provider.AllText);
        Assert.DoesNotContain("freeSign", provider.AllText);
        Assert.Contains("bd-api.kuwo.cn", provider.AllText);   // 但主机与路径在，能定位问题
    }
}
