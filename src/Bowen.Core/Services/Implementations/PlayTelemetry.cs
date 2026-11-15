using Bowen.Core.Api;
using Bowen.Core.Models;
using Bowen.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bowen.Core.Services.Implementations;

/// <inheritdoc cref="IPlayTelemetry" />
/// <remarks>
/// <para>
/// 只负责选 <c>operate</c>、换算秒数、发出请求、吞掉失败。没有本地队列，也没有重试 ——
/// 统计丢一条不影响播放，重试反而会在网络不佳时堆积请求。
/// </para>
/// <para>
/// 不设最短播放秒数或小时限额：官方只按已播比例决定 <c>operate</c>，照抄即可。
/// </para>
/// </remarks>
public sealed class PlayTelemetry : IPlayTelemetry
{
    /// <summary>单次上报的超时。到点就放弃，不重试。</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    private readonly IBodianApi _api;
    private readonly BodianSession _session;
    private readonly ILogger<PlayTelemetry> _logger;

    public PlayTelemetry(
        IBodianApi api,
        BodianSession session,
        ILogger<PlayTelemetry>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(session);

        _api = api;
        _session = session;
        _logger = logger ?? NullLogger<PlayTelemetry>.Instance;
    }

    public void ReportCompleted(Track track)
    {
        if (!TryGetSeconds(track, out var duration))
        {
            return;
        }

        FireAndForget(track.Id, duration, duration, Endpoints.PlayOpComplete);
    }

    public void ReportSwitched(Track track, TimeSpan position)
    {
        if (!TryGetSeconds(track, out var duration))
        {
            return;
        }

        var played = Math.Clamp((int)Math.Round(position.TotalSeconds), 0, duration);
        var ratio = played / (double)duration;

        // 与官方一致：不足 30% 是切歌，30%-90% 是播放中，超过 90% 按完成计。
        var operate = ratio < 0.3 ? Endpoints.PlayOpSwitch
            : ratio <= 0.9 ? Endpoints.PlayOpNone
            : Endpoints.PlayOpComplete;

        FireAndForget(track.Id, duration, played, operate);
    }

    private bool TryGetSeconds(Track track, out int durationSeconds)
    {
        durationSeconds = (int)Math.Round(track.Duration.TotalSeconds);

        return track.Id > 0
            && durationSeconds > 0
            && _session.IsAuthenticated;
    }

    /// <summary>不 await：统计上报不能挡住切歌或播放结束。异常在 <see cref="RunAsync"/> 内部全部吞掉。</summary>
    private void FireAndForget(long musicId, int durationSeconds, int playedSeconds, int operate)
        => _ = RunAsync(musicId, durationSeconds, playedSeconds, operate);

    private async Task RunAsync(long musicId, int durationSeconds, int playedSeconds, int operate)
    {
        try
        {
            using var timeout = new CancellationTokenSource(RequestTimeout);
            await _api.AddPlayRecordAsync(musicId, durationSeconds, playedSeconds, operate, timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("播放统计上报超时：musicId {MusicId}", musicId);
        }
        catch (Exception ex)
        {
            // 未登录、网络错误、服务端拒绝都走这里。只记日志，不向用户提示。
            _logger.LogWarning(ex, "播放统计上报失败：musicId {MusicId}，operate {Operate}", musicId, operate);
        }
    }
}
