namespace Bodian.Core.Models.Home;

/// <summary>
/// 发现页里一个模块渲染出来的内容：一个标题 + 一到多组卡片。
/// </summary>
/// <remarks>
/// 把各家不同的响应形状<b>归一成同一种</b>，界面那一层就不必知道「音乐日历长什么样」。
/// 分组这一层是为实测形状服务的：曲目分组与排行榜都是「一组一个小标题」，
/// 而单曲推荐只有一整片、没有小标题（那种情况下 <see cref="HomeSection.Title"/> 为空）。
/// </remarks>
/// <param name="Title">模块名，来自响应的 <c>moduleName</c>。</param>
/// <param name="Sections">分组。**不会为空** —— 解析不出内容时调用方拿到的就是 <c>null</c>。</param>
public sealed record HomeFeed(string Title, IReadOnlyList<HomeSection> Sections);

/// <summary>
/// 发现页里的一组卡片。
/// </summary>
/// <param name="Title">组内小标题。没有时为空串（单曲推荐那种一整片的）。</param>
/// <param name="CoverImage">组的封面（排行榜有自己的图）。没有时为 <c>null</c>。</param>
/// <param name="Cards">卡片。**不会为空**。</param>
/// <param name="Ai">
/// 这一组能打开成一个完整歌单时，它对应的 <c>aiPlaylistDetail</c> 参数；不能打开时为 <c>null</c>。
/// </param>
/// <remarks>
/// <b>只有「个性化歌单」（type 4）与「你的主题歌单」（type 11）会填它</b> ——
/// 那两个模块每组只给 3 首预览，点进去能拿到完整的（实测都是 30 首）。
/// 按**位置**而不是按 <c>id</c> 取 index：type 11 的分组压根没有 id 字段，
/// 而 type 4 的 id 在实测样本里就是位置。
/// </remarks>
public sealed record HomeSection(
    string Title,
    Uri? CoverImage,
    IReadOnlyList<HomeCard> Cards,
    AiPlaylistRef? Ai = null);

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
/// 发现页里的一张卡片。
/// </summary>
/// <remarks>
/// <b>一张卡片要么是可播的曲目，要么是歌单</b>，两者互斥 ——
/// <see cref="Track"/> 有值时点它播放，<see cref="Playlist"/> 有值时点它进歌单详情。
/// 用「两个可空字段 + 一个判别属性」而不是继承层次：卡片是纯展示数据，
/// 多一层类型层次只会让 XAML 侧多一层模板选择。
/// </remarks>
public sealed record HomeCard
{
    public required string Title { get; init; }

    /// <summary>副标题：曲目是艺人，歌单是创建者或曲目数。</summary>
    public string Subtitle { get; init; } = "";

    public Uri? CoverImage { get; init; }

    /// <summary>可播的曲目。与 <see cref="Playlist"/> 互斥。</summary>
    public Track? Track { get; init; }

    /// <summary>歌单。与 <see cref="Track"/> 互斥。</summary>
    public Playlist? Playlist { get; init; }

    public bool IsPlayable => Track is not null;

    public bool IsPlaylist => Playlist is not null;
}
