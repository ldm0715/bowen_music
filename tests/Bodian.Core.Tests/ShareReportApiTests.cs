using Bodian.Core.Api;
using Bodian.Core.Tests.Support;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 分享上报的请求形状与业务码分支。零真实网络。
/// </summary>
/// <remarks>
/// <b>这是一个写操作</b>：文档 2.8（2026-10-03 实测）确认每调一次该曲目的 <c>share</c> 就 +1，
/// 「复制链接」也不例外。这里只断言报文与分支，不真发请求。
/// </remarks>
public sealed class ShareReportApiTests : IDisposable
{
    private const long MusicId = 381027;

    private readonly ReplayHandler _handler = new();
    private readonly BodianSession _session = BodianSession.CreateAnonymous();
    private readonly BodianHttpTransport _transport;
    private readonly BodianApi _api;

    public ShareReportApiTests()
    {
        _transport = new BodianHttpTransport(
            _handler,
            new BodianTransportOptions(),
            _session,
            new FakeDeviceIdentity(),
            FixedTimeProvider.Golden);

        _api = new BodianApi(_transport, _session, new FakeDeviceIdentity());
    }

    public void Dispose() => _transport.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Report_SendsTheFourDocumentedQueryParameters()
    {
        var outcome = await _api.ReportTrackShareAsync(MusicId, Ct);

        Assert.Equal(ShareOutcome.Succeeded, outcome);

        var request = _handler.LastRequest;
        Assert.Equal("GET", request.Method);
        Assert.Contains("service/share/text?", request.Url, StringComparison.Ordinal);
        Assert.Contains("shareTo=5", request.Url, StringComparison.Ordinal);
        Assert.Contains("shareSource=0", request.Url, StringComparison.Ordinal);
        Assert.Contains($"sourceId={MusicId}", request.Url, StringComparison.Ordinal);
        Assert.Contains("playlistType=4", request.Url, StringComparison.Ordinal);
        // 四个参数都在 query 里，没有 body。
        Assert.Null(request.Body);
    }

    [Fact]
    public async Task Report_IsSigned()
    {
        await _api.ReportTrackShareAsync(MusicId, Ct);

        Assert.Contains("sign=", _handler.LastRequest.Url, StringComparison.Ordinal);
        Assert.Contains("timestamp=", _handler.LastRequest.Url, StringComparison.Ordinal);
    }

    /// <summary>23006 是「该歌曲暂不支持分享」，不是失败 —— 不该抛异常。</summary>
    [Fact]
    public async Task Report_MapsUnsupportedWithoutThrowing()
    {
        _handler.Responder = _ => ReplayHandler.Json("""{"code":23006,"msg":"该歌曲暂不支持分享"}""");

        Assert.Equal(ShareOutcome.Unsupported, await _api.ReportTrackShareAsync(MusicId, Ct));
    }

    /// <summary>其它非 200 业务码走异常路径，由调用方兜住。</summary>
    [Fact]
    public async Task Report_ThrowsOnOtherBusinessErrors()
    {
        _handler.Responder = _ => ReplayHandler.Json("""{"code":20012,"msg":"歌曲已下线"}""");

        await Assert.ThrowsAsync<BodianApiException>(() => _api.ReportTrackShareAsync(MusicId, Ct));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Report_RejectsNonPositiveMusicIds(long musicId)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _api.ReportTrackShareAsync(musicId, Ct));
    }
}
