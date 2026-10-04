using Microsoft.Extensions.Logging;

namespace Bodian.Core.Services;

/// <summary>
/// 批量增删曲目时，一次请求发送多少首。
/// </summary>
public enum BatchWriteStrategy
{
    /// <summary>每次请求一首。</summary>
    SequentialPerItem,

    /// <summary>每次请求最多 100 首。</summary>
    Chunked,
}

/// <summary>批量写入的进度。</summary>
/// <param name="Done">已经处理完的曲目数。</param>
/// <param name="Total">本次一共要处理多少首。</param>
public readonly record struct BatchProgress(int Done, int Total);

/// <summary>批量写入的结果。</summary>
/// <param name="Succeeded">成功的曲目数。</param>
/// <param name="Failed">失败的曲目数。</param>
/// <param name="Canceled">是否被取消。</param>
public readonly record struct BatchWriteResult(int Succeeded, int Failed, bool Canceled);

/// <summary>
/// 往歌单里批量增删曲目。
/// </summary>
/// <remarks>
/// <para>
/// 「喜欢」与「加入歌单」两条路都落到 <c>service/playlist/music</c> 上，只是目标歌单不同，
/// 所以节奏控制、进度上报、失败统计收在这里一份。
/// </para>
/// <para>
/// <b>默认逐首，不是分块。</b> 接口签名收的是 id 列表（<c>BodianApi</c> 里限 1–100 首），
/// 客户端也确实会把整个列表拼进一个请求 —— 但全仓所有真实调用点传的都是<b>单元素</b>列表，
/// 官方移动端没有多选，多元素那条路从来没有对真实服务端发过。
/// 分块若服务端只认第一个 id，症状是<b>静默少加歌</b>，用户看不出来；逐首最多是慢，还能统计失败。
/// </para>
/// <para>
/// <b>实测确认服务端接受多元素之后再改 <see cref="Strategy"/>。</b> 验证方法：拿三个 id 调一次
/// <c>AddPlaylistMusicAsync</c>，确认三首都进了歌单，而不是只进第一首。
/// </para>
/// </remarks>
public static class PlaylistMusicWriter
{
    /// <summary>接口侧的单次上限，见 <c>BodianApi.WritePlaylistMusicAsync</c>。</summary>
    private const int MaxPerRequest = 100;

    /// <summary>
    /// 当前使用的写入策略。
    /// </summary>
    /// <remarks>
    /// 改这个常量会同时影响「批量喜欢」与「批量加入歌单」两条路 —— 它们共用本类的
    /// <see cref="WriteAsync"/>，这是有意的：切换时机应当是「服务端验证通过」这一件事。
    /// </remarks>
    public const BatchWriteStrategy Strategy = BatchWriteStrategy.SequentialPerItem;

    /// <summary>
    /// 按 <see cref="Strategy"/> 把 <paramref name="musicIds"/> 写完。
    /// </summary>
    /// <param name="musicIds">已经去过重、剔除非正 id 的目标曲目。</param>
    /// <param name="writeOnce">发一次请求。列表长度由本类按策略决定，实现方不要自己再切。</param>
    /// <param name="logger">失败时记一条，可空。</param>
    /// <param name="progress">每处理完一批报一次，可空。</param>
    /// <remarks>
    /// <para>
    /// <b>单首失败继续往下做</b>：一首不可播不该让剩下的 99 首白做。失败数如实回报，
    /// 由调用方决定要不要重拉本地状态。
    /// </para>
    /// <para>
    /// <b><see cref="InvalidOperationException"/> 例外 —— 它一定往上抛。</b>
    /// 那个异常表示「未登录」或「登录状态已改变」，后面每一首都会同样失败，
    /// 继续发只是白刷一串请求。
    /// </para>
    /// </remarks>
    public static async Task<BatchWriteResult> WriteAsync(
        IReadOnlyList<long> musicIds,
        Func<IReadOnlyList<long>, CancellationToken, Task> writeOnce,
        ILogger? logger = null,
        IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(musicIds);
        ArgumentNullException.ThrowIfNull(writeOnce);

        var total = musicIds.Count;

        if (total == 0)
        {
            return new BatchWriteResult(0, 0, false);
        }

        var chunkSize = Strategy == BatchWriteStrategy.Chunked ? MaxPerRequest : 1;
        var succeeded = 0;
        var failed = 0;

        for (var offset = 0; offset < total; offset += chunkSize)
        {
            // 取消不抛，返回「做到哪儿了」—— 调用方要据此告诉用户「已加入 N 首（已取消）」，
            // 抛出去的话这个数字就没了。
            if (cancellationToken.IsCancellationRequested)
            {
                return new BatchWriteResult(succeeded, failed, true);
            }

            var length = Math.Min(chunkSize, total - offset);
            var chunk = new long[length];
            for (var i = 0; i < length; i++)
            {
                chunk[i] = musicIds[offset + i];
            }

            try
            {
                await writeOnce(chunk, cancellationToken).ConfigureAwait(true);
                succeeded += length;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new BatchWriteResult(succeeded, failed, true);
            }
            catch (InvalidOperationException)
            {
                // 未登录 / 会话中途变了。不吞 —— 调用方要把它转成「登录后可以…」的提示。
                throw;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "批量写入失败：曲目 {MusicIds}", string.Join(',', chunk));
                failed += length;
            }

            progress?.Report(new BatchProgress(offset + length, total));
        }

        return new BatchWriteResult(succeeded, failed, false);
    }
}
