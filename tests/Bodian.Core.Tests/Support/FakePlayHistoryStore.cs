using Bodian.Core.Models;
using Bodian.Core.Services.Abstractions;

namespace Bodian.Core.Tests.Support;

/// <summary>只数条数、不落盘的播放历史。</summary>
internal sealed class FakePlayHistoryStore : IPlayHistoryStore
{
    public int Count { get; private set; }

    public Task RecordAsync(Track track, CancellationToken cancellationToken = default)
    {
        Count++;

        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<IReadOnlyList<PlayHistoryEntry>> GetRecentAsync(int limit, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<PlayHistoryEntry>>([]);
}
