namespace Bodian.Core.Models;

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

    /// <summary>专辑名。</summary>
    public required string Name { get; init; }

    /// <summary>艺人名。</summary>
    public string ArtistText { get; init; } = "";

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
