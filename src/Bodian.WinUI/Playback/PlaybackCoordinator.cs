using Bodian.Core.Api;
using Bodian.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.WinUI.Playback;

/// <summary>
/// 把「授权判断」「队列」「播放引擎」串起来的地方。
/// </summary>
/// <remarks>
/// <para>
/// 分工：<c>Bodian.Core</c> 决定<b>能不能播</b>，本类决定<b>播什么、什么时候播</b>，
/// <see cref="IPlaybackService"/> 只负责把字节放到声卡上。领域判断一行都不进引擎。
/// </para>
/// <para>
/// <b>每次播放都现取地址</b>：CDN 直链带签名且有时效，不能预先为队列表里的歌批量取。
/// </para>
/// </remarks>
public sealed class PlaybackCoordinator
{
    private readonly IBodianApi _api;
    private readonly IPlaybackService _engine;
    private readonly ILogger<PlaybackCoordinator> _logger;

    public PlaybackCoordinator(
        IBodianApi api,
        IPlaybackService engine,
        ILogger<PlaybackCoordinator>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(engine);

        _api = api;
        _engine = engine;
        _logger = logger ?? NullLogger<PlaybackCoordinator>.Instance;

        _engine.Ended += OnEngineEnded;
        _engine.Failed += OnEngineFailed;
    }

    /// <summary>当前队列。</summary>
    public PlayQueue Queue { get; } = new();

    /// <summary>当前曲目的播放策略。没播过时为 <c>null</c>。</summary>
    public PlaybackPolicy? CurrentPolicy { get; private set; }

    /// <summary>
    /// 当前真正在播的曲目。没播成过时为 <c>null</c>（被拒绝的曲目不会写进来）。
    /// </summary>
    /// <remarks>
    /// <b>不要拿 <see cref="PlayQueue.Current"/> 当它用。</b> <see cref="PlayFromAsync"/> 里
    /// <c>Queue.Replace</c> 早于音源解析完成，而 <see cref="Started"/> 要等两个 await 之后才到 ——
    /// 切歌的那一瞬间 <c>Queue.Current</c> 已经是下一首了。
    /// 读的人和 <see cref="Started"/> 的订阅方必须看到同一个答案，所以赋值放在事件触发之前。
    /// </remarks>
    public Track? CurrentTrack { get; private set; }

    /// <summary>成功开始播放（含试听）。</summary>
    public event EventHandler<PlaybackStartedEventArgs>? Started;

    /// <summary>不能播。UI 据此提示，并区分「去登录」与「如实告知」。</summary>
    public event EventHandler<PlaybackBlockedEventArgs>? Blocked;

    /// <summary>试听片段播完了（整曲播完会走自动下一首，不触发这个）。</summary>
    public event EventHandler? AuditionEnded;

    /// <summary>队列走到头了。</summary>
    public event EventHandler? QueueExhausted;

    /// <summary>从一整页结果开始播第 <paramref name="index"/> 首。</summary>
    public Task PlayFromAsync(IReadOnlyList<Track> playlist, int index, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playlist);

        _logger.LogDebug("从队列第 {Index} 首开始播（共 {Count} 首）", index, playlist.Count);

        Queue.Replace(playlist, index);

        return PlayCurrentAsync(cancellationToken);
    }

    /// <summary>下一首。没有下一首时触发 <see cref="QueueExhausted"/> 且不做循环。</summary>
    public Task NextAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("下一首：当前第 {Index} 首，HasNext={HasNext}", Queue.CurrentIndex, Queue.HasNext);

        if (!Queue.MoveNext())
        {
            QueueExhausted?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        return PlayCurrentAsync(cancellationToken);
    }

    public Task PreviousAsync(CancellationToken cancellationToken = default)
    {
        if (!Queue.HasPrevious)
        {
            return Task.CompletedTask;
        }

        Queue.MovePrevious();

        return PlayCurrentAsync(cancellationToken);
    }

    /// <summary>重播当前曲目。被拒绝或出错后重试时用，会重新取一次地址。</summary>
    public Task ReplayCurrentAsync(CancellationToken cancellationToken = default)
        => PlayCurrentAsync(cancellationToken);

    /// <summary>解析并播放当前队列项。</summary>
    private async Task PlayCurrentAsync(CancellationToken cancellationToken = default)
    {
        if (Queue.Current is not { } track)
        {
            return;
        }

        PlaybackResolution resolution;

        try
        {
            resolution = await _api.ResolvePlaybackAsync(track, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 网络或服务端异常：如实报，不要当成「无权限」。
            _logger.LogWarning(ex, "解析播放授权失败：{Title}", track.Title);
            Blocked?.Invoke(this, new PlaybackBlockedEventArgs(track, PlaybackDenialReason.NoStreamUrl, ex.Message));
            return;
        }

        var policy = PlaybackPolicy.For(resolution);
        CurrentPolicy = policy;

        switch (resolution)
        {
            case PlaybackResolution.Playable playable:
                await StartAsync(track, policy, playable.Source, playable.Source.Url, null, null, cancellationToken)
                    .ConfigureAwait(true);
                break;

            case PlaybackResolution.AuditionOnly audition:
                // 把试听区间交给引擎，由 mpv 自己在 end 处停住（主防线）。
                await StartAsync(
                        track,
                        policy,
                        audition.Source,
                        audition.Source.Url,
                        audition.Start,
                        audition.End,
                        cancellationToken)
                    .ConfigureAwait(true);
                break;

            case PlaybackResolution.Denied denied:
                _logger.LogInformation("不能播放：{Title}（{Reason}）", track.Title, denied.Reason);
                Blocked?.Invoke(this, new PlaybackBlockedEventArgs(track, denied.Reason, null));
                break;
        }
    }

    private async Task StartAsync(
        Track track,
        PlaybackPolicy policy,
        AudioSource source,
        Uri url,
        TimeSpan? start,
        TimeSpan? end,
        CancellationToken cancellationToken)
    {
        await _engine.LoadAsync(new PlaybackSource(url, start, end, track.Title), cancellationToken)
            .ConfigureAwait(true);

        // ★ 先赋值再触发事件：订阅方在事件处理里读 CurrentTrack 时必须是这一首。
        CurrentTrack = track;

        Started?.Invoke(this, new PlaybackStartedEventArgs(track, policy, source));
    }

    private void OnEngineEnded(object? sender, EventArgs e)
    {
        // 试听结束不自动续播：30 秒片段连着放会让人以为「连着播」坏了，
        // 也会在用户不知情时消耗整个队列。停在原地并提示更合适。
        if (CurrentPolicy?.IsCompleteTrack == true)
        {
            _ = AdvanceAfterEndAsync();
            return;
        }

        AuditionEnded?.Invoke(this, EventArgs.Empty);
    }

    private async Task AdvanceAfterEndAsync()
    {
        try
        {
            await NextAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "自动播放下一首失败");
        }
    }

    private void OnEngineFailed(object? sender, PlaybackFailedEventArgs e)
    {
        _logger.LogWarning("播放引擎出错：{Message}", e.Message);
    }
}

/// <summary>成功开始播放。</summary>
public sealed class PlaybackStartedEventArgs(Track track, PlaybackPolicy policy, AudioSource source) : EventArgs
{
    public Track Track { get; } = track;

    public PlaybackPolicy Policy { get; } = policy;

    public AudioSource Source { get; } = source;
}

/// <summary>不能播放。</summary>
public sealed class PlaybackBlockedEventArgs(Track track, PlaybackDenialReason reason, string? detail) : EventArgs
{
    public Track Track { get; } = track;

    public PlaybackDenialReason Reason { get; } = reason;

    /// <summary>附加信息（网络异常等）。没有时为 <c>null</c>。</summary>
    public string? Detail { get; } = detail;
}
