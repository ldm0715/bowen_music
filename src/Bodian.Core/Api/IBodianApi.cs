using Bodian.Core.Api.Paging;
using Bodian.Core.Models;
using Bodian.Core.Models.Home;
using Bodian.Core.Models.Lyrics;

namespace Bodian.Core.Api;

/// <summary>
/// 业务门面。UI 只跟它打交道，不直接碰 DTO，也不自己拼请求。
/// </summary>
/// <remarks>
/// <para>
/// 返回的都是 <c>Bodian.Core.Models</c> 里的公开领域模型 —— 底层 DTO 全是 <c>internal</c>，
/// 这是有意的一道墙（见 <c>Dto/README.md</c>）。
/// </para>
/// <para>
/// 分页游标由<b>调用方持有</b>，门面不自己记状态：这样同一个界面上的两组搜索结果互不干扰，
/// 也便于对同一游标做确定性测试。
/// </para>
/// </remarks>
public interface IBodianApi
{
    /// <summary>综合预览；只返回接口提供的音乐分类，不接受分页参数。</summary>
    Task<IReadOnlyList<SearchResultSection>> SearchComprehensiveAsync(string keyword, CancellationToken cancellationToken = default);
    Task<PagedResult<Album>> SearchAlbumsAsync(string keyword, PagedCursor cursor, CancellationToken cancellationToken = default);
    Task<PagedResult<Playlist>> SearchPlaylistsAsync(string keyword, PagedCursor cursor, CancellationToken cancellationToken = default);
    Task<PagedResult<Artist>> SearchArtistsAsync(string keyword, PagedCursor cursor, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetSearchSuggestionsAsync(string keyword, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SearchHotWord>> GetSearchHotWordsAsync(CancellationToken cancellationToken = default);
    Task<PagedResult<Track>> GetArtistTracksAsync(long artistId, PagedCursor cursor, CancellationToken cancellationToken = default);
    Task<PagedResult<Album>> GetArtistAlbumsAsync(long artistId, PagedCursor cursor, CancellationToken cancellationToken = default);

    /// <summary>
    /// 歌手详情：别名、粉丝数、简介、单曲与专辑数。
    /// </summary>
    /// <remarks>
    /// 服务端没有这个歌手时返回 <c>null</c>。这几个字段<b>搜索结果里都没有</b>，
    /// 只有这条接口会给，所以歌手页要单独拉一次。
    /// </remarks>
    Task<Artist?> GetArtistInfoAsync(long artistId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 搜索曲目。
    /// </summary>
    /// <param name="keyword">关键词。空白串会抛 <see cref="ArgumentException"/>。</param>
    /// <param name="cursor">调用方持有的游标，由本方法推进。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <remarks>
    /// 返回值里的 <c>Total</c> <b>不可信</b>（服务端会漏算不可用条目），判断「还有没有下一页」
    /// 只能看 <see cref="PagedCursor.Exhausted"/>。
    /// </remarks>
    Task<PagedResult<Track>> SearchAsync(
        string keyword,
        PagedCursor cursor,
        CancellationToken cancellationToken = default);

    /// <summary>曲目详情。服务端没有这首歌时返回 <c>null</c>。</summary>
    Task<Track?> GetTrackAsync(long musicId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 解析一首歌能不能播、能播多少。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 一次调用完成「授权检查 → 选档 → 取地址 → 核对降级」，因为界面永远需要这三步连起来的结论，
    /// 没有只做前两步的场景。
    /// </para>
    /// <para>
    /// <b>每次点播现取地址，不要缓存结果</b>：CDN 地址带签名且有时效。
    /// </para>
    /// </remarks>
    Task<PlaybackResolution> ResolvePlaybackAsync(
        Track track,
        CancellationToken cancellationToken = default);

    Task<PlaybackResolution> ResolvePlaybackAsync(
        Track track, AudioQuality preferredQuality, CancellationToken cancellationToken = default)
        => ResolvePlaybackAsync(track, cancellationToken);

    /// <summary>
    /// 当前账号的自建歌单。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>一次返回全部，没有分页</b> —— 这个端点除了 <c>userId</c> 没有别的参数，
    /// 所以不要给它套 <c>PagedCursor</c>。
    /// </para>
    /// <para>
    /// <b>「我喜欢」不在这个列表里</b>，要从 <see cref="GetLikedPlaylistAsync"/> 单独取
    /// （文档 2.4 特地记了这一条，实测也确认了）。
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">未登录。</exception>
    Task<IReadOnlyList<Playlist>> GetCreatedPlaylistsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 「我喜欢」这个歌单<b>本身</b>，不是它的曲目。
    /// </summary>
    /// <remarks>
    /// 拿它的 <see cref="Playlist.Id"/> 再去 <see cref="GetPlaylistTracksAsync"/> 就是红心列表。
    /// 账号没有红心歌单、或响应里缺 <c>id</c> 时返回 <c>null</c> —— 那是正常结果，不是异常。
    /// </remarks>
    /// <exception cref="InvalidOperationException">未登录。</exception>
    Task<Playlist?> GetLikedPlaylistAsync(CancellationToken cancellationToken = default);

    /// <summary>歌单曲目。</summary>
    /// <param name="playlistId">歌单 id。</param>
    /// <param name="source">
    /// 歌单来源，**直接填进 query 的 <c>source</c>**。
    /// 账号歌单（自建与「我喜欢」）是 <c>5</c>；发现页里的公开歌单是它们的 <c>sourceType</c>（实测是 <c>4</c>）。
    /// </param>
    /// <param name="cursor">调用方持有的游标。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <remarks>
    /// <para>
    /// <b><paramref name="source"/> 必须由调用方给</b>：拿不到歌单对象时猜不出来，
    /// 而猜错的表现是「曲目列表是空的」——服务端对不上的 source 只会回空，不报错。
    /// 歌单对象自己的 <c>SourceType</c> 就是它，但那个字段可能缺失（文档 6.2），
    /// 所以调用方要能显式指定。
    /// </para>
    /// <para>
    /// 翻页与搜索同一条规矩：判断「还有没有下一页」只能看
    /// <see cref="PagedCursor.Exhausted"/>，**不信响应里的 <c>total</c>** ——
    /// 不可用曲目会被服务端省略，实测歌单标称 121 首时首页只回 99 首。
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">未登录。</exception>
    Task<PagedResult<Track>> GetPlaylistTracksAsync(
        long playlistId,
        int source,
        PagedCursor cursor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 往歌单加歌。红心（把曲目加入「我喜欢的」）走这条。
    /// </summary>
    /// <param name="playlistId">目标歌单 id。「我喜欢」的 id 从 <see cref="GetLikedPlaylistAsync"/> 取。</param>
    /// <param name="musicIds">曲目 id，单次 <b>1–100</b> 首。</param>
    /// <remarks>
    /// <b>调用方必须先确认歌单归属</b>：自建歌单走 <see cref="GetCreatedPlaylistsAsync"/>，
    /// 「我喜欢」走 <see cref="GetLikedPlaylistAsync"/> —— 两者是不同的列表，
    /// 「我喜欢」<b>不在</b> <c>userCreate</c> 的返回里（文档 2.4）。
    /// </remarks>
    /// <exception cref="InvalidOperationException">未登录。</exception>
    Task AddPlaylistMusicAsync(long playlistId, IReadOnlyList<long> musicIds,
        CancellationToken cancellationToken = default);

    /// <summary>把曲目移出歌单。参数与 <see cref="AddPlaylistMusicAsync"/> 相同。</summary>
    /// <exception cref="InvalidOperationException">未登录。</exception>
    Task RemovePlaylistMusicAsync(long playlistId, IReadOnlyList<long> musicIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 分享上报：取分享文案，并<b>把该曲目的分享数 +1</b>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>这是一个写操作</b>（文档 2.8 实测：<c>share</c> 从 2128 变 2129）。分享链接本身不走接口，
    /// 是本地拼的，但每次分享都要调这里一次 —— 包括「复制链接」，否则分享数与官方行为不一致。
    /// </para>
    /// <para>
    /// 响应里的 <c>data</c> 是分享文案（<c>{title, describe}</c>）。本项目不做系统分享面板，
    /// 用不到文案，所以只返回结果、不建模。
    /// </para>
    /// <para>
    /// <b>不要求登录</b>：链接的复制不该被登录态挡住；服务端拒绝时返回
    /// <see cref="ShareOutcome.Unsupported"/> 或 <see cref="ShareOutcome.Failed"/>，由调用方决定是否提示。
    /// </para>
    /// </remarks>
    Task<ShareOutcome> ReportTrackShareAsync(long musicId, CancellationToken cancellationToken = default);

    /// <summary>已购单曲。</summary>
    /// <remarks>
    /// 响应里**没有任何订单、购买时间、价格字段** —— 它就是一个普通的曲目列表
    /// （findings/06 §12.2）。总数键是 <c>size</c>，不是别处的 <c>total</c>。
    /// </remarks>
    /// <exception cref="InvalidOperationException">未登录。</exception>
    Task<PagedResult<Track>> GetPurchasedSinglesAsync(
        PagedCursor cursor,
        CancellationToken cancellationToken = default);

    /// <summary>已购专辑。</summary>
    /// <exception cref="InvalidOperationException">未登录。</exception>
    Task<PagedResult<Album>> GetPurchasedAlbumsAsync(
        PagedCursor cursor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 收藏的专辑。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 走的是移动端的<b>收藏歌单</b>端点（<c>service/collect/4/list</c>，数组键 <c>playLists</c>）——
    /// 按用户实测，它的内容与官方桌面端「收藏专辑」的效果一致。
    /// 官方桌面端自己那条路（<c>service/collect/6/list</c>）已弃用：实测返回 200 但 <c>data</c> 是空对象。
    /// </para>
    /// <para>
    /// <b>该端点是混合列表</b>（元素自带 <c>sourceType</c>），本方法**排除 <c>sourceType == 4</c> 的歌单**，
    /// 其余都当专辑；歌单由 <see cref="GetCollectedPlaylistsAsync"/> 取。见
    /// <c>reverse/findings/13-collect-playlist-follow-artist.md</c>。
    /// </para>
    /// <para>
    /// 判据写成「排除歌单」而不是「等于专辑（6）」：专辑条目**可能不带 <c>sourceType</c>**，
    /// 那种形状要被收下而不是漏掉。
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">未登录。</exception>
    Task<PagedResult<Album>> GetCollectedAlbumsAsync(
        PagedCursor cursor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 收藏的歌单。与 <see cref="GetCollectedAlbumsAsync"/> 是同一条端点，只保留 <c>sourceType == 4</c>。
    /// </summary>
    /// <remarks>
    /// <b>歌单 ≠ 专辑</b>：两者是不同概念，只是共用 <c>service/collect/4/list</c> 这条读端点。
    /// </remarks>
    /// <exception cref="InvalidOperationException">未登录。</exception>
    Task<PagedResult<Playlist>> GetCollectedPlaylistsAsync(
        PagedCursor cursor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 我关注的歌手（<c>service/collect/7/list</c>，数组键 <c>artistList</c>）。
    /// </summary>
    /// <remarks>
    /// <b>一次返回全部，没有分页</b>（官方客户端固定 <c>rn=400</c>）。
    /// 歌手详情 <c>service/artist/{id}</c> **没有任何 follow 字段**，所以「是否已关注」
    /// 只能靠这份列表的成员判定。
    /// </remarks>
    /// <exception cref="InvalidOperationException">未登录。</exception>
    Task<IReadOnlyList<Artist>> GetFollowedArtistsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 一个歌单**是否已被当前账号收藏**。
    /// </summary>
    /// <returns>
    /// <c>true</c> / <c>false</c> 为确定答案；<b><c>null</c> 表示无法判定</b>（未登录或读取失败）。
    /// </returns>
    /// <remarks>
    /// 判据是歌单详情里的 <c>collectTime</c> 是否存在，**不是 <c>isFond</c>**。
    /// 见 <c>reverse/findings/13-collect-playlist-follow-artist.md</c> §3.3。
    /// </remarks>
    Task<bool?> IsPlaylistCollectedAsync(long playlistId, int source, CancellationToken cancellationToken = default);

    /// <summary>
    /// 收藏 / 取消收藏一个歌单（或专辑）。<c>op</c>（<c>1</c> = 收藏、<c>2</c> = 取消）由这里算好。
    /// </summary>
    /// <param name="source">收藏类型，歌单/专辑是 <c>4</c>。</param>
    /// <exception cref="InvalidOperationException">未登录。</exception>
    Task SetPlaylistCollectedAsync(long playlistId, int source, bool collected, CancellationToken cancellationToken = default);

    /// <summary>
    /// 关注 / 取消关注一个歌手。与 <see cref="SetPlaylistCollectedAsync"/> 同一个端点，
    /// 但 <c>source=7</c> 且报文多一个 <c>token</c>。
    /// </summary>
    /// <exception cref="InvalidOperationException">未登录。</exception>
    Task SetArtistFollowedAsync(long artistId, bool followed, CancellationToken cancellationToken = default);

    /// <summary>
    /// 一张专辑**是否已被当前账号收藏**。
    /// </summary>
    /// <returns>
    /// <c>true</c> / <c>false</c> 为确定答案；<b><c>null</c> 表示无法判定</b>（未登录或读取失败）。
    /// </returns>
    /// <remarks>
    /// <b>专辑详情 <c>service/album/{id}</c> 里没有收藏标志</b>（已收藏与未收藏的响应逐字段同形），
    /// 判据只能走 <c>service/collect/multipleState?source=6&amp;sourceIds=</c> 的 <c>collect</c> 布尔。
    /// 见 <c>reverse/findings/13-collect-playlist-follow-artist.md</c> §3.3 的补记。
    /// </remarks>
    Task<bool?> IsAlbumCollectedAsync(long albumId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 收藏 / 取消收藏一张专辑。与歌单同一个端点，但 <c>source=6</c>（歌单是 <c>4</c>）、同样不带 <c>token</c>。
    /// </summary>
    /// <exception cref="InvalidOperationException">未登录。</exception>
    Task SetAlbumCollectedAsync(long albumId, bool collected, CancellationToken cancellationToken = default);

    /// <summary>
    /// 排行榜首页：分组（置顶位 / 热力榜 / 全球榜 / 特色榜 / H5榜单），每组若干榜。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 实测 5 组共 22 个榜。<b>每个榜带的 <c>PreviewTracks</c> 只是预览（实测 5 首）</b>，
    /// 完整榜单要按 <c>Id</c> 去 <see cref="GetBangTracksAsync"/> 取 —— 实测每个榜 100 首。
    /// </para>
    /// <para>这个端点无参数、不要求登录。</para>
    /// </remarks>
    Task<IReadOnlyList<BangSection>> GetBangSectionsAsync(CancellationToken cancellationToken = default);

    /// <summary>一个榜的曲目。<b>可分页</b>，实测一个榜 100 首。</summary>
    /// <remarks>
    /// 翻页与别处同一条规矩：判断「还有没有下一页」只能看
    /// <see cref="PagedCursor.Exhausted"/>，不信响应里的 <c>total</c>。
    /// </remarks>
    Task<PagedResult<Track>> GetBangTracksAsync(
        long bangId,
        PagedCursor cursor,
        CancellationToken cancellationToken = default);

    /// <summary>专辑详情。服务端没有这张专辑时返回 <c>null</c>。</summary>
    /// <remarks>
    /// <b>详情里不含曲目</b>，曲目走 <see cref="GetAlbumTracksAsync"/>。
    /// 简介（<c>Description</c>）实测很长，界面上要折叠。
    /// </remarks>
    Task<Album?> GetAlbumAsync(long albumId, CancellationToken cancellationToken = default);

    /// <summary>专辑曲目，可分页（<c>pn</c> 从 0）。</summary>
    /// <remarks>
    /// 信封的数组键是 <c>resultList</c> —— 与搜索列表同键、与歌单的 <c>list</c> 不同。
    /// </remarks>
    Task<PagedResult<Track>> GetAlbumTracksAsync(
        long albumId,
        PagedCursor cursor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 某个子类下的专辑列表，可分页（<c>pn</c> 从 1）。
    /// </summary>
    /// <param name="pTypeId">大类的 id（<see cref="MusicCategoryGroup.Id"/>，如 <c>"2005"</c>）。</param>
    /// <param name="cTypeId">子类的 id（<see cref="MusicCategoryChild.Id"/>，如 <c>"8101"</c>）。</param>
    /// <param name="sort">排序。<b>服务端要的是字符串</b>，见 <see cref="MusicLibSort"/>。</param>
    /// <param name="cursor">调用方持有的游标。页号从 1 起。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <remarks>
    /// <b>两个 id 都不是父项/子项的 <c>ptypeId</c> 字段</b> —— 就是它们各自的 <c>id</c>。
    /// 这一点由反编译里两处空值检查的报错路径确定（<c>MusicLibCategoryItem.id</c> 与
    /// <c>MusicLibChildItem.id</c>），不是推断。
    /// </remarks>
    Task<PagedResult<Album>> GetMusicLibraryAlbumsAsync(
        string pTypeId,
        string cTypeId,
        MusicLibSort sort,
        PagedCursor cursor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 乐库的分类树：15 个大类（流行 / 爵士 / 古典 / 民谣 / 摇滚 / 嘻哈 / 轻音乐 / ACG …）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 一次请求带回全部结构：每个大类有名称、简介、封面、专辑数，以及它的子类；
    /// 每个子类还带一张**代表专辑**。
    /// </para>
    /// <para>
    /// <b>这不是 <c>service/category/*</c>。</b> 那个是「歌单广场」（用户创建的歌单），
    /// 与乐库是两个功能，见 <c>reverse/findings/07-musiclib.md</c>。
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<MusicCategoryGroup>> GetMusicLibraryAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 乐库的分类树：6 组（主题 / 流派 / 语言 / 心情 / 场景 / 年代），每组若干子分类。
    /// </summary>
    /// <remarks>
    /// 响应里另有一份 <c>customCategory</c>（推荐 / 歌单），**本项目不用** ——
    /// 拿它的 id 去打 <see cref="GetCategoryPlaylistsAsync"/> 返回空，
    /// 说明它不是子分类 id（那两个标签在客户端里映射到别的端点）。
    /// </remarks>
    Task<IReadOnlyList<CategoryGroup>> GetCategoriesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 某个子分类下的歌单，可分页。
    /// </summary>
    /// <remarks>
    /// <b><paramref name="categoryId"/> 必须用子分类的 id</b>（形如 77「网红」），
    /// **不是**顶层组的 id（形如 67「主题」）—— 用后者能请求成功但语义不对。
    /// <para>
    /// 返回的 <see cref="Playlist.SourceType"/> 实测是 <c>4</c>（公开集合），
    /// 点进详情页时要把它当 <c>source</c> 传下去。
    /// </para>
    /// </remarks>
    Task<PagedResult<Playlist>> GetCategoryPlaylistsAsync(
        long categoryId,
        PagedCursor cursor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 一个 AI 歌单的完整内容。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 两个参数来自 <see cref="HomeSection.Ai"/>。**都不能猜**：
    /// 「个性化歌单」与「你的主题歌单」的 index 都是 0/1/2/3，
    /// 但只有配上各自的 <c>passRecName</c> 才指向正确的歌单 —— 只传 index 会拿到空数据。
    /// </para>
    /// <para>每组只给 3 首预览，这里能取到完整的（实测 30 首）。</para>
    /// </remarks>
    Task<AiPlaylist?> GetAiPlaylistAsync(
        int index,
        string passRecName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 发现页的布局：一串模块（只有 id / type / name）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>没有内容。</b> 每个模块的内容要再用 <see cref="GetHomeModuleAsync"/> 单独拉 ——
    /// 这是服务端的设计，不是本项目的选择。所以打开发现页的请求数取决于拉几个模块，
    /// 调用方应当懒加载而不是一次拉全。
    /// </para>
    /// <para>这个端点无参数、也不需要登录。</para>
    /// </remarks>
    Task<IReadOnlyList<HomeModule>> GetHomeModulesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 一个模块的内容，已归一成「标题 + 若干组卡片」。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>形状由模块的 <c>Type</c> 决定，不是固定的</b>：同一批模块里
    /// <c>songList</c> 这个键有时是曲目分组、有时是歌单卡片。按 type 分派是本方法的职责，
    /// 调用方拿到的永远是同一种模型。
    /// </para>
    /// <para>
    /// 返回 <c>null</c> 的两种情形：模块类型本项目不支持（轮播图、广告、实验室等），
    /// 或者这次响应里没有内容（实测模块可能返回空数组）。
    /// **两者都是正常结果**，不是异常。
    /// </para>
    /// </remarks>
    Task<HomeFeed?> GetHomeModuleAsync(HomeModule module, CancellationToken cancellationToken = default);

    /// <summary>
    /// 原始取词：直接要指定版式，返回 Base64 解码后的歌词文本。
    /// </summary>
    /// <param name="musicId">波点的 musicId，不是酷我 rid。</param>
    /// <param name="lrcx"><c>1</c> 逐字 / <c>0</c> 逐行（见 <c>BodianLyricPayload</c>）。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>
    /// 歌词文本。**空串是正常结果**：这首歌没有该版式的轨时服务端就返回空串，业务码仍是 200。
    /// </returns>
    /// <remarks>
    /// 解析交给 <c>BodianLyricParser</c>，本方法只负责取回文本。
    /// 多数调用方要的是 <see cref="GetLyricsAsync"/>。
    /// </remarks>
    Task<string> GetLyricAsync(long musicId, int lrcx, CancellationToken cancellationToken = default);

    /// <summary>
    /// 取这首歌的歌词并解析成统一模型。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 版式按 <see cref="Track.Lyrics"/> 决定：已知有逐字轨就只要逐字版，已知没有就直接要逐行版，
    /// **未知时（搜索结果没有歌词轨信息）先试逐字版，拿到空串再退逐行版**。最多两次请求。
    /// </para>
    /// <para>
    /// 这首歌没有歌词时返回 <see cref="LyricDocument.Empty"/> —— 那是正常结果，不是异常。
    /// 网络与服务端异常**照常抛出**，不要在这里吞掉。
    /// </para>
    /// </remarks>
    Task<LyricDocument> GetLyricsAsync(Track track, CancellationToken cancellationToken = default);

    /// <summary>读取歌曲评论。页码从 1 开始，固定每页 30 条以避开 hot 的 rn 缺陷。</summary>
    Task<SongCommentPage> GetSongCommentsAsync(long musicId, SongCommentSort sort, int page = 1,
        CancellationToken cancellationToken = default);

    /// <summary>读取一条主评论的回复；页码从 1 开始。</summary>
    Task<SongCommentPage> GetSongCommentRepliesAsync(long musicId, long parentId, int page = 1,
        CancellationToken cancellationToken = default);

    /// <summary>发送文字评论或回复。返回服务端提供的评论 id（可能未提供）；需读取列表确认。</summary>
    Task<long?> PublishSongCommentAsync(long musicId, string content, long parentId = 0, long replyId = 0,
        bool anonymous = false, CancellationToken cancellationToken = default);

    /// <summary>v3 点赞或取消点赞（op=1/2）；回复的 parentId 为所属主评论 id。</summary>
    Task SetSongCommentLikeAsync(long musicId, long commentId, bool liked, long parentId = 0,
        CancellationToken cancellationToken = default);
}
