using Bodian.Core.Api;
using Bodian.Core.Api.Paging;
using Bodian.Core.Models;
using Bodian.Core.Tests.Support;
using Bodian.WinUI.Playback;
using Bodian.WinUI.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 歌单详情与「我喜欢的」那一族的「播放全部」：基类 <see cref="PlaylistTracksViewModel.LoadAllAsync"/>。
/// </summary>
/// <remarks>
/// 与 <see cref="PagedListLoadAllTests"/> 是同一套语义的**两份实现** —— 那边是专辑那一族
/// （<c>PagedList&lt;T&gt;</c>，取数委托构造时固定），这边是歌单那一族（手写游标，
/// 要先回答「是哪个歌单」）。改一处时另一处也要看。
/// </remarks>
public sealed class PlaylistTracksLoadAllTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>按顺序吐给定几页，吐完一页不给就回空页（服务端「到底了」的形状）。</summary>
    private sealed class PagedSource
    {
        private readonly Queue<Track[]> _pages;

        public int Calls { get; private set; }

        public bool FailAfterFirstPage { get; init; }

        public PagedSource(params int[][] pages) =>
            _pages = new Queue<Track[]>(pages.Select(page => page.Select(TrackOf).ToArray()));

        public Task<PagedResult<Track>> FetchAsync(PagedCursor cursor, CancellationToken cancellationToken)
        {
            Calls++;

            if (FailAfterFirstPage && Calls > 1)
            {
                throw new InvalidOperationException("服务端炸了");
            }

            var items = _pages.Count > 0 ? _pages.Dequeue() : [];
            return Task.FromResult(new PagedResult<Track>(items, cursor.Offset, cursor.PageSize, null));
        }
    }

    private static Track TrackOf(int id) => new() { Id = id, Title = $"曲目 {id}" };

    /// <summary>最小的歌单详情页实现：只回答「是哪个歌单」，其余交给基类。</summary>
    private sealed class TestPlaylistViewModel : PlaylistTracksViewModel
    {
        public TestPlaylistViewModel(IBodianApi api, PlaybackCoordinator coordinator)
            : base(api, coordinator, NullLogger.Instance)
        {
        }

        protected override Task<Playlist?> ResolvePlaylistAsync(CancellationToken cancellationToken) =>
            Task.FromResult<Playlist?>(new Playlist { Id = 1, Name = "测试歌单", SourceType = 5 });

        protected override string MissingText => "找不到这个歌单";

        protected override string EmptyText => "这个歌单里还没有歌";
    }

    private static PlaylistTracksViewModel ViewModelOver(PagedSource source, PlaybackApiStub? api = null)
    {
        api ??= new PlaybackApiStub();
        api.GetPlaylistTracks = (_, _, cursor, token) => source.FetchAsync(cursor, token);

        var coordinator = new PlaybackCoordinator(api, new FakePlaybackEngine(), new FakePlayHistoryStore());
        return new TestPlaylistViewModel(api, coordinator);
    }

    /// <summary>一页一页拉到服务端给空页为止。</summary>
    [Fact]
    public async Task LoadAll_LaysEveryPageUntilTheEmptyOne()
    {
        var source = new PagedSource([1, 2], [3, 4], [5]);
        var viewModel = ViewModelOver(source);

        var complete = await viewModel.LoadAllAsync(cancellationToken: Ct);

        Assert.True(complete);
        Assert.Equal([1, 2, 3, 4, 5], viewModel.Tracks.Select(track => (int)track.Id).ToArray());

        // 1 次首屏 + 3 次续加载（最后一页是空页，翻到底）。
        Assert.Equal(4, source.Calls);
        Assert.False(viewModel.HasMore);
    }

    /// <summary>只有一页时是两次请求：首屏那次，加一次确认到底的空页。</summary>
    [Fact]
    public async Task LoadAll_SinglePage_CostsTwoRequests()
    {
        var source = new PagedSource([1, 2, 3]);
        var viewModel = ViewModelOver(source);

        Assert.True(await viewModel.LoadAllAsync(cancellationToken: Ct));

        Assert.Equal(2, source.Calls);
        Assert.Equal(3, viewModel.Tracks.Count);
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
        var viewModel = ViewModelOver(source);

        Assert.False(await viewModel.LoadAllAsync(2, Ct));
        Assert.Equal(6, viewModel.Tracks.Count);

        // 1 次首屏 + 2 次续加载。
        Assert.Equal(3, source.Calls);
        Assert.True(viewModel.HasMore);
    }

    /// <summary>中途失败就停，**已经拉到的部分保留**，不把整次操作判成失败。</summary>
    [Fact]
    public async Task LoadAll_Failure_KeepsWhatWasLoaded()
    {
        var source = new PagedSource([1, 2]) { FailAfterFirstPage = true };
        var viewModel = ViewModelOver(source);

        var complete = await viewModel.LoadAllAsync(cancellationToken: Ct);

        Assert.False(complete);
        Assert.Equal([1, 2], viewModel.Tracks.Select(track => (int)track.Id).ToArray());
        Assert.True(viewModel.LoadFailed);
    }

    /// <summary>首屏就失败：不抛，列表空着，照常返回 <c>false</c>。</summary>
    /// <remarks>
    /// 只判 <c>!HasMore</c> 会把「什么都没拉到」当成「拉完了」——
    /// <c>ReloadAsync</c> 开头就把 <c>HasMore</c> 清成了 false。
    /// </remarks>
    [Fact]
    public async Task LoadAll_FirstPageFailure_ReturnsFalse()
    {
        var api = new PlaybackApiStub();
        api.GetPlaylistTracks = (_, _, _, _) => throw new InvalidOperationException("服务端炸了");

        var coordinator = new PlaybackCoordinator(api, new FakePlaybackEngine(), new FakePlayHistoryStore());
        var viewModel = new TestPlaylistViewModel(api, coordinator);

        Assert.False(await viewModel.LoadAllAsync(cancellationToken: Ct));
        Assert.Empty(viewModel.Tracks);
        Assert.True(viewModel.LoadFailed);
    }

    /// <summary>空歌单：一次请求就到底，返回 <c>true</c>（确实没有更多了）。</summary>
    [Fact]
    public async Task LoadAll_EmptyPlaylist_IsComplete()
    {
        var viewModel = ViewModelOver(new PagedSource([]));

        Assert.True(await viewModel.LoadAllAsync(cancellationToken: Ct));
        Assert.Empty(viewModel.Tracks);
    }

    /// <summary>上限必须是正数，否则循环条件会直接失效。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task LoadAll_NonPositiveCap_Throws(int maxPages)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => ViewModelOver(new PagedSource([1])).LoadAllAsync(maxPages, Ct));
    }
}
