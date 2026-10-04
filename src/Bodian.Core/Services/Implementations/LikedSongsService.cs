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

    /// <inheritdoc />
    /// <remarks>
    /// <b>逐首写，不是一次发一批。</b> 理由见 <see cref="PlaylistMusicWriter"/> ——
    /// 接口收 id 列表，但多元素那条路从没对真实服务端发过。
    /// </remarks>
    public async Task<LikedSongsBatchOutcome> SetLikedManyAsync(
        IReadOnlyList<long> musicIds, bool liked = true,
        IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(musicIds);

        // 非正 id 发过去必然失败，重复的会让同一首发两次。
        var ids = new List<long>();
        var unique = new HashSet<long>();

        foreach (var id in musicIds)
        {
            if (id > 0 && unique.Add(id))
            {
                ids.Add(id);
            }
        }

        if (ids.Count == 0)
        {
            return new LikedSongsBatchOutcome(LikedSongsOutcome.Succeeded, 0, 0, false);
        }

        if (!_session.IsAuthenticated)
        {
            return new LikedSongsBatchOutcome(LikedSongsOutcome.NotAuthenticated, 0, 0, false);
        }

        // 与单首写共用同一套在飞去重：已经在飞的那几首跳过，不让它们被写两次。
        var claimed = new List<long>();

        foreach (var id in ids)
        {
            if (_pending.Add(id))
            {
                claimed.Add(id);
            }
        }

        if (claimed.Count == 0)
        {
            return new LikedSongsBatchOutcome(LikedSongsOutcome.AlreadyPending, 0, 0, false);
        }

        var revision = _session.Revision;

        try
        {
            await EnsureSyncedAsync(cancellationToken).ConfigureAwait(true);

            if (_playlistId is not { } playlistId)
            {
                return new LikedSongsBatchOutcome(LikedSongsOutcome.NoLikedPlaylist, 0, 0, false);
            }

            // 已经是目标状态的不用发请求：喜欢方向跳过已喜欢的（全选一个已收藏的歌单能省掉整批），
            // 取消方向跳过本来就没喜欢的（列表里混着没喜欢的歌时同样省一批）。
            // 能走到这里就说明 _playlistId 有值，而它与 _liked 是一起设的，所以下面不为 null。
            var targets = new List<long>();

            foreach (var id in claimed)
            {
                if (_liked is { } current && (liked ? current.Contains(id) : !current.Contains(id)))
                {
                    continue;
                }

                targets.Add(id);
            }

            if (targets.Count == 0)
            {
                // 用户要的结果本来就已经成立，按成功算。
                return new LikedSongsBatchOutcome(LikedSongsOutcome.Succeeded, claimed.Count, 0, false);
            }

            var result = await PlaylistMusicWriter.WriteAsync(
                targets,
                (chunk, token) => liked
                    ? _api.AddPlaylistMusicAsync(playlistId, chunk, token)
                    : _api.RemovePlaylistMusicAsync(playlistId, chunk, token),
                _logger,
                progress,
                cancellationToken).ConfigureAwait(true);

            // 取消、账号中途换了、有失败 —— 三种都不动本地集合，只标脏，
            // 交给下一次判定整体重拉。半对半错的集合比稍微滞后的集合危险得多。
            if (result.Canceled || result.Failed > 0)
            {
                _dirty = true;
                _logger.LogWarning("批量{Operation}未全部成功：成功 {Succeeded} 首，失败 {Failed} 首，取消 {Canceled}",
                    liked ? "喜欢" : "取消喜欢", result.Succeeded, result.Failed, result.Canceled);
                return new LikedSongsBatchOutcome(
                    LikedSongsOutcome.Succeeded, result.Succeeded, result.Failed, result.Canceled);
            }

            if (revision != _session.Revision)
            {
                _dirty = true;
                return new LikedSongsBatchOutcome(LikedSongsOutcome.Failed, result.Succeeded, 0, false);
            }

            var set = _liked ??= [];

            foreach (var id in targets)
            {
                if (liked)
                {
                    set.Add(id);
                }
                else
                {
                    set.Remove(id);
                }
            }

            // 与单首写一致：写成功只标脏，不立刻重拉。
            _dirty = true;
            _logger.LogInformation("批量{Operation}成功：{Count} 首（另跳过 {Skipped} 首已在目标状态）",
                liked ? "喜欢" : "取消喜欢", targets.Count, claimed.Count - targets.Count);
            return new LikedSongsBatchOutcome(LikedSongsOutcome.Succeeded, targets.Count, 0, false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _dirty = true;
            return new LikedSongsBatchOutcome(LikedSongsOutcome.Failed, 0, 0, true);
        }
        catch (Exception ex)
        {
            _dirty = true;
            _logger.LogWarning(ex, "批量{Operation}失败", liked ? "喜欢" : "取消喜欢");
            return new LikedSongsBatchOutcome(LikedSongsOutcome.Failed, 0, 0, false);
        }
        finally
        {
            foreach (var id in claimed)
            {
                _pending.Remove(id);
            }
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
