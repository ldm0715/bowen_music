namespace Bodian.Core.Api.Paging;

/// <summary>
/// 一页数据。
/// </summary>
/// <param name="Items">本页条目。</param>
/// <param name="Offset">本页对应的偏移量。**下一个游标要用它推进**，不要用条目数推算。</param>
/// <param name="PageSize">请求的页大小。</param>
/// <param name="Total">服务端给的总数。<b>不可信</b>，见 <see cref="PagedCursor"/> 的说明。</param>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Offset, int PageSize, int? Total)
{
    public bool IsEmpty => Items.Count == 0;
}

/// <summary>
/// 翻页游标。
/// </summary>
/// <remarks>
/// <para>
/// <b>游标只按「请求页边界」推进，绝不用返回条数推算下一页偏移。</b>
/// 服务端会省略不可用曲目：实测出现过歌单标称 121 首、第一页只返回 99 首的情况。
/// 若按返回条数推进，这 1 条的差会逐页累积，最终漏歌或重复。
/// </para>
/// <para>
/// <b>「还有下一页」的判据是 <c>items.Count &gt; 0</c></b>，不是 <c>Count == PageSize</c>
/// （短页不代表到底），也不是 <c>Offset + Count &lt; Total</c>（<c>Total</c> 本身不可信）。
/// </para>
/// </remarks>
public sealed class PagedCursor
{
    private readonly PagingConvention _convention;

    public PagedCursor(PagingConvention convention, int pageSize = 30)
    {
        ArgumentNullException.ThrowIfNull(convention);

        _convention = convention;
        PageSize = convention.NormalizePageSize(pageSize);
    }

    /// <summary>已经请求过的条目数。**按页大小累加，不按实际收到数累加。**</summary>
    public int Offset { get; private set; }

    public int PageSize { get; }

    /// <summary>换算后的页号，直接填进 query 的 <c>pn</c>。</summary>
    public int PageNumber => _convention.ToPageNumber(Offset, PageSize);

    /// <summary>页大小，直接填进 query 的 <c>rn</c>。</summary>
    public int RequestedCount => PageSize;

    public string PageParam => _convention.PageParam;

    public string SizeParam => _convention.SizeParam;

    /// <summary>已经翻到底。</summary>
    public bool Exhausted { get; private set; }

    /// <summary>请求参数，直接拼进 query。</summary>
    public IReadOnlyList<KeyValuePair<string, string>> ToQuery()
    {
        if (Exhausted)
        {
            throw new InvalidOperationException("已经翻到底了；再请求一次没有意义。");
        }

        return
        [
            new(_convention.PageParam, PageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(_convention.SizeParam, RequestedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ];
    }

    /// <summary>
    /// 用「本页实际收到的条数」推进游标。
    /// </summary>
    /// <remarks>
    /// <b>无论收到多少，偏移都按 <see cref="PageSize"/> 前进</b>——收到 99 条（页大小 100）
    /// 时下一页仍然从 100 开始，而不是从 99 开始。这是本类存在的全部意义。
    /// <para>收到 0 条意味着到底了（<see cref="Exhausted"/>）。</para>
    /// </remarks>
    public void Advance(int receivedCount)
    {
        if (Exhausted)
        {
            return;
        }

        if (receivedCount <= 0)
        {
            Exhausted = true;
            return;
        }

        Offset += PageSize;
    }
}
