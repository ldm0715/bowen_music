using Bodian.Core.Api.Paging;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 分页换算与游标推进。这里守的是「首页页号各家不同」与「短页不能用来推算偏移」两件事。
/// </summary>
public sealed class PagingTests
{
    // ── 页号换算 ────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>search/*</c> 从 0 开始，歌单与收藏从 1 开始。这两条一混，第一页就会重复或漏掉。
    /// </summary>
    [Theory]
    // search/*：首页 0
    [InlineData(true, 0, 30, 0)]
    [InlineData(true, 30, 30, 1)]
    [InlineData(true, 60, 30, 2)]
    // playlist / collect：首页 1
    [InlineData(false, 0, 30, 1)]
    [InlineData(false, 30, 30, 2)]
    [InlineData(false, 60, 30, 3)]
    public void ToPageNumber_RespectsFirstPage(bool zeroBased, int offset, int pageSize, int expected)
    {
        var convention = zeroBased ? PagingConvention.ZeroBased : PagingConvention.OneBased;

        Assert.Equal(expected, convention.ToPageNumber(offset, pageSize));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(30, 30)]
    [InlineData(100, 100)]
    [InlineData(1000, 100)]     // 上限 100
    public void NormalizePageSize_ClampsToLegalRange(int requested, int expected)
        => Assert.Equal(expected, PagingConvention.OneBased.NormalizePageSize(requested));

    [Fact]
    public void Conventions_UseTheDocumentedParameterNames()
    {
        Assert.Equal("pn", PagingConvention.OneBased.PageParam);
        Assert.Equal("rn", PagingConvention.OneBased.SizeParam);
        Assert.Equal(1, PagingConvention.OneBased.FirstPage);
        Assert.Equal(0, PagingConvention.ZeroBased.FirstPage);
    }

    // ── 游标推进 ────────────────────────────────────────────────────────────

    [Fact]
    public void Cursor_StartsAtTheFirstPage()
    {
        var cursor = new PagedCursor(PagingConvention.OneBased, pageSize: 30);

        Assert.Equal(0, cursor.Offset);
        Assert.Equal(1, cursor.PageNumber);
        Assert.Equal(30, cursor.RequestedCount);
        Assert.False(cursor.Exhausted);
    }

    /// <summary>PageSize 超范围时被夹住，而不是拿一个服务端会拒绝的值去请求。</summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1000, 100)]
    public void Cursor_ClampsPageSize(int requested, int expected)
    {
        var cursor = new PagedCursor(PagingConvention.OneBased, requested);

        Assert.Equal(expected, cursor.PageSize);
        Assert.Equal(expected, cursor.RequestedCount);
    }

    /// <summary>
    /// **这条是分页层的核心。**
    /// </summary>
    /// <remarks>
    /// 服务端会省略不可用曲目：实测出现过歌单标称 121 首、第一页只回 99 首。
    /// 若按「收到多少就前进多少」，这一页的差会逐页累积，最终漏歌或重复。
    /// 正确做法是**按请求的页大小前进**。
    /// </remarks>
    [Fact]
    public void Advance_UsesPageSize_NotReceivedCount()
    {
        var cursor = new PagedCursor(PagingConvention.OneBased, pageSize: 100);

        cursor.Advance(receivedCount: 99);

        Assert.Equal(100, cursor.Offset);          // 不是 99
        Assert.Equal(2, cursor.PageNumber);
        Assert.False(cursor.Exhausted);
    }

    [Fact]
    public void Advance_AccumulatesByPageSizeOverManyPages()
    {
        var cursor = new PagedCursor(PagingConvention.ZeroBased, pageSize: 50);

        cursor.Advance(50);
        cursor.Advance(48);      // 短页也照样按 50 前进
        cursor.Advance(50);

        Assert.Equal(150, cursor.Offset);
        Assert.Equal(3, cursor.PageNumber);        // 0 基 → 150/50 = 3
    }

    /// <summary>收到 0 条才算到底——短页不能当成到底。</summary>
    [Fact]
    public void Advance_ZeroMeansExhausted()
    {
        var cursor = new PagedCursor(PagingConvention.OneBased, pageSize: 100);

        cursor.Advance(99);
        Assert.False(cursor.Exhausted);            // 短页 ≠ 到底

        cursor.Advance(0);
        Assert.True(cursor.Exhausted);
    }

    [Fact]
    public void Advance_AfterExhausted_IsNoOp()
    {
        var cursor = new PagedCursor(PagingConvention.OneBased, pageSize: 30);

        cursor.Advance(0);
        cursor.Advance(30);

        Assert.True(cursor.Exhausted);
        Assert.Equal(0, cursor.Offset);
    }

    [Fact]
    public void ToQuery_ProducesPnAndRn()
    {
        var cursor = new PagedCursor(PagingConvention.ZeroBased, pageSize: 20);
        cursor.Advance(20);

        var query = cursor.ToQuery();

        Assert.Equal(new KeyValuePair<string, string>("pn", "1"), query[0]);
        Assert.Equal(new KeyValuePair<string, string>("rn", "20"), query[1]);
    }

    [Fact]
    public void ToQuery_ThrowsWhenExhausted()
    {
        var cursor = new PagedCursor(PagingConvention.OneBased);
        cursor.Advance(0);

        Assert.Throws<InvalidOperationException>(() => cursor.ToQuery());
    }

    // ── 单页拉取 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task FetchNext_AdvancesAndReportsOffset()
    {
        var cursor = new PagedCursor(PagingConvention.OneBased, pageSize: 100);
        var calls = new List<IReadOnlyList<KeyValuePair<string, string>>>();

        var first = await PagedList.FetchNextAsync<int>(cursor, (query, _) =>
        {
            calls.Add(query);
            return Task.FromResult(new PagedResult<int>([.. Enumerable.Range(1, 99)], 0, 100, 121));
        }, TestContext.Current.CancellationToken);

        Assert.Equal(99, first.Items.Count);
        Assert.Equal(0, first.Offset);
        Assert.Equal(121, first.Total);
        Assert.Equal(100, cursor.Offset);

        var second = await PagedList.FetchNextAsync<int>(cursor, (query, _) =>
        {
            calls.Add(query);
            return Task.FromResult(new PagedResult<int>([], 0, 100, 121));
        }, TestContext.Current.CancellationToken);

        Assert.True(second.IsEmpty);
        Assert.True(cursor.Exhausted);

        // 第二次请求的 pn 必须是 2（按页边界推进），不是 1
        Assert.Equal("2", calls[1][0].Value);
    }

    /// <summary>到底之后再调不发请求——避免无意义地骚扰服务端。</summary>
    [Fact]
    public async Task FetchNext_DoesNotCallWhenExhausted()
    {
        var cursor = new PagedCursor(PagingConvention.OneBased, pageSize: 30);
        cursor.Advance(0);

        var called = false;
        var page = await PagedList.FetchNextAsync<int>(cursor, (_, _) =>
        {
            called = true;
            return Task.FromResult(new PagedResult<int>([], 0, 30, null));
        }, TestContext.Current.CancellationToken);

        Assert.False(called);
        Assert.True(page.IsEmpty);
    }
}
