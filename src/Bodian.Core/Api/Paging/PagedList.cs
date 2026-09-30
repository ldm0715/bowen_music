namespace Bodian.Core.Api.Paging;

/// <summary>
/// 把「游标 + 拉一页」串成一次调用，避免每个调用点都手写推进逻辑。
/// </summary>
/// <remarks>
/// <para>
/// 存在的理由是 <see cref="PagedCursor.Advance"/> 那条规则太容易写错：
/// 收到短页时按条目数推进是直觉做法，也是错的。
/// </para>
/// <para>
/// <b>不自动翻完全部页。</b> 只提供单页拉取 + 推进，循环交给调用方——
/// 因为「拉多少页」取决于界面要多少，把策略写死在这里反而碍事。
/// </para>
/// </remarks>
public static class PagedList
{
    /// <summary>
    /// 拉一页并把游标推进一格。
    /// </summary>
    /// <param name="cursor">游标。<see cref="PagedCursor.Exhausted"/> 时不会再发请求。</param>
    /// <param name="fetch">
    /// 真正拉取的委托，参数是本次要用的 query（<c>pn</c> / <c>rn</c>）。
    /// </param>
    public static async Task<PagedResult<T>> FetchNextAsync<T>(
        PagedCursor cursor,
        Func<IReadOnlyList<KeyValuePair<string, string>>, CancellationToken, Task<PagedResult<T>>> fetch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        ArgumentNullException.ThrowIfNull(fetch);

        if (cursor.Exhausted)
        {
            return new PagedResult<T>([], cursor.Offset, cursor.PageSize, null);
        }

        var offset = cursor.Offset;
        var pageSize = cursor.PageSize;
        var query = cursor.ToQuery();

        var page = await fetch(query, cancellationToken).ConfigureAwait(false);

        cursor.Advance(page.Items.Count);

        return page with { Offset = offset, PageSize = pageSize };
    }
}
