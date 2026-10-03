namespace Bodian.Core.Api.Paging;

/// <summary>
/// 分页约定。**只有 <c>search/*</c> 是 0 基，其余全从 1 开始。**
/// </summary>
/// <remarks>
/// <para>
/// 实测（<c>bodian-api-reference.md</c> 1.6 节）：
/// <list type="bullet">
/// <item><c>search/*/list</c> 从 <b>0</b> 开始（<c>pn=0</c> 回 1–5 条，<c>pn=1</c> 回 6–10 条）</item>
/// <item><c>service/playlist/{id}/musicList</c>、<c>service/collect/4/list</c>、<c>service/collect/6/list</c>
/// 从 <b>1</b> 开始</item>
/// <item><c>service/album/music/{id}</c>、<c>service/artist/music/{id}</c>、<c>service/artist/album/{id}</c>
/// 也从 <b>1</b> 开始</item>
/// </list>
/// </para>
/// <para>
/// ★ <b>这三个 <c>service</c> 接口的页码写错会"看起来正常"</b>：传 <c>pn=0</c> 不报错，
/// 服务端当第 1 页处理。于是首屏拿到的确实是对的，游标推进后发 <c>pn=1</c> —— 又是第 1 页，
/// 追加进去正好把整个列表翻倍（实测 28 张专辑显示成 56 张）。
/// 判断办法是拿 <c>pn=1</c> 与 <c>pn=2</c> 的两组 id 比对，别看 <c>pn=0</c>。
/// </para>
/// </remarks>
public sealed record PagingConvention(
    string PageParam = "pn",
    string SizeParam = "rn",
    int FirstPage = 1,
    int MaxPageSize = 100)
{
    /// <summary>首页为 0：<b>只有 <c>search/*/list</c> 用这个</b>。</summary>
    public static readonly PagingConvention ZeroBased = new(FirstPage: 0);

    /// <summary>首页为 1：歌单曲目、收藏列表用这个。</summary>
    public static readonly PagingConvention OneBased = new(FirstPage: 1);

    /// <summary>把「已消费条数」换算成接口要的页号。</summary>
    public int ToPageNumber(int offset, int pageSize) => (offset / pageSize) + FirstPage;

    /// <summary>合法范围 1–100。超出会被夹到边界，而不是由服务端返回一个难懂的错。</summary>
    public int NormalizePageSize(int requested) => Math.Clamp(requested, 1, MaxPageSize);
}
