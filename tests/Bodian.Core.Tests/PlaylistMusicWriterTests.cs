using Bodian.Core.Services;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 批量写入的节奏控制。
/// </summary>
/// <remarks>
/// 这里验的是<b>调用节奏与失败语义</b>，不是网络 —— 真正的写请求在
/// <see cref="LikedSongsServiceTests"/> 里用桩验过一遍了。
/// </remarks>
public sealed class PlaylistMusicWriterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>记下每次请求收到的那一批曲目。</summary>
    private sealed class Recorder
    {
        public List<long[]> Batches { get; } = [];

        public Func<long[], Exception?>? Fail { get; set; }

        public Task WriteAsync(IReadOnlyList<long> ids, CancellationToken cancellationToken)
        {
            var batch = ids.ToArray();
            Batches.Add(batch);

            return Fail?.Invoke(batch) is { } error
                ? Task.FromException(error)
                : Task.CompletedTask;
        }

        public IEnumerable<long> AllIds => Batches.SelectMany(batch => batch);
    }

    /// <summary>默认策略是逐首，所以一批就是一首。</summary>
    [Fact]
    public async Task WritesOneRequestPerTrackInOrder()
    {
        var recorder = new Recorder();

        var result = await PlaylistMusicWriter.WriteAsync([4, 5, 6], recorder.WriteAsync, cancellationToken: Ct);

        Assert.Equal(new BatchWriteResult(3, 0, false), result);
        Assert.Equal(3, recorder.Batches.Count);
        Assert.Equal([4L, 5L, 6L], recorder.AllIds);
    }

    [Fact]
    public async Task ReportsProgressAfterEachTrack()
    {
        var recorder = new Recorder();
        var progress = new List<BatchProgress>();

        await PlaylistMusicWriter.WriteAsync([1, 2], recorder.WriteAsync,
            progress: new Progress<BatchProgress>(progress.Add), cancellationToken: Ct);

        await Task.Yield();

        Assert.Equal([new BatchProgress(1, 2), new BatchProgress(2, 2)], progress);
    }

    /// <summary>一首写不了不该让剩下的白做。</summary>
    [Fact]
    public async Task KeepsGoingWhenOneTrackFails()
    {
        var recorder = new Recorder
        {
            Fail = batch => batch[0] == 2 ? new HttpRequestException("写不了") : null,
        };

        var result = await PlaylistMusicWriter.WriteAsync([1, 2, 3], recorder.WriteAsync, cancellationToken: Ct);

        Assert.Equal(new BatchWriteResult(2, 1, false), result);
        Assert.Equal([1L, 2L, 3L], recorder.AllIds);
    }

    /// <summary>
    /// 「未登录」与「登录状态已改变」都是 <see cref="InvalidOperationException"/>，
    /// 后面每一首都会同样失败，所以立刻往上抛，不再白刷请求。
    /// </summary>
    [Fact]
    public async Task StopsAtOnceWhenTheSessionIsRejected()
    {
        var recorder = new Recorder
        {
            Fail = _ => new InvalidOperationException("登录状态已改变，请重新检查歌单。"),
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => PlaylistMusicWriter.WriteAsync([1, 2, 3], recorder.WriteAsync, cancellationToken: Ct));

        Assert.Single(recorder.Batches);
    }

    [Fact]
    public async Task ReportsCancellationWithWhatWasAlreadyDone()
    {
        var recorder = new Recorder();
        using var cts = new CancellationTokenSource();

        var result = await PlaylistMusicWriter.WriteAsync([1, 2, 3], (ids, token) =>
        {
            var task = recorder.WriteAsync(ids, token);
            cts.Cancel();
            return task;
        }, cancellationToken: cts.Token);

        Assert.Equal(new BatchWriteResult(1, 0, true), result);
        Assert.Single(recorder.Batches);
    }

    [Fact]
    public async Task DoesNothingForAnEmptyList()
    {
        var recorder = new Recorder();

        var result = await PlaylistMusicWriter.WriteAsync([], recorder.WriteAsync, cancellationToken: Ct);

        Assert.Equal(new BatchWriteResult(0, 0, false), result);
        Assert.Empty(recorder.Batches);
    }
}
