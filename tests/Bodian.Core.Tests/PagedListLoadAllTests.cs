using Bodian.Core.Api.Paging;
using Bodian.WinUI.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 「播放全部」用的 <see cref="PagedList{T}.LoadAllAsync"/>：
/// 专辑首屏只回来一页，不把剩下的拉完的话队列是不全的。
/// </summary>
public sealed class PagedListLoadAllTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>按顺序吐给定几页，吐完一页不给就回空页（服务端「到底了」的形状）。</summary>
    private sealed class PagedSource
    {
        private readonly Queue<int[]> _pages;
        public int Calls { get; private set; }

        public PagedSource(params int[][] pages) => _pages = new Queue<int[]>(pages);

        public Task<PagedResult<int>> FetchAsync(PagedCursor cursor, CancellationToken cancellationToken)
        {
            Calls++;
            var items = _pages.Count > 0 ? _pages.Dequeue() : [];
            return Task.FromResult(new PagedResult<int>(items, cursor.Offset, cursor.PageSize, null));
        }
    }

    private static PagedList<int> ListOver(PagedSource source) =>
        new(source.FetchAsync, NullLogger.Instance, "测试列表", "暂无内容", PagingConvention.OneBased);

    /// <summary>
    /// 一页一页拉到服务端给空页为止。
    /// </summary>
    /// <remarks>
    /// 请求次数是 <b>页数 + 1</b>：判「到底了」靠的是收到一个空页
    /// （游标只在收到 0 条时才 <c>Exhausted</c>），不是靠页数或总数。
    /// </remarks>
    [Fact]
    public async Task LoadAll_LaysEveryPageUntilTheEmptyOne()
    {
        var source = new PagedSource([1, 2], [3, 4], [5]);
        var list = ListOver(source);

        var complete = await list.LoadAllAsync(cancellationToken: Ct);

        Assert.True(complete);
        Assert.Equal([1, 2, 3, 4, 5], list.Items);

        // 1 次首屏 + 3 次续加载（最后一页是空页，翻到底）。
        Assert.Equal(4, source.Calls);
        Assert.False(list.HasMore);
    }

    /// <summary>只有一页时是两次请求：首屏那次，加一次确认到底的空页。</summary>
    [Fact]
    public async Task LoadAll_SinglePage_CostsTwoRequests()
    {
        var source = new PagedSource([1, 2, 3]);
        var list = ListOver(source);

        Assert.True(await list.LoadAllAsync(cancellationToken: Ct));

        Assert.Equal(2, source.Calls);
        Assert.Equal([1, 2, 3], list.Items);
    }

    /// <summary>
    /// 撞上页数上限就停，并如实返回 <c>false</c>。
    /// </summary>
    /// <remarks>
    /// 上限是防「列表实际上无穷」的保险丝。返回 <c>false</c> 让调用方知道队列没拉全，
    /// 而不是假装拉完了。
    /// </remarks>
    [Fact]
    public async Task LoadAll_StopsAtThePageCap()
    {
        var source = new PagedSource([1, 2], [3, 4], [5, 6], [7, 8], [9, 10]);
        var list = ListOver(source);

        var complete = await list.LoadAllAsync(2, Ct);

        Assert.False(complete);
        Assert.Equal([1, 2, 3, 4, 5, 6], list.Items);

        // 1 次首屏 + 2 次续加载。
        Assert.Equal(3, source.Calls);
        Assert.True(list.HasMore);
    }

    /// <summary>
    /// 中途失败就停，<b>已经拉到的部分保留</b>，不把整次操作判成失败。
    /// </summary>
    /// <remarks>
    /// 队列短一点比什么都不播好 —— 调用方拿到 <c>false</c> 时应当照常播现有内容。
    /// </remarks>
    [Fact]
    public async Task LoadAll_Failure_KeepsWhatWasLoaded()
    {
        var source = new PagedSource([1, 2]);
        var calls = 0;

        var list = new PagedList<int>(
            (cursor, _) =>
            {
                if (calls++ == 1)
                {
                    throw new InvalidOperationException("服务端炸了");
                }

                return source.FetchAsync(cursor, _);
            },
            NullLogger.Instance,
            "测试列表",
            "暂无内容",
            PagingConvention.OneBased);

        var complete = await list.LoadAllAsync(cancellationToken: Ct);

        Assert.False(complete);
        Assert.Equal([1, 2], list.Items);
        Assert.True(list.LoadFailed);
    }

    /// <summary>首屏就失败：不抛，列表空着，照常返回 <c>false</c>。</summary>
    [Fact]
    public async Task LoadAll_FirstPageFailure_ReturnsFalse()
    {
        var list = new PagedList<int>(
            (_, _) => throw new InvalidOperationException("服务端炸了"),
            NullLogger.Instance,
            "测试列表",
            "暂无内容",
            PagingConvention.OneBased);

        Assert.False(await list.LoadAllAsync(cancellationToken: Ct));
        Assert.Empty(list.Items);
        Assert.True(list.LoadFailed);
    }

    /// <summary>空列表：一次请求就到底，返回 <c>true</c>（确实没有更多了）。</summary>
    [Fact]
    public async Task LoadAll_EmptyList_IsComplete()
    {
        var source = new PagedSource([]);
        var list = ListOver(source);

        Assert.True(await list.LoadAllAsync(cancellationToken: Ct));
        Assert.Empty(list.Items);
    }

    /// <summary>上限必须是正数，否则循环条件会直接失效。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task LoadAll_NonPositiveCap_Throws(int maxPages)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => ListOver(new PagedSource([1])).LoadAllAsync(maxPages, Ct));
    }
}
