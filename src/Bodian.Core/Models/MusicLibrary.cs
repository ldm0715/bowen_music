namespace Bodian.Core.Models;

/// <summary>
/// 乐库里的一个大类，如「流行」「爵士」。
/// </summary>
/// <param name="Id">分类 id（字符串，如 <c>"2005"</c>）。</param>
/// <param name="Name">中文名，界面上的分类名。</param>
/// <param name="Title">大字标题，如 <c>POP MUSIC</c>。</param>
/// <param name="Description">分类简介。</param>
/// <param name="CoverImage">分类封面。</param>
/// <param name="AlbumCount">该分类下的专辑总数。</param>
/// <param name="Children">子类。</param>
public sealed record MusicCategoryGroup(
    string Id,
    string Name,
    string Title,
    string Description,
    Uri? CoverImage,
    int AlbumCount,
    IReadOnlyList<MusicCategoryChild> Children);

/// <summary>
/// 乐库里的一个子类，如「国语流行」。
/// </summary>
/// <param name="Id">子类 id（如 <c>8101</c>）。</param>
/// <param name="Name">子类名。</param>
/// <param name="Title">形如 <c>[ 国语流行 - Chinese Pop ]</c> 的小标题。</param>
/// <param name="Description">子类简介。</param>
/// <param name="SampleAlbum">
/// 该子类的**代表专辑** —— <c>navigation</c> 顺手带的样本，一个子类只有这一张。
/// </param>
/// <remarks>
/// <b>完整列表还没接通</b>：那要调 <c>play/music/library/albums</c>，
/// 而它的 <c>pTypeId</c>/<c>cTypeId</c> 取值尚未确定（见 <c>reverse/findings/07-musiclib.md</c>）。
/// 所以本版先把这一张代表专辑画出来，点它进专辑详情 —— 那条路是通的。
/// </remarks>
public sealed record MusicCategoryChild(
    string Id,
    string Name,
    string Title,
    string Description,
    Album? SampleAlbum)
{
    /// <summary>代表专辑的封面。没有样本专辑时为 <c>null</c>，界面留一块底色。</summary>
    /// <remarks>
    /// 写成属性而不是在 XAML 里直接绑 <c>SampleAlbum.CoverImage</c>：
    /// <c>x:Bind</c> 对可空链会在运行期炸，而这里返回 <c>null</c> 是正常情况。
    /// </remarks>
    public Uri? SampleAlbumCover => SampleAlbum?.CoverImage;

    /// <summary>
    /// 卡片下面那行小字：有代表专辑时是 <c>曲名 · 歌手</c>，没有时退回子类的小标题。
    /// </summary>
    public string SampleAlbumText => SampleAlbum is { } album
        ? Formats_Join(album.Name, album.ArtistText)
        : Title;

    private static string Formats_Join(string name, string artist) =>
        string.IsNullOrWhiteSpace(artist) ? name : $"{name} · {artist}";
}
