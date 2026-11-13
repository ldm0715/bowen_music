namespace Bowen.Core.Models;

/// <summary>
/// 一个专辑。已购专辑与收藏的专辑两个列表共用。
/// </summary>
/// <remarks>
/// <b>只映射真正有消费方的字段</b>（与 <see cref="Track"/>、<see cref="Playlist"/> 同一条规矩）：
/// 列表要 id、名称、艺人、封面、曲目数。底层 DTO 里还有发行日期、购买数、价格等字段，
/// **没有消费方就不上来**。
/// </remarks>
public sealed record Album
{
    /// <summary>专辑 id。</summary>
    public required long Id { get; init; }

    /// <summary>
    /// 专辑对象自身的 <c>sourceType</c>。上报播放历史时作为条目的 <c>playlistType</c>。
    /// </summary>
    /// <remarks>
    /// 专辑列表与详情实测都是 <c>6</c>；<b>0 表示这个来源没带</b>（例如从曲目行合成出来的专辑）。
    /// 值为 0 时不上报专辑历史，不要把 0 当成有效类型发给服务端。
    /// </remarks>
    public int SourceType { get; init; }

    /// <summary>专辑名。</summary>
    public required string Name { get; init; }

    /// <summary>艺人名。</summary>
    public string ArtistText { get; init; } = "";

    /// <summary>
    /// 艺人 id，用来从专辑跳到歌手页。
    /// </summary>
    /// <remarks>
    /// 服务端一直有给（<c>artistId</c>），0 表示这个来源没带 ——
    /// 从曲目行的「查看专辑」合成出来的专辑就是这种，详情加载回来后会补上。
    /// </remarks>
    public long ArtistId { get; init; }

    /// <summary>
    /// 艺人明细。多歌手专辑里 <see cref="ArtistText"/> 是一串名字，
    /// 这一份才是每人一条（含 id），选人弹窗用它。
    /// </summary>
    public IReadOnlyList<TrackArtist> Artists { get; init; } = [];

    /// <summary>封面地址。</summary>
    public Uri? CoverImage { get; init; }

    /// <summary>服务端标称的曲目数。<b>只用于展示。</b></summary>
    public int MusicCount { get; init; }

    /// <summary>艺人头像。只有专辑详情会填。</summary>
    public Uri? ArtistCover { get; init; }

    /// <summary>发行日，形如 <c>2003-07-31</c>。只有专辑详情会填。</summary>
    public string ReleaseDate { get; init; } = "";

    /// <summary>专辑简介。<b>实测很长</b>（整篇企划文案），界面上要折叠。</summary>
    public string Description { get; init; } = "";

    /// <summary>有没有可展示的简介。</summary>
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
}
