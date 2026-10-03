namespace Bodian.Core.Models;

public sealed record Artist
{
    public required long Id { get; init; }
    public required string Name { get; init; }
    public Uri? CoverImage { get; init; }
    public int SongCount { get; init; }
    public int AlbumCount { get; init; }

    /// <summary>别名，如周杰伦的 <c>Jay Chou</c>。只有歌手详情接口会给。</summary>
    public string AliasName { get; init; } = "";

    /// <summary>粉丝数。只有歌手详情接口会给。</summary>
    public long FansCount { get; init; }

    /// <summary>歌手介绍，<b>实测极长</b>（整篇人物生平），界面上要单独滚动。</summary>
    public string Description { get; init; } = "";

    public string Summary => $"{SongCount} 首歌曲 · {AlbumCount} 张专辑";

    /// <summary>
    /// 两个计数是不是已知的。
    /// </summary>
    /// <remarks>
    /// 带计数的歌手只有搜索结果与歌手接口会给；<b>从一首歌的歌手明细现合成的没有</b>
    /// （曲目行的「查看歌手」就是这种），那时 <see cref="Summary"/> 会拼出
    /// 「0 首歌曲 · 0 张专辑」—— 与其显示错的，不如整行不显示。
    /// </remarks>
    public bool HasCounts => SongCount > 0 || AlbumCount > 0;

    public bool HasAlias => !string.IsNullOrWhiteSpace(AliasName);

    public bool HasFans => FansCount > 0;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
}

public sealed record SearchHotWord(string Keyword, int Rank);
