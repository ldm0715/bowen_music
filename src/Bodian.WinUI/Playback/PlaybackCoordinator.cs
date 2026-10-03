using Bodian.Core.Api;
using Bodian.Core.Models;
using Bodian.Core.Services.Abstractions;
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
public sealed class PlaybackCoordinator : IDisposable
{
    private readonly IBodianApi _api;
    private readonly IPlaybackService _engine;
    private readonly IPlayHistoryStore _history;
    private readonly ILogger<PlaybackCoordinator> _logger;
    private readonly IAudioQualitySettingsStore? _qualitySettings;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private CancellationTokenSource? _operation;
    private bool _disposed;

    public AudioQuality PreferredQuality { get; private set; } = AudioQuality.Lossless;
    public AudioSource? CurrentSource { get; private set; }
    public bool IsChangingQuality { get; private set; }
    public event EventHandler? QualityOptionsChanged;
    public event EventHandler<PlaybackQualityChangedEventArgs>? QualityChanged;
    public event EventHandler<PlaybackFailedEventArgs>? QualityChangeFailed;

    public PlaybackCoordinator(
        IBodianApi api,
        IPlaybackService engine,
        IPlayHistoryStore history,
        ILogger<PlaybackCoordinator>? logger = null,
        IAudioQualitySettingsStore? qualitySettings = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(history);

        _api = api;
        _engine = engine;
        _history = history;
        _logger = logger ?? NullLogger<PlaybackCoordinator>.Instance;
        _qualitySettings = qualitySettings;
        var preference = qualitySettings?.Load() ?? AudioQuality.Lossless;
        PreferredQuality = Enum.IsDefined(preference) ? preference : AudioQuality.Lossless;

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
        if (Queue.Current is not { } track) { return; }
        using var operation = BeginOperation(cancellationToken);
        var ct = operation.Token;
        try
        {
            var resolution = await _api.ResolvePlaybackAsync(track, PreferredQuality, ct).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();
            var policy = PlaybackPolicy.For(resolution);
            switch (resolution)
            {
                case PlaybackResolution.Playable playable:
                    await StartAsync(track, policy, playable.Source, null, null, ct).ConfigureAwait(true);
                    break;
                case PlaybackResolution.AuditionOnly audition:
                    await StartAsync(track, policy, audition.Source, audition.Start, audition.End, ct)
                        .ConfigureAwait(true);
                    break;
                case PlaybackResolution.Denied denied:
                    _logger.LogInformation("不能播放：{Title}（{Reason}）", track.Title, denied.Reason);
                    Blocked?.Invoke(this, new PlaybackBlockedEventArgs(track, denied.Reason, null));
                    break;
            }
        }
        catch (OperationCanceledException) when (!ReferenceEquals(_operation, operation)) { }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (ReferenceEquals(_operation, operation))
            {
                _logger.LogWarning("音源解析或加载失败：{Title}（{ErrorType}）", track.Title, ex.GetType().Name);
                Blocked?.Invoke(this, new PlaybackBlockedEventArgs(track, PlaybackDenialReason.NoStreamUrl, SafeError(ex)));
            }
        }
        finally { CompleteOperation(operation); }
    }

    private async Task StartAsync(
        Track track,
        PlaybackPolicy policy,
        AudioSource source,
        TimeSpan? start,
        TimeSpan? end,
        CancellationToken cancellationToken)
    {
        RequirePlayableSource(source);
        await _loadGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _engine.LoadAsync(new PlaybackSource(source.Url, start, end, track.Title), cancellationToken)
                .ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            CurrentTrack = track;
            CurrentPolicy = policy;
            CurrentSource = source;
            Started?.Invoke(this, new PlaybackStartedEventArgs(track, policy, source));
            QualityOptionsChanged?.Invoke(this, EventArgs.Empty);
            RecordHistory(track);
        }
        finally { _loadGate.Release(); }
    }

    /// <summary>重新取当前曲目的指定音源，保留位置、暂停、队列及播放历史。</summary>
    public async Task SwitchQualityAsync(AudioQuality quality, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(quality)) { throw new ArgumentOutOfRangeException(nameof(quality)); }
        if (CurrentTrack is not { } track)
        {
            SetPreferredQuality(quality);
            return;
        }
        if (CurrentPolicy?.IsAudition != false || !track.AvailableQualities.Contains(quality)) { return; }
        if (CurrentSource?.RequestedQuality == quality && CurrentSource.WasDowngraded == false)
        {
            SetPreferredQuality(quality);
            return;
        }
        using var operation = BeginOperation(cancellationToken);
        var ct = operation.Token;
        IsChangingQuality = true;
        QualityOptionsChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            var resolution = await _api.ResolvePlaybackAsync(track, quality, ct).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();
            if (resolution is not PlaybackResolution.Playable playable)
            {
                QualityChangeFailed?.Invoke(this, new PlaybackFailedEventArgs("当前账号无法使用所选音质，继续播放原音源"));
                return;
            }
            RequirePlayableSource(playable.Source);
            await _loadGate.WaitAsync(ct).ConfigureAwait(true);
            try
            {
                ct.ThrowIfCancellationRequested();
                if (!ReferenceEquals(CurrentTrack, track)) { return; }
                // 取地址和解密时旧音源继续播放，真正替换前才读位置与暂停状态。
                var position = _engine.Position;
                var paused = _engine.State == PlaybackState.Paused;
                var oldSource = CurrentSource;
                try
                {
                    await _engine.LoadAsync(new PlaybackSource(playable.Source.Url, position, null, track.Title, paused), ct)
                        .ConfigureAwait(true);
                    ct.ThrowIfCancellationRequested();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    if (oldSource is not null && !ct.IsCancellationRequested)
                    {
                        await _engine.LoadAsync(new PlaybackSource(oldSource.Url, position, null, track.Title, paused), ct)
                            .ConfigureAwait(true);
                    }
                    throw;
                }
                var source = playable.Source;
                CurrentSource = source;
                CurrentPolicy = PlaybackPolicy.For(resolution);
                SetPreferredQuality(quality);
                QualityChanged?.Invoke(this, new PlaybackQualityChangedEventArgs(track, source, position));
            }
            finally { _loadGate.Release(); }
        }
        catch (OperationCanceledException) when (!ReferenceEquals(_operation, operation)) { }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (ReferenceEquals(_operation, operation))
            {
                QualityChangeFailed?.Invoke(this, new PlaybackFailedEventArgs($"音质切换失败：{SafeError(ex)}"));
            }
        }
        finally { CompleteOperation(operation); }
    }

    private CancellationTokenSource BeginOperation(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var previous = _operation;
        _operation = CancellationTokenSource.CreateLinkedTokenSource(token);
        previous?.Cancel();
        IsChangingQuality = false;
        QualityOptionsChanged?.Invoke(this, EventArgs.Empty);
        return _operation;
    }

    private void CompleteOperation(CancellationTokenSource operation)
    {
        if (!ReferenceEquals(_operation, operation)) { return; }
        _operation = null;
        IsChangingQuality = false;
        QualityOptionsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetPreferredQuality(AudioQuality quality)
    {
        PreferredQuality = quality;
        _qualitySettings?.Save(quality);
        QualityOptionsChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void RequirePlayableSource(AudioSource source)
    {
        if (!AudioQualityTable.IsPlayableFormat(source.Format))
        {
            throw new InvalidDataException("服务端返回了本客户端不支持的音源");
        }
    }

    private static string SafeError(Exception exception) => exception switch
    {
        InvalidDataException => exception.Message,
        _ => "音源请求或加载失败，请重试",
    };

    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        _operation?.Cancel();
        _engine.Ended -= OnEngineEnded;
        _engine.Failed -= OnEngineFailed;
    }

    /// <summary>
    /// 记一次播放历史。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>写在 <see cref="Started"/> 之后而不是 <see cref="PlayFromAsync"/> 里</b>：
    /// 被拒绝的曲目根本没播过，不该进历史；而试听（<c>status=3</c>）确实播了，该进。
    /// 那个分界正是这个事件的语义。
    /// </para>
    /// <para>
    /// <b>不 await。</b> 记录历史是一次本地文件写入，让它挡住起播没有道理；
    /// 失败也只影响「最近播放」一页，所以这里自己吞掉异常并记日志。
    /// </para>
    /// </remarks>
    private void RecordHistory(Track track) => _ = RecordHistoryAsync(track);

    private async Task RecordHistoryAsync(Track track)
    {
        try
        {
            await _history.RecordAsync(track).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "写播放历史失败：{Title}", track.Title);
        }
    }

    private void OnEngineEnded(object? sender, EventArgs e)
    {
        // 试听结束不自动续播：30 秒片段连着放会让人以为「连着播」坏了，
        // 也会在用户不知情时消耗整个队列。停在原地并提示更合适。
        if (IsChangingQuality) { return; }
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

/// <summary>同一曲目音质已切换；与 Started 分开，歌词及系统媒体元数据无需重新加载。</summary>
public sealed class PlaybackQualityChangedEventArgs(Track track, AudioSource source, TimeSpan position) : EventArgs
{
    public Track Track { get; } = track;
    public AudioSource Source { get; } = source;
    public TimeSpan Position { get; } = position;
}
