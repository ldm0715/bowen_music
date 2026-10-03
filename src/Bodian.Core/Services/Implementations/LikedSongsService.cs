using Bodian.Core.Api;
using Bodian.Core.Api.Paging;
using Bodian.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.Core.Services.Implementations;

/// <inheritdoc cref="ILikedSongsService" />
/// <remarks>
/// <para>
/// 判定依据是「我喜欢的」歌单的曲目集合。取集合分两步：<c>service/playlist/fond</c> 拿歌单 id，
/// 再按 <c>source=5</c> 翻页取曲目（文档 2.3 / 2.4）。
/// </para>
/// <para>
/// <b>扫描有上限</b>：<see cref="MaxScannedSongs"/> 之外的曲目不再取，只记一条警告。
/// 这个上限是防「服务端一直回非空页」把循环拖死，不是接口约束。
/// </para>
/// </remarks>
public sealed class LikedSongsService : ILikedSongsService
{
    /// <summary>账号歌单（自建与「我喜欢」）在曲目接口里的 <c>source</c>。</summary>
    private const int AccountPlaylistSource = 5;

    /// <summary>每次取回的条数上限是接口侧的 100。</summary>
    private const int PageSize = 100;

    /// <summary>最多扫多少首；超出只警告，不再继续翻页。</summary>
    public const int MaxScannedSongs = 10_000;

    private readonly IBodianApi _api;
    private readonly BodianSession _session;
    private readonly ILogger<LikedSongsService> _logger;

    /// <summary>串行化「拉取」与「写」，避免两处同时改集合。</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>同一首歌的写操作去重，照评论点赞的写法。</summary>
    private readonly HashSet<long> _pending = [];

    private HashSet<long>? _liked;
    private long? _playlistId;
    private int _syncedRevision = -1;
    private bool _dirty;

    public LikedSongsService(IBodianApi api, BodianSession session, ILogger<LikedSongsService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(session);
        _api = api;
        _session = session;
        _logger = logger ?? NullLogger<LikedSongsService>.Instance;
    }

    public async Task<bool?> IsLikedAsync(long musicId, CancellationToken cancellationToken = default)
    {
        if (musicId <= 0) return null;
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
            _logger.LogDebug(ex, "喜欢状态读取失败：歌曲 {MusicId}", musicId);
            return null;
        }

        return _liked?.Contains(musicId);
    }

    public async Task<LikedSongsOutcome> SetLikedAsync(long musicId, bool liked,
        CancellationToken cancellationToken = default)
    {
        if (musicId <= 0) return LikedSongsOutcome.Failed;
        if (!_session.IsAuthenticated) return LikedSongsOutcome.NotAuthenticated;
        if (!_pending.Add(musicId)) return LikedSongsOutcome.AlreadyPending;

        var revision = _session.Revision;
        try
        {
            await EnsureSyncedAsync(cancellationToken).ConfigureAwait(true);
            if (_playlistId is not { } playlistId) return LikedSongsOutcome.NoLikedPlaylist;

            if (liked)
            {
                await _api.AddPlaylistMusicAsync(playlistId, [musicId], cancellationToken).ConfigureAwait(true);
            }
            else
            {
                await _api.RemovePlaylistMusicAsync(playlistId, [musicId], cancellationToken).ConfigureAwait(true);
            }

            if (revision != _session.Revision)
            {
                // 期间账号换了，这一次的结果不能写进集合 —— 下次判定会整体重拉。
                return LikedSongsOutcome.Failed;
            }

            var set = _liked ??= [];
            if (liked) set.Add(musicId);
            else set.Remove(musicId);
            // 写成功只标脏，不立刻重拉：结果已知，重拉推迟到下一次判定。
            _dirty = true;
            _logger.LogInformation("歌曲{Operation}成功：歌曲 {MusicId}，歌单 {PlaylistId}",
                liked ? "喜欢" : "取消喜欢", musicId, playlistId);
            return LikedSongsOutcome.Succeeded;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return LikedSongsOutcome.Failed;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "歌曲{Operation}失败：歌曲 {MusicId}", liked ? "喜欢" : "取消喜欢", musicId);
            return LikedSongsOutcome.Failed;
        }
        finally
        {
            _pending.Remove(musicId);
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

            var playlist = await _api.GetLikedPlaylistAsync(cancellationToken).ConfigureAwait(true);
            if (playlist is not { Id: > 0 } liked)
            {
                // 账号没有红心歌单：这是正常状态，按「无法判定」缓存下来，不反复请求。
                _logger.LogInformation("账号没有「我喜欢的」歌单，无从判定喜欢状态。");
                _liked = null;
                _playlistId = null;
                _syncedRevision = revision;
                _dirty = false;
                return;
            }

            var set = await FetchLikedIdsAsync(liked.Id, cancellationToken).ConfigureAwait(true);
            _liked = set;
            _playlistId = liked.Id;
            _syncedRevision = revision;
            _dirty = false;
            _logger.LogInformation("已同步「我喜欢的」：歌单 {PlaylistId}，{Count} 首", liked.Id, set.Count);
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool IsSynced() => _syncedRevision == _session.Revision && !_dirty && _syncedRevision >= 0;

    private void Reset(int revision)
    {
        _liked = null;
        _playlistId = null;
        _syncedRevision = revision;
        _dirty = false;
    }

    /// <summary>
    /// 翻页取完「我喜欢的」的曲目 id。翻页推进交给 <see cref="PagedCursor"/>。
    /// </summary>
    /// <remarks>
    /// <b>每次同步都会多发一个请求</b>：这个游标只能用「收到 0 条」判定翻到底，
    /// 不能用短页当判据（服务端会省略不可用曲目，实测标称 121 首的列表首页只回 99 首）。
    /// 一次同步 = 数据页数 + 1。
    /// </remarks>
    private async Task<HashSet<long>> FetchLikedIdsAsync(long playlistId, CancellationToken cancellationToken)
    {
        var ids = new HashSet<long>();
        var cursor = new PagedCursor(PagingConvention.OneBased, PageSize);
        while (!cursor.Exhausted)
        {
            var page = await _api.GetPlaylistTracksAsync(playlistId, AccountPlaylistSource, cursor, cancellationToken)
                .ConfigureAwait(true);
            foreach (var track in page.Items) ids.Add(track.Id);

            if (ids.Count >= MaxScannedSongs && !cursor.Exhausted)
            {
                _logger.LogWarning("「我喜欢的」已超过 {Limit} 首，本次只同步了前 {Count} 首；" +
                    "更靠后的曲目可能被误判为未喜欢。", MaxScannedSongs, ids.Count);
                break;
            }
        }

        return ids;
    }
}
