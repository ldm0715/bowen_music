using Bodian.Core.Api;
using Bodian.Core.Api.Paging;
using Bodian.Core.Tests.Support;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 排行榜。响应来自真实捕获的 <c>fixtures/home-bangNew.json</c> 与
/// <c>fixtures/bang-16-musics.json</c>，零真实网络。
/// </summary>
public sealed class BangApiTests : IDisposable
{
    private readonly ReplayHandler _handler = new();
    private readonly BodianSession _session = BodianSession.CreateAnonymous();
    private readonly BodianHttpTransport _transport;
    private readonly BodianApi _api;

    public BangApiTests()
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

    /// <summary>热歌榜的 id，实测样本里就是它。</summary>
    private const long HotBangId = 16;

    // ── 榜单首页 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task BangSections_MapsRealResponse()
    {
        RespondWith("home-bangNew.json");

        var sections = await _api.GetBangSectionsAsync(Ct);

        Assert.NotEmpty(sections);
        Assert.Contains(sections, section => section.Title == "置顶位");
        Assert.Contains(sections, section => section.Title == "热力榜");

        var top = sections.First(section => section.Title == "置顶位");

        Assert.NotEmpty(top.Bangs);
        Assert.Equal(HotBangId, top.Bangs[0].Id);
        Assert.Equal("热歌榜", top.Bangs[0].Name);
    }

    /// <summary>
    /// <c>data</c> 是**裸数组**，不是 <c>{moduleList: [...]}</c> 那种包一层的对象。
    /// </summary>
    /// <remarks>与 <c>service/home/index</c> 的形状不同，读错会得到「一个榜都没有」。</remarks>
    [Fact]
    public async Task BangSections_ReadsBareArray()
    {
        RespondWith("home-bangNew.json");

        Assert.NotEmpty(await _api.GetBangSectionsAsync(Ct));
    }

    /// <summary>
    /// 「H5榜单」那组里的条目**没有 id**（是外部 H5 链接），必须滤掉。
    /// </summary>
    /// <remarks>
    /// 不滤的话这些条目会变成一张张「取不了曲目」的空榜 ——
    /// 界面上是几个点进去什么都没有的标题。
    /// </remarks>
    [Fact]
    public async Task BangSections_SkipsEntriesWithoutId()
    {
        RespondWith("home-bangNew.json");

        var sections = await _api.GetBangSectionsAsync(Ct);

        Assert.All(sections, section =>
            Assert.All(section.Bangs, bang => Assert.True(bang.Id > 0)));

        // H5 那组因此整个被丢掉（它的条目全都没有 id），不该留一个空分组。
        Assert.DoesNotContain(sections, section => section.Bangs.Count == 0);
    }

    /// <summary>每个榜都带前几首预览 —— 界面在「更多」之前的兜底。</summary>
    [Fact]
    public async Task BangSections_CarryPreviewTracks()
    {
        RespondWith("home-bangNew.json");

        var sections = await _api.GetBangSectionsAsync(Ct);
        var hot = sections.SelectMany(section => section.Bangs).First(bang => bang.Id == HotBangId);

        Assert.NotEmpty(hot.PreviewTracks);
        Assert.False(string.IsNullOrWhiteSpace(hot.PreviewTracks[0].Title));
        Assert.False(string.IsNullOrWhiteSpace(hot.UpdateText));
    }

    [Fact]
    public async Task BangSections_SendsNoParameters()
    {
        RespondWith("home-bangNew.json");

        await _api.GetBangSectionsAsync(Ct);

        Assert.Contains("service/home/bangNew?", _handler.LastRequest.Url);
    }

    // ── 单个榜 ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task BangTracks_MapsRealResponse()
    {
        RespondWith("bang-16-musics.json");

        var page = await _api.GetBangTracksAsync(HotBangId, new PagedCursor(PagingConvention.OneBased, 10), Ct);

        Assert.NotEmpty(page.Items);
        Assert.False(string.IsNullOrWhiteSpace(page.Items[0].Title));
        Assert.False(string.IsNullOrWhiteSpace(page.Items[0].ArtistText));
    }

    /// <summary>
    /// 榜是**一百首**（实测 <c>total</c> 是 100），所以分页必须真的翻得动。
    /// </summary>
    /// <remarks>
    /// 这条守的是「详情页只显示第一页」：界面上表现为「榜只有 20 首」，
    /// 而用户知道它有一百首。
    /// </remarks>
    [Fact]
    public async Task BangTracks_TotalIsOneHundred()
    {
        RespondWith("bang-16-musics.json");

        var page = await _api.GetBangTracksAsync(HotBangId, new PagedCursor(PagingConvention.OneBased, 10), Ct);

        Assert.Equal(100, page.Total);
    }

    [Fact]
    public async Task BangTracks_SendsOneBasedPaging()
    {
        RespondWith("bang-16-musics.json");

        await _api.GetBangTracksAsync(HotBangId, new PagedCursor(PagingConvention.OneBased, 10), Ct);

        Assert.Contains("service/bang/16/musics?", _handler.LastRequest.Url);
        Assert.Contains("pn=1", _handler.LastRequest.Url);
        Assert.Contains("rn=10", _handler.LastRequest.Url);
    }

    [Fact]
    public async Task BangTracks_SecondPageAdvances()
    {
        RespondWith("bang-16-musics.json");

        var cursor = new PagedCursor(PagingConvention.OneBased, 10);

        await _api.GetBangTracksAsync(HotBangId, cursor, Ct);
        await _api.GetBangTracksAsync(HotBangId, cursor, Ct);

        Assert.Contains("pn=2", _handler.LastRequest.Url);
    }

    /// <summary>空页让游标到底 —— 一百首翻完之后不该再请求。</summary>
    [Fact]
    public async Task BangTracks_EmptyPage_ExhaustsCursor()
    {
        _handler.Responder = _ => ReplayHandler.Json(
            """{"code":200,"msg":"success","data":{"total":0,"musics":[]}}""");

        var cursor = new PagedCursor(PagingConvention.OneBased, 10);
        var page = await _api.GetBangTracksAsync(HotBangId, cursor, Ct);

        Assert.Empty(page.Items);
        Assert.True(cursor.Exhausted);
    }

    // ── 前置条件 ────────────────────────────────────────────────────────────

    /// <summary>排行榜不要求登录 —— 实测匿名可见。</summary>
    [Fact]
    public async Task Bangs_WorkWithoutLogin()
    {
        _session.Clear();
        RespondWith("home-bangNew.json");

        Assert.NotEmpty(await _api.GetBangSectionsAsync(Ct));
    }

    [Fact]
    public async Task BangTracks_NonPositiveId_Throws()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _api.GetBangTracksAsync(0, new PagedCursor(PagingConvention.OneBased), Ct));
    }

    [Fact]
    public async Task BangTracks_NullCursor_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _api.GetBangTracksAsync(HotBangId, null!, Ct));
    }
}
