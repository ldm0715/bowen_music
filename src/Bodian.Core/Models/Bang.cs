namespace Bodian.Core.Models;

/// <summary>
/// 一个排行榜。
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="PreviewTracks"/> 只是预览，不是全部。</b> 排行榜首页（<c>service/home/bangNew</c>）
/// 每个榜只带前几首；完整榜单要按 <see cref="Id"/> 去
/// <c>service/bang/{id}/musics</c> 分页取 —— 实测一个榜有 100 首。
/// </para>
/// <para>
/// 所以界面上「每个榜展示前十首」不能只靠预览：预览可能不足十首，
/// 得按 id 再取一次（<c>rn=10</c>）。
/// </para>
/// </remarks>
public sealed record Bang
{
    public required long Id { get; init; }

    public required string Name { get; init; }

    public Uri? CoverImage { get; init; }

    /// <summary>更新时间的人话形式，形如 <c>09-30更新</c>。</summary>
    public string UpdateText { get; init; } = "";

    /// <summary>榜单首页带来的预览曲目。<b>不是完整榜单。</b></summary>
    public IReadOnlyList<Track> PreviewTracks { get; init; } = [];
}

/// <summary>
/// 排行榜首页里的一组。
/// </summary>
/// <param name="Title">组名，形如「热力榜」「全球榜」。</param>
/// <param name="Bangs">这一组里的榜。</param>
public sealed record BangSection(string Title, IReadOnlyList<Bang> Bangs);
