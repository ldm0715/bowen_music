namespace Bodian.Core.Models.Home;

/// <summary>
/// 发现页里一个模块渲染出来的内容：一个标题 + 一到多组。
/// </summary>
/// <remarks>
/// 把各家不同的响应形状<b>归一成同一种</b>，界面那一层就不必知道「音乐日历长什么样」。
/// </remarks>
/// <param name="Title">模块名，来自响应的 <c>moduleName</c>。</param>
/// <param name="Sections">分组。**不会为空** —— 解析不出内容时调用方拿到的就是 <c>null</c>。</param>
public sealed record HomeFeed(string Title, IReadOnlyList<HomeSection> Sections);

/// <summary>
/// 一组内容怎么排。
/// </summary>
/// <remarks>
/// <b>整个模块用同一种排法</b>，所以卡片本身不需要再带类型判别 ——
/// 界面按这一个值就能选出该用哪个模板、一屏放几张。
/// </remarks>
public enum HomeSectionLayout
{
    /// <summary>
    /// 封面拼图 + 名称（type 4「个性化歌单」）。
    /// </summary>
    /// <remarks>
    /// 分组自己没有封面图，只有名字和 3 首预览，所以卡片顶部的图由那几首先拼出来。
    /// 点整张卡进那个歌单。
    /// </remarks>
    PlaylistMosaic,

    /// <summary>
    /// 封面 + 名称 + 几行曲目预览（type 11「你的主题歌单」）。
    /// </summary>
    /// <remarks>与 <see cref="PlaylistMosaic"/> 同源，但卡片更宽：右边直接列出那几首都是什么。</remarks>
    PlaylistPreview,

    /// <summary>
    /// 封面 + 播放数 + 描述（type 5「宝藏歌单库」）。
    /// </summary>
    /// <remarks>这些歌单有真实封面、描述与播放数，所以不用拼图。</remarks>
    PlaylistCover,

    /// <summary>
    /// 曲目列：若干首纵向排成一列，列与列横向排开（type 3/10 的一整片单曲）。
    /// </summary>
    TrackColumns,
}

/// <summary>
/// 发现页里的一组内容。
/// </summary>
/// <param name="Title">组内小标题。没有时为空串。</param>
/// <param name="CoverImage">组的封面（排行榜有自己的图）。没有时为 <c>null</c>。</param>
/// <param name="Cards">卡片。**不会为空**。</param>
/// <param name="Layout">这一组怎么排。</param>
public sealed record HomeSection(
    string Title,
    Uri? CoverImage,
    IReadOnlyList<HomeCard> Cards,
    HomeSectionLayout Layout = HomeSectionLayout.PlaylistCover);

/// <summary>
/// 打开一个完整歌单所需的两个参数（<c>service/home/aiPlaylistDetail</c>）。
/// </summary>
/// <remarks>
/// <b>两个参数缺一不可，而且不能靠猜。</b> 格式化串是从安卓 5.9.8 的二进制里读出来的：
/// <c>service/home/aiPlaylistDetail?index=%d&amp;passRecName=%s</c>。
/// 实测：
/// <list type="bullet">
/// <item>只传 <c>index</c>（<c>passRecName</c> 为空）能拿到「个性化歌单」的歌单；</item>
/// <item>「你的主题歌单」必须两个都传 —— 只传 index 会拿到空数据，
/// 因为它的 0/1/2/3 与个性化歌单的 0/1/2/3 是**两套不同的歌单**。</item>
/// </list>
/// </remarks>
/// <param name="Index">这一组在该模块里的位置（0 起）。</param>
/// <param name="PassRecName">分组自带的 <c>passRecName</c>；没有这个字段时为空串。</param>
public sealed record AiPlaylistRef(int Index, string PassRecName);

/// <summary>
/// 歌单卡片里列出来的一行曲目预览（只有歌名与歌手，不是可播的行）。
/// </summary>
/// <param name="Title">歌名。</param>
/// <param name="Artist">艺人。</param>
public sealed record HomeTrackPreview(string Title, string Artist);

/// <summary>
/// 发现页里的一卡片。
/// </summary>
/// <remarks>
/// <b>一张卡片要么是可播的曲目，要么是歌单</b>：<see cref="Track"/> 有值时点它播放，
/// 否则点它进歌单（<see cref="Playlist"/> 或 <see cref="Ai"/>）。
/// 用「可空字段 + 判别属性」而不是继承层次 —— 同一模块里的卡片形态是一致的
/// （排法由 <see cref="HomeSection.Layout"/> 决定），所以多一层类型只会让 XAML 侧多一层模板选择。
/// </remarks>
public sealed record HomeCard
{
    public required string Title { get; init; }

    /// <summary>副标题：曲目是艺人，歌单是曲目数或描述。</summary>
    public string Subtitle { get; init; } = "";

    /// <summary>
    /// 封面。
    /// </summary>
    /// <remarks>
    /// 普通卡片一张就够；<b>「个性化歌单」「你的主题歌单」的歌单卡片是几张</b> ——
    /// 那两个模块的分组**自己不带封面图**，只有名字和几首预览，
    /// 所以卡片的图只能靠它们拼出来。界面按张数决定怎么拼。
    /// </remarks>
    public IReadOnlyList<Uri> Covers { get; init; } = [];

    /// <summary>可播的曲目。与 <see cref="Playlist"/>、<see cref="Ai"/> 互斥。</summary>
    public Track? Track { get; init; }

    /// <summary>歌单。与 <see cref="Track"/>、<see cref="Ai"/> 互斥。</summary>
    public Playlist? Playlist { get; init; }

    /// <summary>
    /// 点它打开哪个 AI 歌单。
    /// </summary>
    /// <remarks>
    /// <b>只有「个性化歌单」（type 4）与「你的主题歌单」（type 11）的卡片会填它</b> ——
    /// 那两个模块每组只给 3 首预览，点进去能拿到完整的（实测都是 30 首）。
    /// 按**位置**而不是按 <c>id</c> 取 index：type 11 的分组压根没有 id 字段，
    /// 而 type 4 的 id 在实测样本里就是位置。
    /// </remarks>
    public AiPlaylistRef? Ai { get; init; }

    /// <summary>卡片上直接列出来的几行曲目（<see cref="HomeSectionLayout.PlaylistPreview"/> 用）。</summary>
    public IReadOnlyList<HomeTrackPreview> Previews { get; init; } = [];

    /// <summary>播放数，画在封面左下角的角标里（<see cref="HomeSectionLayout.PlaylistCover"/> 用）。</summary>
    public long PlayCount { get; init; }

    public bool IsPlayable => Track is not null;

    public bool IsPlaylist => Playlist is not null;

    /// <summary>这首曲目要不要会员。单曲行上要画一个付费标识。</summary>
    /// <remarks>
    /// 从 <see cref="Track"/> 上摊平出来，是为了让 XAML 能直接绑：
    /// 绑 <c>Track.RequiresVip</c> 的话，曲目为 <c>null</c>（歌单卡片）时拿不到 bool。
    /// </remarks>
    public bool RequiresVip => Track?.RequiresVip ?? false;

    public bool RequiresPurchase => Track?.RequiresPurchase ?? false;

    /// <summary>点它要打开一个 AI 歌单（卡片上那几首只是预览，不是全部内容）。</summary>
    public bool OpensAiPlaylist => Ai is not null;

    /// <summary>
    /// 第 1/2/3 张封面。
    /// </summary>
    /// <remarks>
    /// 给 XAML 用：函数绑定取不了列表下标（越界会抛），而拼图最多就三张，摊成属性最省事。
    /// </remarks>
    public Uri? Cover1 => Covers.Count > 0 ? Covers[0] : null;

    public Uri? Cover2 => Covers.Count > 1 ? Covers[1] : null;

    public Uri? Cover3 => Covers.Count > 2 ? Covers[2] : null;
}
