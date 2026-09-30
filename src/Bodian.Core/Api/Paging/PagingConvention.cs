namespace Bodian.Core.Api.Paging;

/// <summary>
/// 分页约定。**首页页号各家不同**，这是波点接口最容易踩的一处不一致。
/// </summary>
/// <remarks>
/// 实测（<c>bodian-api-reference.md</c> 1.6 节）：
/// <list type="bullet">
/// <item><c>search/*/list</c> 从 <b>0</b> 开始</item>
/// <item><c>service/playlist/{id}/musicList</c>、<c>service/collect/4/list</c>、<c>service/collect/6/list</c>
/// 从 <b>1</b> 开始</item>
/// <item><c>service/album/music/{id}</c>、<c>service/artist/music/{id}</c>、<c>service/artist/album/{id}</c>
/// 从 <b>0</b> 开始</item>
/// </list>
/// </remarks>
public sealed record PagingConvention(
    string PageParam = "pn",
    string SizeParam = "rn",
    int FirstPage = 1,
    int MaxPageSize = 100)
{
    /// <summary>首页为 0：<c>search/*</c>、专辑与艺人的曲目列表用这个。</summary>
    public static readonly PagingConvention ZeroBased = new(FirstPage: 0);

    /// <summary>首页为 1：歌单曲目、收藏列表用这个。</summary>
    public static readonly PagingConvention OneBased = new(FirstPage: 1);

    /// <summary>把「已消费条数」换算成接口要的页号。</summary>
    public int ToPageNumber(int offset, int pageSize) => (offset / pageSize) + FirstPage;

    /// <summary>合法范围 1–100。超出会被夹到边界，而不是由服务端返回一个难懂的错。</summary>
    public int NormalizePageSize(int requested) => Math.Clamp(requested, 1, MaxPageSize);
}
