using System.Text;
using Bowen.Core.Api;
using Bowen.Core.Models;
using Bowen.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bowen.Core.Services.Implementations;

/// <inheritdoc cref="IPlayTelemetry" />
/// <remarks>
/// <para>
/// 发的是官方移动端的 <c>log.kuwo.cn/music.yl</c> 日志，不是 <c>service/music/record</c>。
/// 2026-11-19 的对照实验确认：移动端格式的 <c>music.yl</c> 会被统计采纳，而桌面明文
/// <c>record</c> 不会。见 <c>docs/play-stats.md</c> §9.8。
/// </para>
/// <para>
/// 无签名、纯 HTTP、身份在 body 的 <c>UI</c> 字段。只负责拼日志、编码、发出、吞掉失败，
/// 不重试、不阻塞播放。
/// </para>
/// </remarks>
public sealed class PlayTelemetry : IPlayTelemetry, IDisposable
{
    private const string Endpoint = "http://log.kuwo.cn/music.yl?encv=1";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    private readonly BodianSession _session;
    private readonly IDeviceIdentity _device;
    private readonly TimeProvider _clock;
    private readonly ILogger<PlayTelemetry> _logger;
    private readonly HttpClient _http;

    public PlayTelemetry(
        BodianSession session,
        IDeviceIdentity device,
        TimeProvider clock,
        ILogger<PlayTelemetry>? logger = null)
        : this(session, device, clock, logger, handler: null)
    {
    }

    internal PlayTelemetry(
        BodianSession session,
        IDeviceIdentity device,
        TimeProvider clock,
        ILogger<PlayTelemetry>? logger,
        HttpMessageHandler? handler)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(clock);

        _session = session;
        _device = device;
        _clock = clock;
        _logger = logger ?? NullLogger<PlayTelemetry>.Instance;
        _http = handler is null
            ? new HttpClient()
            : new HttpClient(handler, disposeHandler: false);
        _http.Timeout = Timeout.InfiniteTimeSpan;
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Dart/3.11 (dart:io)");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("api-ver", "application/json");
    }

    public void ReportCompleted(Track track) => Report(track, track.Duration);

    public void ReportSwitched(Track track, TimeSpan position) => Report(track, position);

    private void Report(Track track, TimeSpan played)
    {
        if (track.Id <= 0
            || track.Duration <= TimeSpan.Zero
            || !_session.IsAuthenticated)
        {
            return;
        }

        // 不 await：统计上报不能挡住切歌或播放结束。异常在 RunAsync 内部全部吞掉。
        _ = RunAsync(track, played);
    }

    private async Task RunAsync(Track track, TimeSpan played)
    {
        try
        {
            var log = PlayLogComposer.Build(
                track,
                played,
                _session.Uid,
                _device.Value,
                _clock.GetLocalNow());
            var body = PlayLogComposer.Encode(log);

            using var timeout = new CancellationTokenSource(RequestTimeout);
            using var content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded");
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = content };
            using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);

            _logger.LogDebug(
                "播放日志已发送：musicId {MusicId}，{Played}/{Duration} 秒，HTTP {Status}",
                track.Id,
                played.TotalSeconds,
                track.Duration.TotalSeconds,
                (int)response.StatusCode);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("播放日志发送超时：musicId {MusicId}", track.Id);
        }
        catch (Exception ex)
        {
            // 网络错误、服务端拒绝都走这里。只记日志，不向用户提示。
            _logger.LogWarning(ex, "播放日志发送失败：musicId {MusicId}", track.Id);
        }
    }

    public void Dispose() => _http.Dispose();
}
