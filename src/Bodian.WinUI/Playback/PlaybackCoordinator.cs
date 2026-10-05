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
    private readonly IPlaybackSettingsStore? _playbackSettings;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private CancellationTokenSource? _operation;
    private bool _disposed;

    /// <summary>
    /// 队列是不是已经播到头了（顺序模式放完最后一首）。
    /// </summary>
    /// <remarks>
    /// 只有 <see cref="PlayAsync"/> 读它：此时引擎里没有加载文件，「播放」的语义是<b>整个队列从第一首重来</b>，
    /// 而不是像试听结束、加载失败那样重播当前这首。任何一次新的播放尝试都会把它清掉（见 <see cref="PlayCurrentAsync"/>）。
    /// </remarks>
    private bool _queueExhausted;

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
        IAudioQualitySettingsStore? qualitySettings = null,
        IPlaybackSettingsStore? playbackSettings = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(history);

        _api = api;
        _engine = engine;
        _history = history;
        _logger = logger ?? NullLogger<PlaybackCoordinator>.Instance;
        _qualitySettings = qualitySettings;
        _playbackSettings = playbackSettings;
        var preference = qualitySettings?.Load() ?? AudioQuality.Lossless;
        PreferredQuality = Enum.IsDefined(preference) ? preference : AudioQuality.Lossless;
        Queue.Mode = playbackSettings?.Load() is { } savedMode && Enum.IsDefined(savedMode)
            ? savedMode
            : PlayMode.Sequential;

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

    /// <summary>
    /// 点播一首：把它插到<b>正在播这首的后面</b>，并立即播放它。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>这是列表里点行的语义。</b> 队列由用户一首首点出来，点一首就多一首 ——
    /// 不再像以前那样把<b>整个列表</b>拖进队列。想一次把列表排进去，走工具栏的「全部加入播放列表」。
    /// </para>
    /// <para>
    /// <b>插到当前曲目之后，不是队尾。</b> 排到队尾的话，正在播这首后面原本排着的歌就被整段跳过去了，
    /// 点的那首一放完就直接撞上 <see cref="QueueExhausted"/>（顺序模式），听起来像「点了首歌就断档」。
    /// 插到当前之后，它顶在下一首的位置、后面原本排着的整体后移，放完照常往下走。
    /// </para>
    /// <para>
    /// <b>队列里已经有同一首时不重复添加</b>，直接跳到原来那一份放：点三次同一首歌不该在队列里出现三份。
    /// 这种情况<b>不把它挪到当前之后</b> —— 挪了就是替用户重排队列，而「已经在队列里」本身说明它早就排好了。
    /// </para>
    /// </remarks>
    public Task EnqueueAndPlayAsync(Track track, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);

        _logger.LogDebug("点播：{Title}", track.Title);

        // 与 PlayQueue.Append 同一套去重规则：没有有效 Id 的曲目无从比对（上游已挡掉）。
        var existing = track.Id > 0 ? IndexOfQueueItem(track.Id) : -1;

        if (existing >= 0)
        {
            // 队列里已经有了 —— 不重复添加，跳到原来那一份放。
            Queue.MoveToItem(existing);
        }
        else
        {
            var wasEmpty = Queue.Count == 0;

            Queue.InsertNext(track);

            // 新曲目就落在当前曲目之后。队列原本为空时 InsertNext 已经把它放上去并落了游标，不用再移。
            if (!wasEmpty)
            {
                Queue.MoveToItem(Queue.CurrentIndex + 1);
            }
        }

        return PlayCurrentAsync(cancellationToken);
    }

    /// <summary>队列里第一条指定曲目 id 的下标；没有返回 <c>-1</c>。</summary>
    private int IndexOfQueueItem(long musicId)
    {
        var items = Queue.Items;

        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Id == musicId)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>下一首。没有下一首时触发 <see cref="QueueExhausted"/> 且不做循环。</summary>
    public Task NextAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("下一首：当前第 {Index} 首，HasNext={HasNext}", Queue.CurrentIndex, Queue.HasNext);

        if (!Queue.MoveNext())
        {
            _queueExhausted = true;
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

    /// <summary>
    /// 让「播放」这个意图生效。**所有播放键都走这里**，不要直接调引擎的 <c>PlayAsync</c>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>引擎里没有加载文件时不能直接调 <see cref="IPlaybackService.PlayAsync"/></b>：
    /// mpv 放完最后一首后回到 idle，此时设 <c>pause=false</c> 是<b>空操作</b>，
    /// 而引擎的乐观更新还会把状态置成「正在播放」——表现就是「点了没声音，歌词却在滚」。
    /// 必须重新解析一次音源再 loadfile（CDN 直链带签名且有时效，旧的很可能已经过期）。
    /// </para>
    /// <para>
    /// 重新加载的目标分两种：<b>队列已经播到头</b>时整个队列从第一首重来，
    /// 否则重播当前这首（试听片段结束、加载失败后重试）。
    /// </para>
    /// </remarks>
    public Task PlayAsync(CancellationToken cancellationToken = default)
    {
        if (_engine.State is PlaybackState.Idle or PlaybackState.Stopped)
        {
            return _queueExhausted
                ? RestartQueueAsync(cancellationToken)
                : ReplayCurrentAsync(cancellationToken);
        }

        return _engine.PlayAsync(cancellationToken);
    }

    /// <summary>整个队列从第一首重新开始播。</summary>
    private Task RestartQueueAsync(CancellationToken cancellationToken = default)
        => Queue.MoveToStart() ? PlayCurrentAsync(cancellationToken) : Task.CompletedTask;

    /// <summary>当前播放模式。</summary>
    public PlayMode Mode => Queue.Mode;

    /// <summary>切到指定模式，并记住。</summary>
    public void SetPlayMode(PlayMode mode)
    {
        if (Queue.Mode == mode)
        {
            return;
        }

        Queue.Mode = mode;
        _playbackSettings?.Save(mode);
        _logger.LogDebug("播放模式切到 {Mode}", mode.DisplayName());
    }

    /// <summary>按「顺序播放 → 列表循环 → 列表随机 → 顺序播放」切到下一个。</summary>
    public void CyclePlayMode() => SetPlayMode(Queue.Mode.Next());

    /// <summary>
    /// 加到队尾。队列里已经有同一首时什么都不做。
    /// </summary>
    /// <remarks>
    /// <b>队列原本是空的时候直接开播</b>：否则加了没有任何反应，用户会以为没生效 ——
    /// 而「队列是空的」恰恰是第一次用这个入口时最常见的状态。
    /// </remarks>
    /// <returns>真的加进去了返回 <c>true</c>；队列里已有同一首返回 <c>false</c>，调用方据此改提示。</returns>
    public async Task<bool> AddToQueueAsync(Track track, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);

        var wasEmpty = Queue.Count == 0;

        if (!Queue.Append(track))
        {
            return false;
        }

        if (wasEmpty)
        {
            await PlayCurrentAsync(cancellationToken).ConfigureAwait(true);
        }

        return true;
    }

    /// <summary>
    /// 把一批曲目加到队尾，队列里已有的（按 Id）与本批内重复的都跳过。
    /// </summary>
    /// <remarks>
    /// <b>队列原本是空的时候直接开播</b>：沿袭单首版 <see cref="AddToQueueAsync(Track, CancellationToken)"/>
    /// 的语义，否则加了没有任何反应。<b>非空队列只追加，不动游标</b>，正在播的那首不被打断。
    /// </remarks>
    /// <returns>实际追加的条数；全都已在队列里时返回 <c>0</c>。</returns>
    public async Task<int> AddToQueueAsync(IReadOnlyList<Track> tracks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        if (tracks.Count == 0)
        {
            return 0;
        }

        var wasEmpty = Queue.Count == 0;
        var added = Queue.AppendRange(tracks);

        _logger.LogDebug("批量加入队列：追加 {Added} 首（传入 {Count} 首）", added, tracks.Count);

        if (added > 0 && wasEmpty)
        {
            await PlayCurrentAsync(cancellationToken).ConfigureAwait(true);
        }

        return added;
    }

    /// <summary>插到当前曲目之后。队列为空时等同于 <see cref="AddToQueueAsync"/>。</summary>
    public Task PlayNextAsync(Track track, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);

        var wasEmpty = Queue.Count == 0;
        Queue.InsertNext(track);

        return wasEmpty ? PlayCurrentAsync(cancellationToken) : Task.CompletedTask;
    }

    /// <summary>跳到队列里第 <paramref name="itemIndex"/> 首并开始播。</summary>
    public Task PlayQueueItemAsync(int itemIndex, CancellationToken cancellationToken = default)
        => Queue.MoveToItem(itemIndex) ? PlayCurrentAsync(cancellationToken) : Task.CompletedTask;

    /// <summary>
    /// 从队列里删掉第 <paramref name="itemIndex"/> 首。
    /// </summary>
    /// <remarks>
    /// <b>只有删掉的正好是当前曲目时才换歌。</b> 界面把正在播放那一行的删除按钮置灰，
    /// 所以这个分支正常走不到；留着是因为队列是共享状态，别处也会改它。
    /// 队列被删空时 <see cref="PlayCurrentAsync"/> 自己会直接返回，正在放的那首不受影响。
    /// </remarks>
    public Task RemoveQueueItemAsync(int itemIndex, CancellationToken cancellationToken = default)
    {
        var wasCurrent = Queue.CurrentIndex == itemIndex;

        if (!Queue.RemoveItem(itemIndex) || !wasCurrent)
        {
            return Task.CompletedTask;
        }

        return PlayCurrentAsync(cancellationToken);
    }

    /// <summary>
    /// 清空队列。
    /// </summary>
    /// <remarks>
    /// <b>正在播的那一首不打断</b>，让它自然放完 ——「清空列表」说的是列表，不是「停止播放」。
    /// </remarks>
    public void ClearQueue() => Queue.Clear();

    /// <summary>解析并播放当前队列项。</summary>
    private async Task PlayCurrentAsync(CancellationToken cancellationToken = default)
    {
        if (Queue.Current is not { } track) { return; }

        // 队列里确实有一首、接下来就要解析并加载它 —— 上一轮的「播到头」到此翻篇。
        _queueExhausted = false;

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
