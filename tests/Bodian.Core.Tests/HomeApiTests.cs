using Bodian.Core.Api;
using Bodian.Core.Models.Home;
using Bodian.Core.Tests.Support;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 发现页。响应全部来自 <c>fixtures/</c>，零真实网络。
/// </summary>
/// <remarks>
/// <b>这些 fixture 是真实捕获的</b>（探针 <c>--save</c> 存下来的原始响应，已脱敏），
/// 不是照文档写的 —— 这是本项目第一次在曲库之外拿到端到端样本。
/// 模块的类型与文件名的对应：1→type4、2→type5、10→type10、12→type11、4→type6、11→type7。
/// </remarks>
public sealed class HomeApiTests : IDisposable
{
    private readonly ReplayHandler _handler = new();
    private readonly BodianSession _session = BodianSession.CreateAnonymous();
    private readonly BodianHttpTransport _transport;
    private readonly BodianApi _api;

    public HomeApiTests()
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

    private static HomeModule Module(int id, int type) => new(id, type, $"模块 {id}", 0);

    // ── 布局 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HomeModules_MapsRealLayout()
    {
        RespondWith("home-index-raw.json");

        var modules = await _api.GetHomeModulesAsync(Ct);

        Assert.Equal(12, modules.Count);

        var first = modules[0];

        Assert.Equal(1, first.Id);
        Assert.Equal(4, first.Type);
        Assert.Equal("个性化歌单", first.Name);
    }

    /// <summary>布局端点**无参数、不签名**。</summary>
    [Fact]
    public async Task HomeModules_SendsNoParameters()
    {
        RespondWith("home-index-raw.json");

        await _api.GetHomeModulesAsync(Ct);

        // 传输层拼 URL 的形态是 BaseAddress + path + "?" + query，query 为空时末尾仍有一个 ?。
        Assert.Contains("service/home/index?", _handler.LastRequest.Url);
    }

    /// <summary>
    /// 布局里哪些模块本项目能渲染。
    /// </summary>
    /// <remarks>
    /// 实测 12 个模块里能渲染 5 个（类型 3/4/5/10/11）。
    /// <b>排行榜（type 2）有意不渲染</b>：它是侧栏的独立一项，数据源是 <c>home/bangNew</c>，
    /// 放在发现页里是重复入口。
    /// 另外 6 个是轮播图、广告、实验室、音乐日历、数字专辑馆、听点不一样的视频 ——
    /// 形状复杂且不是内容流。<b>换模块表时这条会挂，那是提醒你布局变了。</b>
    /// </remarks>
    [Fact]
    public async Task HomeModules_KnowsWhichOnesAreSupported()
    {
        RespondWith("home-index-raw.json");

        var modules = await _api.GetHomeModulesAsync(Ct);

        Assert.Equal(5, modules.Count(module => module.IsSupported));

        // 排行榜在布局里仍然存在（服务端给的），只是本客户端不渲染它。
        Assert.Contains(modules, module => module.Type == 2);
        Assert.DoesNotContain(modules, module => module.Type == 2 && module.IsSupported);
    }

    // ── type 4：曲目分组 ────────────────────────────────────────────────────

    /// <summary>
    /// <c>songList</c> 在 type 4 里是**曲目分组**（组有 title + songs）。
    /// </summary>
    [Fact]
    public async Task SongGroups_ProducesSectionsOfTracks()
    {
        RespondWith("home-module-1.json");

        var feed = await _api.GetHomeModuleAsync(Module(1, 4), Ct);

        Assert.NotNull(feed);
        Assert.Equal("个性化歌单", feed.Title);
        Assert.NotEmpty(feed.Sections);

        var section = feed.Sections[0];

        Assert.False(string.IsNullOrWhiteSpace(section.Title));
        Assert.NotEmpty(section.Cards);
        Assert.All(section.Cards, card => Assert.True(card.IsPlayable));
        Assert.All(section.Cards, card => Assert.False(card.IsPlaylist));
    }

    // ── type 5：**同一个键，语义不同** ──────────────────────────────────────

    /// <summary>
    /// <c>songList</c> 在 type 5 里是**歌单卡片**，不是曲目分组。
    /// </summary>
    /// <remarks>
    /// 这条守着整个按 type 分派的设计：只按键名解析的话，这里会得到一堆「没有 songs 的分组」，
    /// 界面上表现为宝藏歌单库整段空白。
    /// </remarks>
    [Fact]
    public async Task PlaylistCards_SameKeyDifferentMeaning()
    {
        RespondWith("home-module-2.json");

        var feed = await _api.GetHomeModuleAsync(Module(2, 5), Ct);

        Assert.NotNull(feed);

        var section = Assert.Single(feed.Sections);

        Assert.NotEmpty(section.Cards);
        Assert.All(section.Cards, card => Assert.True(card.IsPlaylist));
        Assert.All(section.Cards, card => Assert.False(card.IsPlayable));
        Assert.All(section.Cards, card => Assert.False(string.IsNullOrWhiteSpace(card.Title)));
    }

    /// <summary>
    /// 歌单卡片必须原样带上服务端给的来源。
    /// </summary>
    /// <remarks>
    /// <b>实测这个值是 <c>13</c>，不是文档里公开集合的 <c>4</c>。</b>
    /// 所以映射**不能自作主张归一化成 4** —— 那会让取曲目时填错 <c>source</c>，
    /// 而服务端对不上的值只回空、不报错，表现就是「点进去是空歌单」。
    /// <para>
    /// 顺带记一条未验证项：<c>source=13</c> 能不能被
    /// <c>service/playlist/{id}/musicList</c> 接受，本项目还没实测过。
    /// </para>
    /// </remarks>
    [Fact]
    public async Task PlaylistCards_CarryTheirSourceVerbatim()
    {
        RespondWith("home-module-2.json");

        var feed = await _api.GetHomeModuleAsync(Module(2, 5), Ct);

        Assert.All(feed!.Sections[0].Cards, card => Assert.Equal(13, card.Playlist!.SourceType));
    }

    // ── type 10：一整片曲目 ─────────────────────────────────────────────────

    [Fact]
    public async Task MusicList_ProducesOneUntitledSection()
    {
        RespondWith("home-module-10.json");

        var feed = await _api.GetHomeModuleAsync(Module(10, 10), Ct);

        Assert.NotNull(feed);

        var section = Assert.Single(feed.Sections);

        // 一整片没有小标题 —— 界面靠这个空串决定不画标题行。
        Assert.Equal("", section.Title);
        Assert.Equal(15, section.Cards.Count);
        Assert.All(section.Cards, card => Assert.True(card.IsPlayable));
    }

    // ── type 11：也是曲目分组，但组名在另一个键上 ──────────────────────────

    /// <summary>type 11 的组名在 <c>passRecName</c> 上，不在 <c>title</c> 上。</summary>
    [Fact]
    public async Task SongGroups_ReadsTheOtherTitleKey()
    {
        RespondWith("home-module-12.json");

        var feed = await _api.GetHomeModuleAsync(Module(12, 11), Ct);

        Assert.NotNull(feed);
        Assert.NotEmpty(feed.Sections);
        Assert.All(feed.Sections, section => Assert.False(string.IsNullOrWhiteSpace(section.Title)));
    }

    // ── AI 歌单（个性化歌单点进去）─────────────────────────────────────────

    /// <summary>
    /// 「个性化歌单」的分组要带上 <c>AiIndex</c> —— 那个 <c>id</c> 就是
    /// <c>aiPlaylistDetail</c> 的 <c>index</c>。
    /// </summary>
    /// <remarks>
    /// 实测 <c>index=0</c> 返回的 <c>title</c> 与那一组的标题逐字相同（「潮趣日推」）。
    /// </remarks>
    [Fact]
    public async Task SongGroups_Type4_CarryAiIndex()
    {
        RespondWith("home-module-1.json");

        var feed = await _api.GetHomeModuleAsync(Module(1, 4), Ct);

        Assert.NotNull(feed);
        Assert.All(feed.Sections, section => Assert.NotNull(section.Ai));

        // index 就是位置，第一组是 0 —— 所以**不能**用「大于 0」来判断可点。
        var indices = feed.Sections.Select(section => section.Ai!.Index).ToList();
        Assert.Equal(new List<int> { 0, 1, 2, 3 }, indices);
    }

    /// <summary>
    /// type 11「你的主题歌单」的分组**也**可打开，而且必须带上它自己的 <c>passRecName</c>。
    /// </summary>
    /// <remarks>
    /// <b>两个模块的 index 都是 0/1/2/3</b> —— 只传 index 的话，「你的主题歌单」的每一组
    /// 都会打开「个性化歌单」里那个同号歌单（实测 index=0 返回的是「潮趣日推」而不是「华文流行极致」）。
    /// <c>passRecName</c> 才是区分它们的东西。
    /// </remarks>
    [Fact]
    public async Task SongGroups_Type11_CarryAiRefWithPassRecName()
    {
        RespondWith("home-module-12.json");

        var feed = await _api.GetHomeModuleAsync(Module(12, 11), Ct);

        Assert.NotNull(feed);
        Assert.All(feed.Sections, section => Assert.NotNull(section.Ai));

        var indices = feed.Sections.Select(section => section.Ai!.Index).ToList();
        var passes = feed.Sections.Select(section => section.Ai!.PassRecName).ToList();

        Assert.Equal(new List<int> { 0, 1, 2, 3 }, indices);
        Assert.Equal(new List<string> { "0_15", "4_119", "5_15", "2194" }, passes);
    }

    /// <summary>
    /// 「个性化歌单」的分组没有 <c>passRecName</c> 字段，传空串 —— 实测服务端接受。
    /// </summary>
    [Fact]
    public async Task SongGroups_Type4_CarryEmptyPassRecName()
    {
        RespondWith("home-module-1.json");

        var feed = await _api.GetHomeModuleAsync(Module(1, 4), Ct);

        Assert.NotNull(feed);
        Assert.All(feed.Sections, section => Assert.Equal("", section.Ai!.PassRecName));
    }

    [Fact]
    public async Task AiPlaylist_MapsRealResponse()
    {
        RespondWith("ai-playlist-0.json");

        var playlist = await _api.GetAiPlaylistAsync(0, "", Ct);

        Assert.NotNull(playlist);
        Assert.Equal("潮趣日推", playlist.Title);
        Assert.False(string.IsNullOrWhiteSpace(playlist.Subtitle));

        // 实测 30 首，而模块里的预览只有 3 首 —— 这就是「点进去」的意义。
        Assert.Equal(30, playlist.Tracks.Count);
        Assert.False(string.IsNullOrWhiteSpace(playlist.Tracks[0].Title));
    }

    /// <summary>
    /// <c>index</c> 走 query，且**整条 URL 必须精确等于这个形态**。
    /// </summary>
    /// <remarks>
    /// <b>这条一开始写成了 <c>Contains</c>，于是漏掉了一个真 bug：</b> 当时把
    /// <c>?index=N</c> 塞进了路径，而传输层还会再拼一次 query，最终发出去的是
    /// <c>...aiPlaylistDetail?index=3?</c> —— 末尾多一个 <c>?</c>，服务端回 <b>400</b>。
    /// 而 <c>Contains</c> 那个子串照样成立，测试全绿。
    /// <para>
    /// <b>教训：拼 URL 这类测试要用 <c>Equal</c>，不要用 <c>Contains</c></b> ——
    /// 多出来的字符只有精确比对才看得见。
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AiPlaylist_SendsIndexAsQuery()
    {
        RespondWith("ai-playlist-0.json");

        await _api.GetAiPlaylistAsync(3, "2194", Ct);

        var url = _handler.LastRequest.Url;

        Assert.StartsWith(
            "https://bd-api.kuwo.cn/api/service/home/aiPlaylistDetail?index=3&passRecName=2194",
            url);

        // ★ 整条 URL 只该有一个 ?。
        //   把 ?index=N 写进路径、再由传输层拼一次 query，会得到末尾多一个 ? 的形态
        //   （...aiPlaylistDetail?index=3?），服务端回 400。
        //   这条断言才是真正守住它的那个 —— StartsWith / Contains 都看不见多出来的字符。
        Assert.Equal(1, url.Count(character => character == '?'));
    }

    [Fact]
    public async Task AiPlaylist_NegativeIndex_Throws()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _api.GetAiPlaylistAsync(-1, "", Ct));
    }

    /// <summary>这个端点匿名也能取（发现页本身就不要求登录）。</summary>
    [Fact]
    public async Task AiPlaylist_WorksWithoutLogin()
    {
        _session.Clear();
        RespondWith("ai-playlist-0.json");

        Assert.NotNull(await _api.GetAiPlaylistAsync(0, "", Ct));
    }

    // ── 不支持的模块 ────────────────────────────────────────────────────────

    /// <summary>
    /// 不支持的类型返回 <c>null</c>，**而且一个请求都不发**。
    /// </summary>
    /// <remarks>
    /// 类型 6（波点实验室）与 7（听点不一样的视频）都能拿到样本，本项目不渲染它们。
    /// 不发请求这点很重要：不做的事不该花流量。
    /// </remarks>
    [Theory]
    [InlineData(4, 6)]
    [InlineData(11, 7)]
    [InlineData(6, 1)]
    [InlineData(0, 8)]

    // 排行榜（type 2）本客户端有意不渲染 —— 它是侧栏的独立一项。
    [InlineData(5, 2)]
    public async Task UnsupportedModule_ReturnsNullWithoutRequesting(int id, int type)
    {
        RespondWith("home-module-4.json");

        var before = _handler.Requests.Count;
        var feed = await _api.GetHomeModuleAsync(Module(id, type), Ct);

        Assert.Null(feed);
        Assert.Equal(before, _handler.Requests.Count);
    }

    // ── 参数与前置条件 ──────────────────────────────────────────────────────

    [Fact]
    public async Task HomeModule_SendsModuleId()
    {
        RespondWith("home-module-10.json");

        await _api.GetHomeModuleAsync(Module(10, 10), Ct);

        Assert.Contains("service/home/module?moduleId=10", _handler.LastRequest.Url);
    }

    /// <summary>发现页匿名也能看 —— 实测 <c>home/index</c> 与 <c>home/module</c> 都不要求登录。</summary>
    [Fact]
    public async Task Discover_WorksWithoutLogin()
    {
        _session.Clear();
        RespondWith("home-index-raw.json");

        Assert.NotEmpty(await _api.GetHomeModulesAsync(Ct));
    }

    [Fact]
    public async Task NullModule_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _api.GetHomeModuleAsync(null!, Ct));
    }
}
