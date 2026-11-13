using Bowen.Core.Api;
using Bowen.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bowen.Core.Services.Implementations;

/// <inheritdoc cref="IPlayHistorySync" />
/// <remarks>
/// <para>
/// 只负责三件事：判断登录态、发出请求、吞掉失败。没有本地队列，也没有重试 ——
/// 历史同步丢一条不影响播放，重试反而会在网络不佳时堆积请求。
/// </para>
/// <para>
/// 单次请求套 5 秒超时，避免连接卡住时留下长时间悬挂的任务。
/// </para>
/// </remarks>
public sealed class PlayHistorySync : IPlayHistorySync
{
    /// <summary>单次上报的超时。到点就放弃，不重试。</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    private readonly IBodianApi _api;
    private readonly BodianSession _session;
    private readonly ILogger<PlayHistorySync> _logger;

    public PlayHistorySync(
        IBodianApi api,
        BodianSession session,
        ILogger<PlayHistorySync>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(session);

        _api = api;
        _session = session;
        _logger = logger ?? NullLogger<PlayHistorySync>.Instance;
    }

    public void NotifyPlayed(long musicId)
    {
        if (musicId <= 0 || !_session.IsAuthenticated)
        {
            return;
        }

        FireAndForget(
            token => _api.AddSongPlayHistoryAsync(musicId, token),
            $"歌曲 {musicId}");
    }

    public void NotifyCollectionPlayed(int historySourceType, long sourceId, int playlistType)
    {
        if (sourceId <= 0 || playlistType <= 0 || !_session.IsAuthenticated)
        {
            return;
        }

        FireAndForget(
            token => _api.AddCollectionPlayHistoryAsync(historySourceType, sourceId, playlistType, token),
            $"{historySourceType switch { 2 => "歌单", 3 => "专辑", _ => "集合" }} {sourceId}");
    }

    /// <summary>不 await：历史上报不能挡住起播。异常在 <see cref="RunAsync"/> 内部全部吞掉。</summary>
    private void FireAndForget(Func<CancellationToken, Task> send, string description)
        => _ = RunAsync(send, description);

    private async Task RunAsync(Func<CancellationToken, Task> send, string description)
    {
        try
        {
            using var timeout = new CancellationTokenSource(RequestTimeout);
            await send(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("播放历史上报超时：{Description}", description);
        }
        catch (Exception ex)
        {
            // 未登录、网络错误、服务端拒绝都走这里。只记日志，不向用户提示。
            _logger.LogDebug(ex, "播放历史上报失败：{Description}", description);
        }
    }
}
