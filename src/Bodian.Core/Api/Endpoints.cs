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

    /// <summary>
    /// MV 详情。GET + query <c>musicId</c>，无 MV 时业务码 <c>20048</c>。
    /// 证据：<c>reverse/findings/15-mv.md</c>（实网验证过）。
    /// </summary>
    public const string MvInfo = "service/mv/info";

    public const string SearchMusicList = "search/music/list";
    public const string SearchComprehensive = "search/comprehensive/v2/list";
    public const string SearchAlbumList = "search/album/list";
    public const string SearchPlaylistList = "search/playlist/list";
    public const string SearchArtistList = "search/artist/list";
    public const string SearchTips = "search/tip/v2/list";
    public const string SearchTopics = "search/topic/word/list";
    public static string ArtistTracks(long id) => $"service/artist/music/{id}";
    public static string ArtistAlbums(long id) => $"service/artist/album/{id}";

    /// <summary>
    /// 歌手详情。<b>无 query</b>，响应 <c>{ artistInfo: {...} }</c>，见 <c>Dto/ArtistInfoPayloads.cs</c>。
    /// </summary>
    /// <remarks>
    /// 别名、粉丝数与简介只有这条接口会给；搜索结果的歌手条目里没有这几个字段。
    /// </remarks>
    public static string ArtistInfo(long id) => $"service/artist/{id}";

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

    /// <summary>
    /// 歌单加歌 / 删歌。「我喜欢」的红心就是往账号的红心歌单增删曲目。
    /// </summary>
    /// <remarks>
    /// 两条路径都在官方桌面端二进制里，<b>用默认 PC 请求头</b>（findings/06 §8）。
    /// body 是 <c>{"playListId": &lt;number&gt;, "musicIdList": [&lt;number&gt;, ...]}</c>，
    /// 单次 1–100 首。完整往返实测见文档 2.4。
    /// </remarks>
    public const string PlaylistMusic = "service/playlist/music";

    /// <summary>
    /// 歌单<b>本身</b>的新建 / 删除 / 编辑。**三条共用这一条裸路径，靠 HTTP method 区分**：
    /// <c>POST</c> 新建（body <c>{name, private}</c>）、<c>DELETE</c> 删除（body <c>{playlistIds: […]}</c>）、
    /// <c>PUT</c> 编辑（body <c>{id, name, description, pic, categoryList}</c>）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>与 <see cref="PlaylistMusic"/> 不是一回事</b>：那条动的是「歌单里的歌」，这条动的是「歌单本身」。
    /// 两条路径不同、语义不同，不要互相套用。
    /// </para>
    /// <para>
    /// 新建与删除已实测（文档 2.4、<c>reverse/findings/11-share-playlist-crud.md</c> §2）：
    /// 桌面头 + 桌面签名即可，<b>新建的回执只有 <c>{id}</c></b>，删除支持一次传多个 id。
    /// </para>
    /// <para>
    /// <c>PUT</c> 的五个键都有反汇编字面量证据（<c>edit_user_playlist.dart:3796-3914</c>），
    /// 但<b>尚未实测</b>。编辑页没有隐私开关，body 里也没有 <c>private</c> 键 ——
    /// <b>官方客户端不支持改已有歌单的隐私</b>。
    /// </para>
    /// <para>
    /// <c>categoryList</c> 是分类 <b>id 数组</b>（id 来自 <c>service/category/list</c>）；
    /// 歌单已有的标签从 <c>service/playlist/info</c> 的 <c>categories</c> 读回
    /// （<c>[{id, name}]</c>，见 <c>PlaylistDto.Categories</c>）。
    /// </para>
    /// </remarks>
    /// </remarks>
    public const string PlaylistCrud = "service/playlist";

    /// <summary>
    /// 歌单详情。query 带 <c>source</c>（就是歌单自身的 <c>sourceType</c>）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 响应 <c>data</c> 就是歌单对象本身（不套壳）。<b>「是否已收藏」看响应里有没有
    /// <c>collectTime</c></b> —— 不是 <c>isFond</c>。见
    /// <c>reverse/findings/13-collect-playlist-follow-artist.md</c> §3.3。
    /// </para>
    /// <para>
    /// <b>id 在路径上</b>，与 <see cref="PlaylistTracks"/> 同形；<c>source</c> 走 query。
    /// </para>
    /// </remarks>
    public static string PlaylistInfo(long playlistId) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"service/playlist/info/{playlistId}");

    /// <summary>
    /// 歌单封面上传。歌单 id 在路径上，body 是 <c>multipart/form-data</c>，字段名 <c>file</c>。
    /// </summary>
    /// <remarks>
    /// <b>签名只覆盖 path 与 query，不含二进制 body</b> —— 桌面签名对 body 算的是
    /// <c>md5(body + "kuwotest")</c>，那是针对 JSON 字符串的，二进制没有良定义的字符串形态。
    /// 上传成功后返回的封面 URL，回填到 <see cref="PlaylistCrud"/> 的 <c>pic</c> 才会真正生效。
    /// </remarks>
    public static string PlaylistUploadPic(long playlistId) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"service/playlist/uploadPic/{playlistId}");

    /// <summary>收藏写入的 <c>source</c>：歌单与专辑都走 <c>4</c>，按元素的 <c>sourceType</c> 分型。</summary>
    /// <remarks>见 <see cref="Collect"/> 与 <c>reverse/findings/13-collect-playlist-follow-artist.md</c>。</remarks>
    public const int CollectSourcePlaylistAlbum = 4;

    /// <summary>收藏**专辑**的 <c>source</c>。</summary>
    /// <remarks>
    /// 与歌单（<c>4</c>）不同。写端点与
    /// <see cref="CollectMultipleState"/> 都用它 —— 2026-10-03 真机往返实测。
    /// </remarks>
    public const int CollectSourceAlbum = 6;

    /// <summary>
    /// 批量查收藏状态。query <c>source</c> + <c>sourceIds</c>（逗号分隔）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 返回 <c>{result:[{id, collect}]}</c>。<b>专辑「是否已收藏」只有这条路</b> ——
    /// 专辑详情 <c>service/album/{id}</c> 里**没有任何收藏标志**（已收藏与未收藏的响应逐字段同形）。
    /// </para>
    /// <para>
    /// <b><c>source</c> 必须与对象类型匹配</b>：对专辑要传 <c>6</c>，传 <c>4</c> 会一律回 <c>false</c>（实测）。
    /// </para>
    /// </remarks>
    public const string CollectMultipleState = "service/collect/multipleState";

    /// <summary>关注歌手的 <c>source</c>。与歌单同为 <see cref="Collect"/> 端点，但 body 多一个 <c>token</c>。</summary>
    public const int CollectSourceArtist = 7;

    /// <summary>收藏族混合列表里代表「歌单」的 <c>sourceType</c>。</summary>
    public const int CollectedPlaylistType = 4;

    /// <summary>收藏族混合列表里代表「专辑」的 <c>sourceType</c>。</summary>
    public const int CollectedAlbumType = 6;

    /// <inheritdoc cref="PlaylistMusic"/>
    public const string PlaylistMusicDelete = "service/playlist/music/delete";

    /// <summary>
    /// 分享上报：取分享文案，并<b>把该内容的分享数 +1</b>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>链接本身不经过服务端</b>，是客户端本地拼的（<see cref="ShareHost"/>），
    /// 但分享动作必须调这里上报一次，否则分享数不涨。见文档 2.8（2026-10-03 实测）。
    /// </para>
    /// <para>query：<c>shareTo</c> / <c>shareSource</c> / <c>sourceId</c> / <c>playlistType</c>，四个都是 int。</para>
    /// </remarks>
    public const string ShareText = "service/share/text";

    /// <summary>微信会话。</summary>
    public const int ShareToWeChatSession = 0;

    /// <summary>微信朋友圈。</summary>
    public const int ShareToWeChatMoments = 1;

    /// <summary>复制链接。<b>同样计入分享数</b>，本项目只用这一个。</summary>
    public const int ShareToCopyLink = 5;

    /// <summary>分享内容类型：歌曲。</summary>
    public const int ShareSourceSong = 0;

    /// <summary>分享上报的歌单类型参数，客户端默认值。</summary>
    public const int SharePlaylistType = 4;

    /// <summary>
    /// 分享链接的 host。可被服务端远程配置覆盖，本项目不实现远程覆盖。
    /// </summary>
    /// <remarks>见文档 2.8：链接是客户端本地拼的，不走接口。</remarks>
    public const string ShareHost = "https://h5app.kuwo.cn/m/bodian/";

    // ── 账号统计：已实测 ✅（2026-10-04，桌面头 + 桌面签名直接通）──────────────

    /// <summary>
    /// 账号的社交计数：关注数 / 粉丝数 / 关注歌手数 / 获赞数。
    /// </summary>
    /// <remarks>
    /// <para>
    /// uid <b>在路径上</b>（官方客户端拼 <c>"service/users/" + uid + "/metadata"</c>）。
    /// 安卓那边另带一个 query <c>uid</c>，<b>本项目不用自己拼</b> —— 传输层对每个请求
    /// 都补 <c>uid</c>，自己再加会得到重复键。
    /// </para>
    /// <para>响应字段见 <c>Dto/UserStatsPayloads.cs</c>；样本见 <c>fixtures/users-metadata.json</c>。</para>
    /// </remarks>
    public static string UserMetadata(long uid) =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"service/users/{uid}/metadata");

    /// <summary>
    /// 用户公开资料。uid <b>在路径上</b>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 响应的 <c>data</c> 与登录响应<b>同构</b>（<c>id</c> / <c>userInfo</c> / <c>payInfo</c> /
    /// <c>bid</c> / …），所以 <c>payInfo</c> 可以直接复用 <see cref="Dto.AccountPayInfoDto"/>。
    /// </para>
    /// <para>
    /// 本项目只用它取<b>当前账号的会员档位与到期时间</b>——登录响应里的那两个值是一份快照，
    /// 登录期间不会变；要「点开下拉框即最新」就得靠这条重取。
    /// </para>
    /// </remarks>
    public static string UserPub(long uid) =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"ucenter/users/pub/{uid}");

    /// <summary>
    /// 听歌统计：播放次数 + 听歌时长。参数只有 <c>userId</c>。
    /// </summary>
    /// <remarks>
    /// 响应 <c>data</c> 直接是 <c>{playcnt, playTime}</c>，不套壳。
    /// <c>playTime</c> 的<b>单位是秒</b>（实测 <c>186344 / 1110 ≈ 168</c> 秒/首），
    /// 见 <c>Models/Account/ListenTimeLabel</c>；样本见 <c>fixtures/playdata-user-data.json</c>。
    /// </remarks>
    public const string UserPlayData = "ucenter/playdata/user_data";

    // ── 歌曲评论（android 请求头，读取、发布与点赞使用 v3）────────────────────────

    public const string SongCommentsRecommended = "comments/v3/hot";
    public const string SongCommentsLatest = "comments/v3/new";
    public const string SongCommentReplies = "comments/v3/replies";
    public const string SongCommentPublish = "comments/v3/publish";
    public const string SongCommentLike = "comments/v3/like";

    // ── 歌词站（另一个域，不走 /api 前缀、不签名）────────────────────────────

    public const string LyricHost = "https://mlyric.kuwo.cn";

    public const string LyricPath = "mobi.s";
}
