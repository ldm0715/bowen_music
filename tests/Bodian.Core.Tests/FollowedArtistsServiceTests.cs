using Bodian.Core.Api;
using Bodian.Core.Models;
using Bodian.Core.Services.Abstractions;
using Bodian.Core.Services.Implementations;
using Bodian.Core.Tests.Support;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// <see cref="FollowedArtistsService"/> 的拉取时机与失效规则。零真实网络。
/// </summary>
/// <remarks>
/// 守的是「判定时拉一次、写后标脏推迟重拉、换号整体失效」这三条 ——
/// 与 <see cref="LikedSongsService"/> 同一套模式，见 <c>docs/like-share.md</c>。
/// </remarks>
public sealed class FollowedArtistsServiceTests
{
    private const string Uid = "36828743";
    private const long FollowedId = 1306;
    private const long OtherId = 1524176;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Artist ArtistWith(long id) => new() { Id = id, Name = $"歌手 {id}" };

    private sealed class Server
    {
        public List<long> FollowedIds { get; } = [];

        public Exception? WriteError { get; set; }

        public int ListCalls { get; private set; }

        public int WriteCalls { get; private set; }

        public PlaybackApiStub BuildStub()
        {
            var stub = new PlaybackApiStub();
            stub.GetFollowedArtists = _ =>
            {
                ListCalls++;
                return Task.FromResult<IReadOnlyList<Artist>>([.. FollowedIds.Select(ArtistWith)]);
            };
            stub.SetArtistFollowed = (id, followed, _) =>
            {
                WriteCalls++;
                if (WriteError is { } error) return Task.FromException(error);
                if (followed)
                {
                    if (!FollowedIds.Contains(id)) FollowedIds.Add(id);
                }
                else
                {
                    FollowedIds.Remove(id);
                }

                return Task.CompletedTask;
            };
            return stub;
        }
    }

    private readonly Server _server = new();
    private readonly BodianSession _session = BodianSession.CreateAnonymous();
    private readonly FollowedArtistsService _service;

    public FollowedArtistsServiceTests()
    {
        _server.FollowedIds.Add(FollowedId);
        _service = new FollowedArtistsService(_server.BuildStub(), _session);
    }

    [Fact]
    public async Task IsFollowed_ReportsMembership()
    {
        _session.Set(Uid, "token");

        Assert.True(await _service.IsFollowedAsync(FollowedId, Ct));
        Assert.False(await _service.IsFollowedAsync(OtherId, Ct));
    }

    /// <summary>判定时拉一次，之后走内存；两次判定只发一个请求。</summary>
    [Fact]
    public async Task IsFollowed_SyncsOncePerRevision()
    {
        _session.Set(Uid, "token");

        await _service.IsFollowedAsync(FollowedId, Ct);
        await _service.IsFollowedAsync(OtherId, Ct);
        await _service.IsFollowedAsync(FollowedId, Ct);

        Assert.Equal(1, _server.ListCalls);
    }

    /// <summary>未登录时返回 <c>null</c>（无法判定），且不发请求。</summary>
    [Fact]
    public async Task IsFollowed_NullWhenAnonymous()
    {
        Assert.Null(await _service.IsFollowedAsync(FollowedId, Ct));
        Assert.Equal(0, _server.ListCalls);
    }

    /// <summary>写成功后本地立即生效，**点击当下不重拉**。</summary>
    [Fact]
    public async Task SetFollowed_UpdatesLocallyWithoutImmediateRefetch()
    {
        _session.Set(Uid, "token");
        await _service.IsFollowedAsync(FollowedId, Ct);

        var outcome = await _service.SetFollowedAsync(OtherId, followed: true, Ct);

        Assert.Equal(FollowedArtistOutcome.Succeeded, outcome);
        Assert.Equal(1, _server.ListCalls);
        Assert.True(await _service.IsFollowedAsync(OtherId, Ct));

        // 上一次判定触发了重拉（缓存被标脏），所以列表调用变成 2 次。
        Assert.Equal(2, _server.ListCalls);
    }

    [Fact]
    public async Task SetFollowed_UnfollowRemovesLocally()
    {
        _session.Set(Uid, "token");
        await _service.IsFollowedAsync(FollowedId, Ct);

        await _service.SetFollowedAsync(FollowedId, followed: false, Ct);

        Assert.False(await _service.IsFollowedAsync(FollowedId, Ct));
        Assert.DoesNotContain(FollowedId, _server.FollowedIds);
    }

    [Fact]
    public async Task SetFollowed_NotAuthenticated()
    {
        Assert.Equal(FollowedArtistOutcome.NotAuthenticated,
            await _service.SetFollowedAsync(FollowedId, followed: true, Ct));
        Assert.Equal(0, _server.WriteCalls);
    }

    [Fact]
    public async Task SetFollowed_FailsWithoutTouchingCache()
    {
        _session.Set(Uid, "token");
        await _service.IsFollowedAsync(FollowedId, Ct);
        _server.WriteError = new InvalidOperationException("boom");

        var outcome = await _service.SetFollowedAsync(OtherId, followed: true, Ct);

        Assert.Equal(FollowedArtistOutcome.Failed, outcome);
        Assert.False(await _service.IsFollowedAsync(OtherId, Ct));
    }

    /// <summary>换号（<c>Revision</c> 变化）后整体失效，下次判定重拉。</summary>
    [Fact]
    public async Task RevisionChange_ForcesResync()
    {
        _session.Set(Uid, "token");
        await _service.IsFollowedAsync(FollowedId, Ct);
        Assert.Equal(1, _server.ListCalls);

        _session.Set("99999", "other-token");
        await _service.IsFollowedAsync(FollowedId, Ct);

        Assert.Equal(2, _server.ListCalls);
    }
}
