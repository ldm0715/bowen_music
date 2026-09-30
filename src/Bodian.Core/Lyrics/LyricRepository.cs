using Bodian.Core.Api;
using Bodian.Core.Models;
using Bodian.Core.Models.Lyrics;

namespace Bodian.Core.Lyrics;

/// <summary>
/// 歌词的内存缓存。界面通过它取词，不直接找 <see cref="IBodianApi"/>。
/// </summary>
/// <remarks>
/// <para>
/// 存在的理由只有一条：**别把别人的服务当压测**。同一首歌反复播、歌词页来回切，
/// 都不该重发请求。空结果同样缓存 —— 「这首歌没有歌词」也是稳定结论，反复问它没有意义。
/// </para>
/// <para>
/// 只在内存里，不落盘：歌词很小，但要落盘就得处理失效与清理，当前不值得。
/// </para>
/// <para>
/// 并发取舍：查表加锁，**取词不加锁**，所以同一首歌被同时要两次时可能发两次请求。
/// 换来的是网络等待永远不进锁内 —— 这比省一次请求重要。
/// </para>
/// </remarks>
public sealed class LyricRepository
{
    /// <summary>默认缓存多少首。一首歌词几十 KB 量级，32 首的占用可以忽略。</summary>
    public const int DefaultCapacity = 32;

    private readonly IBodianApi _api;
    private readonly int _capacity;
    private readonly Lock _gate = new();
    private readonly Dictionary<long, LinkedListNode<Entry>> _index = [];
    private readonly LinkedList<Entry> _lru = new();

    public LyricRepository(IBodianApi api, int capacity = DefaultCapacity)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        _api = api;
        _capacity = capacity;
    }

    /// <summary>命中就返回缓存（空文档也算命中），否则取词并写进缓存。</summary>
    /// <remarks>网络与服务端异常照常向上抛，**不缓存失败**。</remarks>
    public async Task<LyricDocument> GetAsync(Track track, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);

        if (TryGet(track.Id, out var cached))
        {
            return cached;
        }

        var document = await _api.GetLyricsAsync(track, cancellationToken).ConfigureAwait(false);

        Store(track.Id, document);

        return document;
    }

    /// <summary>清空缓存。登出或换账号时用。</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _index.Clear();
            _lru.Clear();
        }
    }

    private bool TryGet(long musicId, out LyricDocument document)
    {
        lock (_gate)
        {
            if (_index.TryGetValue(musicId, out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                document = node.Value.Document;
                return true;
            }
        }

        document = LyricDocument.Empty;
        return false;
    }

    private void Store(long musicId, LyricDocument document)
    {
        lock (_gate)
        {
            if (_index.TryGetValue(musicId, out var existing))
            {
                _lru.Remove(existing);
                _index.Remove(musicId);
            }

            var node = _lru.AddFirst(new Entry(musicId, document));
            _index[musicId] = node;

            while (_index.Count > _capacity && _lru.Last is { } oldest)
            {
                _lru.RemoveLast();
                _index.Remove(oldest.Value.MusicId);
            }
        }
    }

    private sealed record Entry(long MusicId, LyricDocument Document);
}
