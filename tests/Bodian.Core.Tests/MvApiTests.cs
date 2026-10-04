using Bodian.Core.Api;
using Bodian.Core.Api.Paging;
using Bodian.Core.Tests.Support;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// MV 播放链路（<c>service/mv/info</c>）。响应全部来自 <c>fixtures/</c>，零真实网络。
/// </summary>
/// <remarks>
/// 两个 fixture 都是**真实响应**，只把视频直链里的签名路径抹掉了
/// （见 <c>reverse/findings/15-mv.md</c>）。所以这些测试守的是「解析与信封形状一致」；
/// 直链本身能不能拉，由那次探测直接证明过。
/// </remarks>
public sealed class MvApiTests : IDisposable
{
    private readonly ReplayHandler _handler = new();
    private readonly BodianSession _session = BodianSession.CreateAnonymous();
    private readonly BodianHttpTransport _transport;
    private readonly BodianApi _api;

    public MvApiTests()
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

    private void RespondWith(string fixture) =>
        _handler.Responder = _ => ReplayHandler.Json(Fixtures.Read(fixture));

    private static PagedCursor NewCursor() => new(PagingConvention.OneBased, 30);

    [Fact]
    public async Task WithMv_ReturnsVideoUrl()
    {
        RespondWith("mv-info-228908.json");

        var mv = await _api.GetMvInfoAsync(228908, Ct);

        Assert.NotNull(mv);
        Assert.Equal("bd-bj.kuwo.cn", mv.VideoUrl.Host);
    }

    /// <summary>
    /// 没有 MV 的曲子，服务端回业务码 <c>20048</c>（<c>msg</c> 是「获取MV失败」），不是异常。
    /// </summary>
    /// <remarks>
    /// 这条守的是「这首歌没有 MV」与「请求出错」在 API 层的区别：
    /// 前者是正常结果，界面该把入口收起来；后者要往上抛。
    /// </remarks>
    [Fact]
    public async Task WithoutMv_ReturnsNull()
    {
        RespondWith("mv-info-no-mv.json");

        var mv = await _api.GetMvInfoAsync(202497954, Ct);

        Assert.Null(mv);
    }

    // ── 曲目的 MV 标记 ──────────────────────────────────────────────────────

    /// <summary>
    /// 搜索结果里 <c>isMv</c> 恒为 0，但 <c>vid</c> 非零的歌确实有 MV。
    /// </summary>
    /// <remarks>
    /// 只看 <c>isMv</c> 会把搜索列表里的 MV 全漏掉。见 <c>reverse/findings/15-mv.md</c> §4：
    /// 《晴天》《夜曲》《青花瓷》三首在搜索里都是 <c>isMv=0</c>，详情里都是 <c>isMv=1</c>。
    /// </remarks>
    [Fact]
    public async Task SearchResult_WithVidButNoIsMv_HasMv()
    {
        RespondWith("search-music-list-anon.json");

        var page = await _api.SearchAsync("周杰伦", NewCursor(), Ct);

        Assert.True(page.Items.Single(t => t.Id == 228908).HasMv);
    }

    /// <summary>确实没有 MV 的歌：详情接口给 <c>isMv=0</c> 且 <c>vid=0</c>（实测《三国恋》）。</summary>
    [Fact]
    public async Task TrackWithoutMv_HasNoMv()
    {
        _handler.Responder = _ => ReplayHandler.Json(
            """{"code":200,"msg":"success","data":{"id":202497954,"name":"三国恋","isMv":0,"vid":0}}""");

        var track = await _api.GetTrackAsync(202497954, Ct);

        Assert.NotNull(track);
        Assert.False(track.HasMv);
    }

    // ── 试看 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>playLimitTime &gt; 0</c> 是试看秒数，必须让调用方看得出来，不能当完整 MV 播。
    /// </summary>
    /// <remarks>
    /// 实测那个账号拿到的都是 <c>0</c>（不限），所以这条守的是**形状**：
    /// 字段一旦非零，<c>IsPreviewOnly</c> 必须为真、上限必须是那个秒数。
    /// </remarks>
    [Fact]
    public async Task WithPlayLimit_IsPreviewOnly()
    {
        _handler.Responder = _ => ReplayHandler.Json(
            """{"code":200,"msg":"success","data":{"mv":{"mid":1,"highUrl":"https://h/a.mp4","playLimitTime":30}}}""");

        var mv = await _api.GetMvInfoAsync(1, Ct);

        Assert.NotNull(mv);
        Assert.True(mv.IsPreviewOnly);
        Assert.Equal(TimeSpan.FromSeconds(30), mv.PreviewLimit);
    }

    /// <summary><c>playLimitTime = 0</c> 是「不限」，不是「零秒」。</summary>
    [Fact]
    public async Task WithoutPlayLimit_IsNotPreviewOnly()
    {
        RespondWith("mv-info-228908.json");

        var mv = await _api.GetMvInfoAsync(228908, Ct);

        Assert.NotNull(mv);
        Assert.False(mv.IsPreviewOnly);
        Assert.Null(mv.PreviewLimit);
    }
}
