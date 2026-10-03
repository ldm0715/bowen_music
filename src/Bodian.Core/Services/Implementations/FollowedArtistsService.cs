using Bodian.Core.Api;
using Bodian.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.Core.Services.Implementations;

/// <inheritdoc cref="IFollowedArtistsService" />
/// <remarks>
/// 判定依据是 <c>service/collect/7/list</c> 返回的 <c>artistList</c> ——
/// 一次全量取回（官方客户端固定 <c>rn=400</c>），不像「我喜欢的」那样要翻页。
/// 见 <c>reverse/findings/13-collect-playlist-follow-artist.md</c> §4。
/// </remarks>
public sealed class FollowedArtistsService : IFollowedArtistsService
{
    private readonly IBodianApi _api;
    private readonly BodianSession _session;
    private readonly ILogger<FollowedArtistsService> _logger;

    /// <summary>串行化「拉取」与「写」，避免两处同时改集合。</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>同一个歌手的写操作去重，照 <see cref="LikedSongsService"/> 的写法。</summary>
    private readonly HashSet<long> _pending = [];

    private HashSet<long>? _followed;
    private int _syncedRevision = -1;
    private bool _dirty;

    public FollowedArtistsService(IBodianApi api, BodianSession session,
        ILogger<FollowedArtistsService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(session);
        _api = api;
        _session = session;
        _logger = logger ?? NullLogger<FollowedArtistsService>.Instance;
    }

    public async Task<bool?> IsFollowedAsync(long artistId, CancellationToken cancellationToken = default)
    {
        if (artistId <= 0) return null;

        try
        {
            await EnsureSyncedAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 读取失败按「无法判定」处理，且不写同步标记 —— 下一次判定会重试。
            _logger.LogDebug(ex, "关注状态读取失败：歌手 {ArtistId}", artistId);
            return null;
        }

        return _session.IsAuthenticated ? _followed?.Contains(artistId) : null;
    }

    public async Task<FollowedArtistOutcome> SetFollowedAsync(long artistId, bool followed,
        CancellationToken cancellationToken = default)
    {
        if (artistId <= 0) return FollowedArtistOutcome.Failed;
        if (!_session.IsAuthenticated) return FollowedArtistOutcome.NotAuthenticated;
        if (!_pending.Add(artistId)) return FollowedArtistOutcome.AlreadyPending;

        var revision = _session.Revision;
        try
        {
            // 先确保集合可用：没同步过就在写之前拉一次，否则这之后本地增减的是**残缺**的集合
            // （只含本次写进去的那个 id），别的歌手会被误判成未关注。
            await EnsureSyncedAsync(cancellationToken).ConfigureAwait(true);
            await _api.SetArtistFollowedAsync(artistId, followed, cancellationToken).ConfigureAwait(true);

            if (revision != _session.Revision)
            {
                // 期间账号换了，这一次的结果不能写进集合 —— 下次判定会整体重拉。
                return FollowedArtistOutcome.Failed;
            }

            var set = _followed ??= [];
            if (followed) set.Add(artistId);
            else set.Remove(artistId);
            // 写成功只标脏，不立刻重拉：结果已知，重拉推迟到下一次判定。
            _dirty = true;
            _logger.LogInformation("歌手{Operation}成功：歌手 {ArtistId}", followed ? "关注" : "取消关注", artistId);
            return FollowedArtistOutcome.Succeeded;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return FollowedArtistOutcome.Failed;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "歌手{Operation}失败：歌手 {ArtistId}", followed ? "关注" : "取消关注", artistId);
            return FollowedArtistOutcome.Failed;
        }
        finally
        {
            _pending.Remove(artistId);
        }
    }

    /// <summary>
    /// 需要判定时确保集合可用：会话没同步过、缓存被标脏、或账号换了都重拉一次。
    /// 同步成功后才写 <see cref="_syncedRevision"/>，所以失败会在下一次调用重试。
    /// </summary>
    private async Task EnsureSyncedAsync(CancellationToken cancellationToken)
    {
        if (IsSynced()) return;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            if (IsSynced()) return;

            var revision = _session.Revision;
            if (!_session.IsAuthenticated)
            {
                Reset(revision);
                return;
            }

            var artists = await _api.GetFollowedArtistsAsync(cancellationToken).ConfigureAwait(true);
            var set = new HashSet<long>(artists.Select(a => a.Id));

            _followed = set;
            _syncedRevision = revision;
            _dirty = false;
            _logger.LogInformation("已同步关注歌手：{Count} 位", set.Count);
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool IsSynced() => _syncedRevision == _session.Revision && !_dirty && _syncedRevision >= 0;

    private void Reset(int revision)
    {
        _followed = null;
        _syncedRevision = revision;
        _dirty = false;
    }
}
