using Bodian.Core.Api;
using Bodian.Core.Api.Paging;
using Bodian.Core.Models;
using Bodian.Core.Services;
using Bodian.Core.Services.Abstractions;
using Bodian.Core.Services.Implementations;
using Bodian.Core.Tests.Support;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 「我喜欢的」状态缓存。零真实网络 —— API 用委托桩，桩自己维护一份服务端侧的曲目集合。
/// </summary>
/// <remarks>
/// 这里要守住的核心行为是**拉取时机**：判定时按需拉一次，写成功后标脏、推迟到下一次判定才重拉。
/// </remarks>
public sealed class LikedSongsServiceTests
{
    private const string Uid = "50303440";
    private const long PlaylistId = 99980832;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Track TrackWith(long id) => new() { Id = id, Title = $"曲目 {id}" };

    /// <summary>
    /// 一次全量同步的曲目请求次数。
    /// </summary>
    /// <remarks>
    /// <b>数据页数 + 1</b>：这个游标只能靠「收到 0 条」判定翻到底，短页不算到底
    /// （服务端会省略不可用曲目）。空列表是 1 次，因为首页就是那个终止请求。
    /// </remarks>
    private static int PageCalls(int dataPages) => dataPages + 1;

    /// <summary>带服务端侧状态的桩：写请求真的会改它，所以「重拉」能被观察到。</summary>
    private sealed class Server
    {
        public List<long> LikedIds { get; } = [];

        public bool HasLikedPlaylist { get; set; } = true;

        public Exception? WriteError { get; set; }

        /// <summary>只让这几首写失败，用来验「单首失败不中断」。</summary>
        public HashSet<long> FailingIds { get; } = [];

        public int FondCalls { get; private set; }

        public int TrackCalls { get; private set; }

        public int AddCalls { get; private set; }

        public int RemoveCalls { get; private set; }

        public PlaybackApiStub BuildStub()
        {
            var stub = new PlaybackApiStub();
            stub.GetLikedPlaylist = _ =>
            {
                FondCalls++;
                return Task.FromResult(HasLikedPlaylist
                    ? new Playlist { Id = PlaylistId, Name = "我喜欢的" }
                    : null);
            };
            stub.GetPlaylistTracks = (_, _, cursor, _) =>
            {
                TrackCalls++;
                // 与生产实现一致：先取本次偏移，再用**实际收到的条数**推进游标。
                var offset = cursor.Offset;
                var page = LikedIds.Skip(offset).Take(cursor.PageSize).Select(TrackWith).ToArray();
                cursor.Advance(page.Length);
                return Task.FromResult(new PagedResult<Track>(page, offset, cursor.PageSize, LikedIds.Count));
            };
            stub.AddPlaylistMusic = (_, musicIds, _) =>
            {
                AddCalls++;
                if (WriteError is { } error) return Task.FromException(error);
                if (musicIds.Any(FailingIds.Contains)) return Task.FromException(new HttpRequestException("这首歌写不了"));
                foreach (var id in musicIds)
                {
                    if (!LikedIds.Contains(id)) LikedIds.Add(id);
                }

                return Task.CompletedTask;
            };
            stub.RemovePlaylistMusic = (_, musicIds, _) =>
            {
                RemoveCalls++;
                if (WriteError is { } error) return Task.FromException(error);
                if (musicIds.Any(FailingIds.Contains)) return Task.FromException(new HttpRequestException("这首歌写不了"));
                foreach (var id in musicIds) LikedIds.Remove(id);
                return Task.CompletedTask;
            };
            return stub;
        }
    }

    private readonly Server _server = new();
    private readonly BodianSession _session = BodianSession.CreateAnonymous();
    private readonly LikedSongsService _service;

    public LikedSongsServiceTests() => _service = new LikedSongsService(_server.BuildStub(), _session);

    // ── 判定与拉取 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task IsLiked_ReadsTheLikedPlaylistOnce()
    {
        _session.Set(Uid, "test-token");
        _server.LikedIds.AddRange([1, 2, 3]);

        Assert.True(await _service.IsLikedAsync(2, Ct));
        Assert.False(await _service.IsLikedAsync(9, Ct));

        // 第二次判定不再发请求。曲目是 2 次：一页数据 + 一个终止请求（见 PageCalls）。
        Assert.Equal(1, _server.FondCalls);
        Assert.Equal(PageCalls(dataPages: 1), _server.TrackCalls);
    }

    /// <summary>翻页要全部取到，不能只看第一页 —— 更靠后的曲目被漏掉就会误判成「未喜欢」。</summary>
    [Fact]
    public async Task IsLiked_WalksAllPages()
    {
        _session.Set(Uid, "test-token");
        _server.LikedIds.AddRange(Enumerable.Range(1, 250).Select(i => (long)i));

        Assert.True(await _service.IsLikedAsync(250, Ct));
        Assert.Equal(PageCalls(dataPages: 3), _server.TrackCalls);
    }

    [Fact]
    public async Task IsLiked_IsNullWhenNotAuthenticated()
    {
        Assert.Null(await _service.IsLikedAsync(1, Ct));
        Assert.Equal(0, _server.FondCalls);
    }

    /// <summary>账号没有红心歌单是正常状态，不能反复请求。</summary>
    [Fact]
    public async Task IsLiked_IsNullWithoutALikedPlaylistAndDoesNotRefetch()
    {
        _session.Set(Uid, "test-token");
        _server.HasLikedPlaylist = false;

        Assert.Null(await _service.IsLikedAsync(1, Ct));
        Assert.Null(await _service.IsLikedAsync(2, Ct));
        Assert.Equal(1, _server.FondCalls);
    }

    [Fact]
    public async Task IsLiked_IsNullWhenTheReadFailsAndRetriesNextTime()
    {
        _session.Set(Uid, "test-token");
        var stub = new PlaybackApiStub();
        stub.GetLikedPlaylist = _ => throw new HttpRequestException("网络不可用");
        var service = new LikedSongsService(stub, _session);

        // 读取失败按「无法判定」处理，且不写同步标记。
        Assert.Null(await service.IsLikedAsync(1, Ct));
    }

    // ── 写与标脏 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetLiked_AddsAndThenRefetchesOnTheNextJudgementOnly()
    {
        _session.Set(Uid, "test-token");
        Assert.Equal(LikedSongsOutcome.Succeeded, await _service.SetLikedAsync(7, liked: true, Ct));
        Assert.Equal(1, _server.AddCalls);
        Assert.Contains(7L, _server.LikedIds);

        // 写成功后第一次判定会重拉（缓存已标脏）。首次同步时集合为空，重拉时已有 1 首。
        Assert.True(await _service.IsLikedAsync(7, Ct));
        Assert.Equal(2, _server.FondCalls);
        Assert.Equal(PageCalls(dataPages: 0) + PageCalls(dataPages: 1), _server.TrackCalls);

        // 之后又回到「不发请求」。
        Assert.True(await _service.IsLikedAsync(7, Ct));
        Assert.Equal(2, _server.FondCalls);
    }

    [Fact]
    public async Task SetLiked_RemovesAndMarksTheCacheDirtyToo()
    {
        _session.Set(Uid, "test-token");
        _server.LikedIds.Add(7);

        Assert.Equal(LikedSongsOutcome.Succeeded, await _service.SetLikedAsync(7, liked: false, Ct));
        Assert.Equal(1, _server.RemoveCalls);
        Assert.DoesNotContain(7L, _server.LikedIds);

        // 取消同样标脏：下一次判定重拉（此时集合已空，重拉只要那个终止请求）。
        Assert.False(await _service.IsLikedAsync(7, Ct));
        Assert.Equal(PageCalls(dataPages: 1) + PageCalls(dataPages: 0), _server.TrackCalls);
    }

    /// <summary>写失败既不服务端生效，也不该动本地集合或标脏。</summary>
    [Fact]
    public async Task SetLiked_KeepsStateOnFailure()
    {
        _session.Set(Uid, "test-token");
        _server.LikedIds.Add(7);
        await _service.IsLikedAsync(7, Ct);

        _server.WriteError = new HttpRequestException("网络不可用");
        Assert.Equal(LikedSongsOutcome.Failed, await _service.SetLikedAsync(7, liked: false, Ct));

        _server.WriteError = null;
        Assert.True(await _service.IsLikedAsync(7, Ct));
        // 失败没有标脏，所以这一次判定直接用缓存，没有重拉。
        Assert.Equal(PageCalls(dataPages: 1), _server.TrackCalls);
    }

    [Fact]
    public async Task SetLiked_RequiresLogin()
    {
        Assert.Equal(LikedSongsOutcome.NotAuthenticated, await _service.SetLikedAsync(7, liked: true, Ct));
        Assert.Equal(0, _server.AddCalls);
    }

    [Fact]
    public async Task SetLiked_ReportsAMissingLikedPlaylist()
    {
        _session.Set(Uid, "test-token");
        _server.HasLikedPlaylist = false;

        Assert.Equal(LikedSongsOutcome.NoLikedPlaylist, await _service.SetLikedAsync(7, liked: true, Ct));
        Assert.Equal(0, _server.AddCalls);
    }

    // ── 批量喜欢 ────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>逐首写，一次一首。</b> 这条断言钉住的是 <c>PlaylistMusicWriter.Strategy</c> ——
    /// 服务端从未验证过「一次传多首」，所以默认逐首；哪天实测通过、改成分块，
    /// 这里会红，正好提醒改的人确认服务端确实吃多元素。
    /// </summary>
    [Fact]
    public async Task SetLikedMany_WritesOneRequestPerTrack()
    {
        _session.Set(Uid, "test-token");

        var result = await _service.SetLikedManyAsync([1, 2, 3], liked: true, cancellationToken: Ct);

        Assert.Equal(LikedSongsOutcome.Succeeded, result.Outcome);
        Assert.Equal(3, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.Equal(3, _server.AddCalls);
        Assert.Equal([1L, 2L, 3L], _server.LikedIds);
    }

    /// <summary>全选一个已收藏的歌单时，一首都不该发。</summary>
    [Fact]
    public async Task SetLikedMany_SkipsTracksThatAreAlreadyLiked()
    {
        _session.Set(Uid, "test-token");
        _server.LikedIds.AddRange([1, 2]);
        await _service.IsLikedAsync(1, Ct);

        var result = await _service.SetLikedManyAsync([1, 2, 3], liked: true, cancellationToken: Ct);

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(1, _server.AddCalls);
    }

    [Fact]
    public async Task SetLikedMany_DropsDuplicatesAndNonPositiveIds()
    {
        _session.Set(Uid, "test-token");

        var result = await _service.SetLikedManyAsync([5, 5, 0, -3, 6], liked: true, cancellationToken: Ct);

        Assert.Equal(2, result.Succeeded);
        Assert.Equal(2, _server.AddCalls);
        Assert.Equal([5L, 6L], _server.LikedIds);
    }

    [Fact]
    public async Task SetLikedMany_WithNothingToDo_SucceedsWithoutRequests()
    {
        _session.Set(Uid, "test-token");

        var result = await _service.SetLikedManyAsync([], liked: true, cancellationToken: Ct);

        Assert.Equal(LikedSongsOutcome.Succeeded, result.Outcome);
        Assert.Equal(0, _server.AddCalls);
    }

    /// <summary>一首写不了不该让剩下的白做；失败数如实回报。</summary>
    [Fact]
    public async Task SetLikedMany_KeepsGoingAfterASingleFailure()
    {
        _session.Set(Uid, "test-token");
        _server.FailingIds.Add(2);

        var result = await _service.SetLikedManyAsync([1, 2, 3], liked: true, cancellationToken: Ct);

        Assert.Equal(2, result.Succeeded);
        Assert.Equal(1, result.Failed);
        // 三首都发了 —— 第二首失败没有把第三首吞掉。
        Assert.Equal(3, _server.AddCalls);
        Assert.Equal([1L, 3L], _server.LikedIds);
    }

    /// <summary>
    /// 有失败就不动本地集合、只标脏：下一次判定必须重拉，否则半对半错的缓存会一直骗人。
    /// </summary>
    [Fact]
    public async Task SetLikedMany_LeavesTheLocalSetAloneWhenSomethingFailed()
    {
        _session.Set(Uid, "test-token");
        await _service.IsLikedAsync(1, Ct);
        _server.FailingIds.Add(2);

        await _service.SetLikedManyAsync([1, 2], liked: true, cancellationToken: Ct);

        Assert.True(await _service.IsLikedAsync(1, Ct));
        // 1 次是首同步、1 次是这次的重拉。
        Assert.Equal(2, _server.FondCalls);
    }

    [Fact]
    public async Task SetLikedMany_ReportsProgressAsItGoes()
    {
        _session.Set(Uid, "test-token");
        var progress = new RecordingProgress<BatchProgress>();

        await _service.SetLikedManyAsync([1, 2, 3], liked: true, progress: progress, cancellationToken: Ct);

        Assert.Equal(3, progress.Reports.Count);
        Assert.Equal(new BatchProgress(3, 3), progress.Reports[^1]);
    }

    [Fact]
    public async Task SetLikedMany_RequiresLogin()
    {
        var result = await _service.SetLikedManyAsync([1, 2], liked: true, cancellationToken: Ct);

        Assert.Equal(LikedSongsOutcome.NotAuthenticated, result.Outcome);
        Assert.Equal(0, _server.AddCalls);
    }

    [Fact]
    public async Task SetLikedMany_ReportsAMissingLikedPlaylist()
    {
        _session.Set(Uid, "test-token");
        _server.HasLikedPlaylist = false;

        var result = await _service.SetLikedManyAsync([1, 2], liked: true, cancellationToken: Ct);

        Assert.Equal(LikedSongsOutcome.NoLikedPlaylist, result.Outcome);
        Assert.Equal(0, _server.AddCalls);
    }

    /// <summary>取消方向同样跳过「已经是目标状态」的：本来就没喜欢的歌不该白发请求。</summary>
    [Fact]
    public async Task SetLikedMany_UnlikeSkipsTracksThatWereNotLiked()
    {
        _session.Set(Uid, "test-token");
        _server.LikedIds.AddRange([1, 2]);
        await _service.IsLikedAsync(1, Ct);

        // 3 与 4 本来就没喜欢，只有 1、2 需要真的移除。
        var result = await _service.SetLikedManyAsync([1, 2, 3, 4], liked: false, cancellationToken: Ct);

        Assert.Equal(LikedSongsOutcome.Succeeded, result.Outcome);
        Assert.Equal(2, result.Succeeded);
        Assert.Equal(2, _server.RemoveCalls);
        Assert.Empty(_server.LikedIds);
    }

    [Fact]
    public async Task SetLikedMany_CanUnlike()
    {
        _session.Set(Uid, "test-token");
        _server.LikedIds.AddRange([1, 2, 3]);
        await _service.IsLikedAsync(1, Ct);

        var result = await _service.SetLikedManyAsync([1, 2], liked: false, cancellationToken: Ct);

        Assert.Equal(LikedSongsOutcome.Succeeded, result.Outcome);
        Assert.Equal(2, _server.RemoveCalls);
        Assert.Equal([3L], _server.LikedIds);
    }

    // ── 会话变更 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task IsLiked_RefetchesAfterTheSessionChanges()
    {
        _session.Set(Uid, "test-token");
        _server.LikedIds.Add(1);
        Assert.True(await _service.IsLikedAsync(1, Ct));

        // 换账号：旧账号的集合不能沿用。
        _session.Set("99999", "other-token");
        _server.LikedIds.Clear();
        _server.LikedIds.Add(2);

        Assert.False(await _service.IsLikedAsync(1, Ct));
        Assert.True(await _service.IsLikedAsync(2, Ct));
        Assert.Equal(2, _server.FondCalls);
    }

    [Fact]
    public async Task IsLiked_StopsJudgingAfterLogout()
    {
        _session.Set(Uid, "test-token");
        _server.LikedIds.Add(1);
        Assert.True(await _service.IsLikedAsync(1, Ct));

        _session.Clear();

        Assert.Null(await _service.IsLikedAsync(1, Ct));
    }
}
