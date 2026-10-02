namespace Bodian.Core.Api;

/// <summary>
/// 已核实的业务路径常量。
/// </summary>
/// <remarks>
/// <para>
/// <b>路径不含 <c>/api</c> 前缀</b>——签名覆盖的就是这个形态。
/// </para>
/// <para>
/// <b>这里只列 P0 已实测或静态确认过的路径</b>，不留猜测项。加新路径前先确认它来自
/// <c>bodian-api-reference.md</c> 的已验证小节或 <c>reverse/findings/</c> 的结论。
/// </para>
/// </remarks>
internal static class Endpoints
{
    // ── 已实测 ✅ ───────────────────────────────────────────────────────────

    public const string MusicInfo = "service/music/info";

    public const string SearchMusicList = "search/music/list";
    public const string SearchComprehensive = "search/comprehensive/v2/list";
    public const string SearchAlbumList = "search/album/list";
    public const string SearchPlaylistList = "search/playlist/list";
    public const string SearchArtistList = "search/artist/list";
    public const string SearchTips = "search/tip/v2/list";
    public const string SearchTopics = "search/topic/word/list";
    public static string ArtistTracks(long id) => $"service/artist/music/{id}";
    public static string ArtistAlbums(long id) => $"service/artist/album/{id}";

    /// <summary><b>GET 且必须带 JSON body，签名覆盖该 body。</b></summary>
    public const string CheckRight = "play/music/v2/checkRight";

    /// <summary>与 <see cref="CheckRight"/> 请求形态相同。</summary>
    public const string AudioUrl = "play/music/v2/audioUrl";

    public const string LoginQrCode = "ucenter/login/qrCode";

    public const string LoginQrCodeStatus = "ucenter/login/qrCodeStatus";

    /// <summary>扫码登录的最后一步，响应见 <c>Dto/LoginDto.cs</c>。</summary>
    public const string UsersLogin = "ucenter/users/login";

    // ── 曲库：静态确认，参数与信封都已解 🟡 ─────────────────────────────────

    /// <summary>
    /// 自建歌单列表。参数只有 <c>userId</c>，**没有分页参数**（一次全给）。
    /// </summary>
    /// <remarks>响应信封 <c>{ playLists: [...], total: N }</c>。</remarks>
    public const string PlaylistUserCreate = "service/playlist/userCreate";

    /// <summary>
    /// 「我喜欢」这个歌单本身。参数只有 <c>userId</c>。
    /// </summary>
    /// <remarks>
    /// <b>响应是单个对象，不是数组</b>（文档 2.3：拿它的 <c>id</c> 再去取曲目）。
    /// 账号没有红心歌单时返回的对象缺 <c>id</c>，按「没有这个歌单」处理。
    /// </remarks>
    public const string PlaylistFond = "service/playlist/fond";

    /// <summary>
    /// 歌单曲目。除 <c>source</c> / <c>pn</c>（从 1）/ <c>rn</c> 外无他参。
    /// </summary>
    /// <remarks>响应信封 <c>{ list: [...], total: N }</c>。</remarks>
    public static string PlaylistTracks(long playlistId) =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"service/playlist/{playlistId}/musicList");

    // ── 专辑：实测已解 🟡 ──────────────────────────────────────────────────

    /// <summary>
    /// 专辑详情。<b>无 query 参数</b>，id 在路径上。
    /// </summary>
    /// <remarks>
    /// 实测 <c>service/album/1293</c> 返回 <c>{ albumInfo: {...} }</c>，
    /// 里面 <c>id</c> 与 <c>albumId</c> 同值，另有 <c>showtime</c>（发行日）、
    /// <c>info</c>（长简介）、<c>musicCount</c>。
    /// </remarks>
    public static string AlbumDetail(long albumId) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"service/album/{albumId}");

    /// <summary>
    /// 专辑曲目，**可分页**（<c>pn</c> 从 0）。
    /// </summary>
    /// <remarks>
    /// 实测 <c>service/album/music/1293</c> 返回 <c>{ total, rn, resultList, pn }</c>，
    /// 元素是曲目对象。<b>注意数组键是 <c>resultList</c></b>，与歌单的 <c>list</c> 不同。
    /// </remarks>
    public static string AlbumTracks(long albumId) =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"service/album/music/{albumId}");

    // ── 乐库（MusicLib）：实测已解 🟡 ──────────────────────────────────────

    /// <summary>
    /// 乐库的分类树。<b>无参数</b>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 实测 15 个大类（流行 / 爵士 / 古典 / 民谣 / 摇滚 / 嘻哈 / 轻音乐 / ACG / …），
    /// 每条带名称、简介、封面、专辑数与 <c>childList</c>（子类，各带一个代表专辑 <c>albumVo</c>）。
    /// </para>
    /// <para>
    /// <b>这是乐库，不是 <c>service/category/*</c>。</b> 后者是「歌单广场」（用户创建的 UGC 歌单），
    /// 两者是不同的功能，见 <c>reverse/findings/07-musiclib.md</c>。
    /// </para>
    /// <para>这个端点用桌面请求头就能取（实测）。</para>
    /// </remarks>
    public const string MusicLibraryNavigation = "play/music/library/navigation";

    /// <summary>
    /// 某个子类下的**专辑列表**，可分页（<c>pn</c> 从 1）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 参数四个，缺一不可（<b>都是实测确定的</b>）：
    /// <c>pTypeId</c> = 大类的 id、<c>cTypeId</c> = 子类的 id、
    /// <c>pn</c>/<c>rn</c> 分页、<c>sort</c>。
    /// </para>
    /// <para>
    /// <b><c>sort</c> 是字符串枚举而不是数字</b>：<c>"1"</c> = 精品、<c>"2"</c> = 最新。
    /// 这是整条链路卡最久的一处 —— 传数字（哪怕 <c>"0"</c>）服务端会回
    /// <c>200</c> 但 <c>data</c> 是**空对象</c>，不报错，看起来像"参数不全"。
    /// </para>
    /// <para>
    /// <b>方法必须是 GET（参数走 query）。</b> 用 POST + JSON body 打过去服务端回 <b>500</b>。
    /// </para>
    /// <para>
    /// <b>请求头无关</b>：<c>plat=win/ver=1.1.7</c> 与 <c>plat=android/ver=5.9.8</c>
    /// 实测返回完全一致。本项目沿用桌面头即可。
    /// （这条一开始被误判成"需要移动端头"，实际是 <c>sort</c> 传错导致的假象。）
    /// </para>
    /// <para>响应 <c>{ total, list }</c>，元素是专辑对象。</para>
    /// </remarks>
    public const string MusicLibraryAlbums = "play/music/library/albums";

    // ── 乐库（分类歌单）：实测已解 🟡 ──────────────────────────────────────

    /// <summary>
    /// 分类树。<b>无参数</b>。
    /// </summary>
    /// <remarks>
    /// 实测：<c>{ total, customCategory, categories }</c>，<c>categories</c> 是 6 组
    /// （主题 / 流派 / 语言 / 心情 / 场景 / 年代），每组带若干子分类。
    /// <para>
    /// <c>customCategory</c>（推荐 / 歌单）**本项目不用**：拿它的 id 去打
    /// <see cref="CategoryPlaylists"/> 返回空，它不是子分类 id。
    /// </para>
    /// </remarks>
    public const string CategoryList = "service/category/list";

    /// <summary>
    /// 某个子分类下的歌单，**可分页**（<c>pn</c> 从 1）。
    /// </summary>
    /// <remarks>
    /// 实测 <c>service/category/77/playlist</c> 返回 <c>{ playLists, total }</c> ——
    /// <b>与收藏歌单同一个信封</b>，元素也是歌单形状（<c>sourceType</c> 4），
    /// 所以复用 <c>PlaylistListPayload</c> 与歌单详情页。
    /// </remarks>
    public static string CategoryPlaylists(long categoryId) =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"service/category/{categoryId}/playlist");

    // ── 排行榜：实测已解 🟡 ────────────────────────────────────────────────

    /// <summary>
    /// 排行榜首页：**分组数组**，每组带若干榜（含前几首预览）。
    /// </summary>
    /// <remarks>
    /// <b>无参数</b>。实测返回 5 组、共 22 个榜，每组形如
    /// <c>{ moduleName, moduleId, bangList: [{ id, name, pic, bangType, pub, pubStr, musics }] }</c>。
    /// 「H5榜单」那组里的条目**没有 id**（是外部 H5 链接），映射时要跳过。
    /// </remarks>
    public const string HomeBangNew = "service/home/bangNew";

    /// <summary>
    /// 一个榜的曲目，**可分页**。
    /// </summary>
    /// <remarks>
    /// 实测 <c>service/bang/16/musics</c> 默认回 20 首、<c>total</c> 是 100；
    /// 带 <c>pn=2&amp;rn=10</c> 回第 11–20 名。页号从 1 起。
    /// 响应 <c>{ id, name, pic, pub, musics, total, sort }</c>。
    /// </remarks>
    public static string BangMusics(long bangId) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"service/bang/{bangId}/musics");

    /// <summary>
    /// 个性化歌单（AI 歌单）的详情。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><c>index</c> 就是 <c>service/home/module?moduleId=1</c> 里那几个分组的 <c>id</c>。</b>
    /// 实测 <c>index=0</c> 返回的 <c>title</c> 与那一组的标题逐字相同（都是「潮趣日推」），
    /// 内容是该歌单的 <b>30 首</b>曲目。
    /// </para>
    /// <para>
    /// 所以「个性化歌单」里的每一组其实是一个 AI 歌单，点标题就能进去看完整列表 ——
    /// 而 <c>home/module</c> 给的那 3 首只是预览。
    /// </para>
    /// <para>响应：<c>{ title, subTitle, bigTitle, musicList }</c>。</para>
    /// </remarks>
    /// <remarks>
    /// <b><c>index</c> 不是路径的一部分</b>，它和别的接口一样走 query ——
    /// 传输层拼 URL 的形态是 <c>BaseAddress + Path + "?" + query</c>，
    /// 把 <c>?index=N</c> 写进路径会拼出 <c>...?index=N?</c>，服务端回 <b>400</b>。
    /// </remarks>
    public const string AiPlaylistDetail = "service/home/aiPlaylistDetail";

    // ── 发现页：实测已解 🟡 ────────────────────────────────────────────────

    /// <summary>
    /// 发现页布局。<b>无参数</b>，只返回模块清单（id / type / name）。
    /// </summary>
    /// <remarks>响应信封 <c>{ moduleList: [...] }</c>，实测 12 个模块。</remarks>
    public const string HomeIndex = "service/home/index";

    /// <summary>
    /// 一个模块的内容。参数 <c>moduleId</c>；<b>形状由模块的 type 决定</b>，不是固定的。
    /// </summary>
    /// <remarks>实测样本见 <c>fixtures/home-module-*.json</c>。</remarks>
    public const string HomeModule = "service/home/module";

    // ── 曲库：已购与收藏 ────────────────────────────────────────────────────

    /// <summary>已购单曲。参数 <c>pn</c>（从 1）/ <c>rn</c>；响应信封 <c>{musicList, size}</c>。</summary>
    public const string PurchasedSingles = "ucenter/pay/album/music/purchasedList";

    /// <summary>
    /// 已购专辑。参数 <c>pn</c> / <c>rn</c>；响应信封 <c>{albumList, size}</c>。
    /// </summary>
    /// <remarks>
    /// 桌面端二进制里还有一个 <c>purchasedList2</c>，**移动端不存在、形状无从得知**，
    /// 所以本项目用移动端那条端到端追通的路径（findings/06 §12）。
    /// </remarks>
    public const string PurchasedAlbums = "ucenter/pay/album/purchasedList";

    /// <summary>
    /// 收藏族列表。<c>source</c> 是**运行时拼进路径**的，不是 query 参数。
    /// </summary>
    /// <remarks>
    /// 已确认的取值：<c>4</c> 收藏歌单、<c>7</c> 关注歌手、<c>8</c> 粉丝、<c>12</c> AI 学习。
    /// <b>收藏专辑用的 <c>6</c> 没有静态证据</b>（全树只有那 4 个调用点）——
    /// 见 <see cref="Dto.CollectedAlbumsPayload"/> 的警告。
    /// </remarks>
    public static string CollectList(int source) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"service/collect/{source}/list");

    // ── 静态确认，参数已解 🟡 ───────────────────────────────────────────────

    /// <summary>
    /// 收藏写入（移动端报文，PC 端实现不同且无法分析）。
    /// body 形如 <c>{"source":6,"sourceId":[228908],"op":1,"uid":...}</c>；
    /// <c>op</c> 的 1/2 方向仍未被证实，**写入前必须先读回确认**。
    /// </summary>
    public const string Collect = "service/collect";

    // ── 歌词站（另一个域，不走 /api 前缀、不签名）────────────────────────────

    public const string LyricHost = "https://mlyric.kuwo.cn";

    public const string LyricPath = "mobi.s";
}
