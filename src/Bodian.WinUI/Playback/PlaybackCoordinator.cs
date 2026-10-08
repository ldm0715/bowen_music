using Bodian.Core.Api;
using Bodian.Core.Models;
using Bodian.Core.Services;
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
    private readonly IPlayQueueSnapshotStore? _queueStore;
    private readonly ICurrentAccount? _account;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly System.Threading.Timer? _persistTimer;
    private CancellationTokenSource? _operation;
    private bool _disposed;

    /// <summary>
    /// 内存里这份队列属于哪个作用域。落盘一律用它，<b>不问「当前是谁」</b> ——
    /// 切号那一刻「当前是谁」已经是新账号了，拿它去写会把上一个账号的队列写进新账号的目录。
    /// </summary>
    private string _scope;

    /// <summary>
    /// 构造本类时的线程。账号变更事件可能来自线程池，切号要回到这个线程上做。
    /// </summary>
    /// <remarks>
    /// 在应用里这个值一定不是 <c>null</c>：<c>Program.RunWinUi</c> 在 <c>new App()</c> 之前就显式装了
    /// <c>DispatcherQueueSynchronizationContext</c>，而本类是在 <c>OnLaunched</c> 里解析主窗口时构造的。
    /// <b>不直接用 <c>DispatcherQueue</c></b>：这个文件被链接进 <c>net10.0</c> 的测试工程，
    /// 那里没有 WinUI 类型；测试里它就是 <c>null</c>，切号同步做完。
    /// </remarks>
    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;

    /// <summary>正在装/清队列。<b>期间不落盘</b>：那会把「刚读出来的东西」立刻写回同一个文件。</summary>
    private bool _switchingScope;

    /// <summary>队列落盘的防抖时长。见 <see cref="RequestPersist"/>。</summary>
    private static readonly TimeSpan SnapshotDebounce = TimeSpan.FromSeconds(1);

    /// <summary>播放中不切歌时，进度落盘的粒度。见 <see cref="OnEnginePositionChangedForPersist"/>。</summary>
    private static readonly TimeSpan PositionCheckpoint = TimeSpan.FromSeconds(30);

    /// <summary>距末尾不足这么多就不续播。见 <see cref="IsResumable"/>。</summary>
    private static readonly TimeSpan ResumeTailGuard = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 最新一份待写快照 + <b>它属于哪个作用域</b>；计时器线程取走它去落盘。
    /// </summary>
    /// <remarks>
    /// <b>作用域必须在抓快照的同一刻定下来</b>，不能等落盘时再读 <see cref="_scope"/>：
    /// 中间隔着最多一秒的防抖，而切号就发生在这段时间里 ——
    /// 落盘时读到的会是新账号，于是上一份队列被写进新账号的目录，正是这次要修的东西。
    /// </remarks>
    private PendingSnapshot? _pending;

    /// <summary>一份待写快照与它的归属。用类而不是元组是为了能 <c>Interlocked.Exchange</c>。</summary>
    private sealed record PendingSnapshot(string Scope, PlayQueueSnapshot Snapshot);

    /// <summary>「重启后恢复播放列表」开关。关掉时所有落盘路径都早退。</summary>
    private bool _restoreEnabled = true;

    /// <summary>待续播曲目的 id。<b>按 id 配对</b>，起播成功即消费掉（见 <see cref="StartAsync"/>）。</summary>
    private long _resumeTrackId;

    /// <summary>待续播曲目停在第几秒。与 <see cref="_resumeTrackId"/> 同生共死。</summary>
    private TimeSpan _resumePosition;

    /// <summary>进度检查点的基准位置。见 <see cref="OnEnginePositionChangedForPersist"/>。</summary>
    private TimeSpan _lastCheckpoint;

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

    /// <summary>
    /// 换了账号、队列跟着换了一份之后触发。
    /// </summary>
    /// <remarks>
    /// <b>由本类抛、由外壳弹提示</b>：本类不能注入 <c>INoticeSink</c> —— 那指向
    /// <c>TrackActionsService</c>，而后者依赖本类，会成环。
    /// </remarks>
    public event EventHandler<QueueScopeSwitchedEventArgs>? ScopeSwitched;

    public PlaybackCoordinator(
        IBodianApi api,
        IPlaybackService engine,
        IPlayHistoryStore history,
        ILogger<PlaybackCoordinator>? logger = null,
        IAudioQualitySettingsStore? qualitySettings = null,
        IPlaybackSettingsStore? playbackSettings = null,
        IPlayQueueSnapshotStore? queueStore = null,
        ICurrentAccount? account = null)
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
        _queueStore = queueStore;
        _account = account;
        _scope = account?.Scope ?? AppPaths.AnonymousScope;

        var preference = qualitySettings?.Load() ?? AudioQuality.Lossless;
        PreferredQuality = Enum.IsDefined(preference) ? preference : AudioQuality.Lossless;

        var settings = playbackSettings?.Load() ?? PlaybackSettings.Default;

        // 模式要先于恢复设置：Replace 会按当前模式建排列，反过来的话随机模式恢复不出洗牌序。
        Queue.Mode = Enum.IsDefined(settings.Mode) ? settings.Mode : PlayMode.Sequential;

        // 「记住播放列表」是个人级偏好（在 playback.json 里），不再跟着快照走。见 PlayQueueSnapshot 的说明。
        _restoreEnabled = settings.RestoreQueue;

        // 顺序不能改：先把磁盘上的队列装回去，再挂落盘钩子。
        // 恢复那一刻一个订阅者都没有（播放条、队列面板、系统媒体控件都依赖本类，构造都在本类之后），
        // 所以 Replace 抛出的 Changed 引发不了任何反应 ——「刚读出来就写回去」与「往系统媒体面板
        // 注册一条假会话」这两条在结构上不可能发生，因此不需要一个加载中的守卫标志。
        // 谁把下面的订阅挪到 RestoreQueue 之前，就会踩这两个坑。
        // （切号时订阅已经在了，那里另有一个 _switchingScope 守卫。）
        // 开关关掉时不装：磁盘上那份本来就已经被删掉了，这里再读一次只会把「关掉」变成「这次先例外」。
        if (_restoreEnabled)
        {
            RestoreQueue(queueStore?.Load(_scope) ?? PlayQueueSnapshot.Default);
        }

        Queue.Changed += OnQueueChangedForPersist;
        _engine.PositionChanged += OnEnginePositionChangedForPersist;
        _engine.StateChanged += OnEngineStateChangedForPersist;
        _persistTimer = queueStore is null
            ? null
            : new System.Threading.Timer(_ => FlushPending(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        _engine.Ended += OnEngineEnded;
        _engine.Failed += OnEngineFailed;

        if (account is not null)
        {
            account.Changed += OnAccountChanged;
        }
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

    /// <summary>
    /// 启动时从磁盘恢复出来的「上次停在这」。没有恢复时为 <c>null</c>。
    /// </summary>
    /// <remarks>
    /// <b>只给播放条视图模型在构造时读一次。</b> 它存在的唯一理由见那边的应用方法：不把这一首铺到
    /// 播放条上，播放键就是禁用的，用户没有入口把恢复出来的队列放起来。
    /// <para>
    /// <b>刻意保持 internal，且不要出现在 XAML 可见类型的公开属性上。</b> 类型信息生成器会为公开
    /// 属性里的类型生成激活代码，而 <see cref="Track"/> 带 required 成员、生成器造不出实例。
    /// </para>
    /// </remarks>
    internal RestoredQueueState? RestoredState { get; private set; }

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
        _playbackSettings?.Save(new PlaybackSettings(mode, _restoreEnabled));
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

    /// <summary>
    /// 把队列里第 <paramref name="fromItemIndex"/> 首挪到第 <paramref name="toItemIndex"/> 位
    /// （<paramref name="toItemIndex"/> 是挪完后的最终下标）。
    /// </summary>
    /// <remarks>
    /// <b>完全不碰引擎</b>：当前曲目还是同一首（<see cref="PlayQueue.MoveItem"/> 会把它一起搬），
    /// 不需要重新解析音源、也不用重发 <see cref="Started"/>。
    /// </remarks>
    /// <returns>真的挪动了返回 <c>true</c>。见 <see cref="PlayQueue.MoveItem"/> 的边界说明。</returns>
    public bool MoveQueueItem(int fromItemIndex, int toItemIndex)
        => Queue.MoveItem(fromItemIndex, toItemIndex);

    /// <summary>队列顺序能不能被用户重排。界面据此决定显不显示拖动条。</summary>
    public bool CanReorderQueue => Queue.CanReorder;

    /// <summary>「重启后恢复播放列表」这个开关。</summary>
    public bool RestoreQueueEnabled => _restoreEnabled;

    /// <summary>
    /// 改「记住播放列表」开关。它是<b>个人级偏好</b>（跨账号共用），存在 <c>playback.json</c> 里。
    /// </summary>
    /// <remarks>
    /// <b>关掉时把盘上所有账号的队列一并抹掉</b>：用户说的是「别记我的播放列表」，留一份等下次
    /// 打开复活，与这句话相反。只清当前账号那份不够 —— 换号之后别人的队列会冒出来。
    /// 抹掉之后「重新打开开关时从下一次变更开始保存」这条也就自动成立。
    /// <b>不动内存里的队列</b>：关掉只是不落盘，正在播的那首、抽屉里的列表照旧。
    /// </remarks>
    /// <returns>写盘成功返回 <c>true</c>；失败时这个开关一并回滚，调用方据此回滚界面并提示。</returns>
    public bool SetRestoreQueueEnabled(bool enabled)
    {
        if (_restoreEnabled == enabled) { return true; }

        var previous = _restoreEnabled;
        _restoreEnabled = enabled;

        _playbackSettings?.Save(new PlaybackSettings(Queue.Mode, enabled));

        if (_queueStore is null || enabled)
        {
            _logger.LogInformation("恢复播放列表：{State}", enabled ? "开" : "关");
            return true;
        }

        if (_queueStore.DeleteAllScopes())
        {
            _logger.LogInformation("恢复播放列表：关（并已抹掉盘上各账号的队列）");
            return true;
        }

        // 删不掉就把开关退回去，别让界面显示「已关闭」而磁盘上还留着。
        _restoreEnabled = previous;
        _playbackSettings?.Save(new PlaybackSettings(Queue.Mode, previous));
        return false;
    }

    /// <summary>
    /// 退出前的同步落盘。
    /// </summary>
    /// <remarks>
    /// <b>由应用在释放引擎之前调用</b>（位置要从引擎读）。只在真退出时走这条路 —— 点 ✕ 关到托盘
    /// 取消了窗口关闭，这里不会被触发，正如所愿。它补上「最后一次变更的防抖还没到点就退出了」那个窗口。
    /// </remarks>
    public void FlushForShutdown()
    {
        if (!_restoreEnabled || _queueStore is null) { return; }

        _persistTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _pending = null;                            // 排队中那份已经过时
        _queueStore.Save(_scope, CaptureSnapshot());  // 同步：进程马上要没了
    }

    /// <summary>
    /// 账号变了（登录 / 登出 / 被服务端清）。
    /// </summary>
    /// <remarks>
    /// <b>同步触发时可能是任意线程</b>（11012 走的是解析响应那条路），而切号要读 <c>Queue.Items</c>
    /// 那个 <c>List</c>、还要动界面绑定的东西，所以先回到构造本类时的线程上。
    /// </remarks>
    private void OnAccountChanged(object? sender, EventArgs e)
    {
        if (_uiContext is null || ReferenceEquals(SynchronizationContext.Current, _uiContext))
        {
            SwitchScope();
            return;
        }

        _uiContext.Post(_ => SwitchScope(), null);
    }

    /// <summary>
    /// 把内存里的队列换成另一个作用域的。<b>顺序不能改。</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>先把内存这份写回它原来那个作用域</b>：<see cref="_scope"/> 此刻还是旧值，
    /// 而防抖还没到点的那份快照属于旧账号 —— 晚一步就会写进新账号的目录。
    /// </para>
    /// <para>
    /// <b>停播</b>：正在播的那首属于上一个账号，用新账号的权限接着播可能被拒或降级。
    /// </para>
    /// <para>
    /// <b>清队列与装新队列期间不落盘</b>（<see cref="_switchingScope"/>）：否则这次清空本身
    /// 会被当成一次「队列变更」写回去 —— 而它写的还是刚换成的新作用域。
    /// </para>
    /// </remarks>
    private void SwitchScope()
    {
        var next = _account?.Scope ?? AppPaths.AnonymousScope;

        if (next == _scope || _disposed) { return; }

        var hadSomething = Queue.Count > 0 || CurrentTrack is not null;

        // 整段都不落盘：停播会从引擎收到状态变化，清队列与装新队列会让 Queue 抛 Changed ——
        // 那两件事都会被当成「队列变更」，而它们记的是切换过程本身，不是用户的编辑。
        _switchingScope = true;
        try
        {
            _persistTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            FlushPending();     // 此刻 _scope 还是旧值，这份快照归它
            _pending = null;

            StopForScopeSwitch();

            _scope = next;

            Queue.Clear();
            if (_restoreEnabled)
            {
                RestoreQueue(_queueStore?.Load(_scope) ?? PlayQueueSnapshot.Default);
            }
        }
        finally
        {
            _switchingScope = false;
        }

        _logger.LogInformation("播放队列已切换到作用域 {Scope}", _scope);

        ScopeSwitched?.Invoke(this, new QueueScopeSwitchedEventArgs(hadSomething));
    }

    /// <summary>切号时把正在播的停掉，并清掉与「哪首歌」相关的状态。</summary>
    private void StopForScopeSwitch()
    {
        _operation?.Cancel();
        _ = _engine.StopAsync();
        CurrentTrack = null;
        CurrentPolicy = null;
        RestoredState = null;
        _resumeTrackId = 0;
        _resumePosition = TimeSpan.Zero;
        _lastCheckpoint = TimeSpan.Zero;
        _queueExhausted = false;
    }

    /// <summary>
    /// 把某个作用域的快照装回队列。<b>构造时必须在挂落盘钩子之前调用。</b>
    /// </summary>
    /// <remarks>
    /// <b>不碰「记住播放列表」开关</b>：那个开关现在是个人级偏好，住在 <c>playback.json</c> 里，
    /// 不由快照决定（见 <c>PlayQueueSnapshot</c>）。
    /// </remarks>
    private void RestoreQueue(PlayQueueSnapshot snapshot)
    {
        if (snapshot.Items.Length == 0) { return; }

        var tracks = new List<Track>(snapshot.Items.Length);
        foreach (var item in snapshot.Items)
        {
            // 坏条目丢掉，别让它拖垮整条队列。
            if (item.Id <= 0 || item.Title.Length == 0) { continue; }
            tracks.Add(item.ToTrack());
        }

        if (tracks.Count == 0) { return; }

        // 以 id 为准找回当前曲目：坏条目被过滤后下标会整体错位，所以 id 优先、下标只作兜底。
        var index = snapshot.CurrentTrackId > 0
            ? tracks.FindIndex(t => t.Id == snapshot.CurrentTrackId)
            : -1;
        if (index < 0) { index = Math.Clamp(snapshot.CurrentIndex, 0, tracks.Count - 1); }

        Queue.Replace(tracks, index);

        var position = double.IsFinite(snapshot.PositionSeconds) && snapshot.PositionSeconds > 0
            ? TimeSpan.FromSeconds(snapshot.PositionSeconds)
            : TimeSpan.Zero;

        _resumeTrackId = tracks[index].Id;
        _resumePosition = position;
        _lastCheckpoint = position;
        RestoredState = new RestoredQueueState(tracks[index], position);

        _logger.LogInformation("已恢复播放列表：{Count} 首，当前第 {Index} 首，续播位置 {Position}",
            tracks.Count, index, position);
    }

    /// <summary>
    /// 这次该从第几秒起播；没有待续播时返回 <c>null</c>（从头播）。
    /// </summary>
    /// <remarks>
    /// <b>按 id 配对</b>：用户换了别的歌、或队列里那首被删掉重建，id 对不上自然失效，
    /// 不需要在切歌路径上清理。试听片段不走这里（见 <see cref="PlayCurrentAsync"/> 的试听分支）。
    /// </remarks>
    private TimeSpan? ResumePointFor(Track track)
    {
        if (track.Id <= 0 || track.Id != _resumeTrackId || _resumePosition <= TimeSpan.Zero)
        {
            return null;
        }

        return IsResumable(track, _resumePosition) ? _resumePosition : null;
    }

    /// <summary>
    /// 这个位置还值得续吗。
    /// </summary>
    /// <remarks>
    /// <b>用绝对秒数而不是百分比</b>：上次停在 95% 的地方，恢复后点播放只会听到最后几秒然后跳到下一首 ——
    /// 那不是「接着听」，是「播了个尾巴」。而 10 分钟的歌只剩 30 秒是值得续的、3 分钟的歌只剩 9 秒不值，
    /// 百分比在两处都给出错误的答案。时长未知（服务端没给）时只能信这个位置本身。
    /// </remarks>
    private static bool IsResumable(Track track, TimeSpan position)
    {
        var duration = track.Duration;
        return duration <= TimeSpan.Zero || duration - position > ResumeTailGuard;
    }

    /// <summary>
    /// 抓一份当前状态。<b>只能在 UI 线程调</b> —— 要读 <c>Queue.Items</c>（一个 List）。
    /// </summary>
    private PlayQueueSnapshot CaptureSnapshot()
    {
        var items = Queue.Items;
        var buffer = new QueuedTrack[items.Count];
        for (var i = 0; i < items.Count; i++) { buffer[i] = QueuedTrack.From(items[i]); }

        return new PlayQueueSnapshot
        {
            Items = buffer,
            CurrentTrackId = Queue.Current?.Id ?? 0,
            CurrentIndex = Queue.CurrentIndex,
            PositionSeconds = CurrentPositionSeconds(),
        };
    }

    /// <summary>
    /// 当前曲目此刻停在第几秒。
    /// </summary>
    /// <remarks>
    /// <b>引擎里真的装着这一首时才读引擎</b>，否则回落成「恢复出来的待续播位置」—— 恢复后引擎是闲置、
    /// 位置为 0，用户可能连队列都没动过就退出了，不这么做会把续播位置冲成 0。
    /// 读引擎的位置而不是播放条视图模型的进度：后者被拖动状态挡着，方向也是视图模型 ← 引擎，
    /// 反过来会成环。
    /// </remarks>
    private double CurrentPositionSeconds()
    {
        if (Queue.Current is not { } current) { return 0; }

        if (CurrentTrack is { } playing && playing.Id == current.Id
            && _engine.State is PlaybackState.Playing or PlaybackState.Paused or PlaybackState.Loading)
        {
            return _engine.Position.TotalSeconds;
        }

        return current.Id == _resumeTrackId ? _resumePosition.TotalSeconds : 0;
    }

    /// <summary>
    /// 请求落盘。<b>快照在调用线程（UI 线程）上抓</b>，计时器线程只负责序列化与写盘。
    /// </summary>
    /// <remarks>
    /// <b>为什么防抖而不是立刻写</b>：队列可能几百首，随机模式下每个「下一首」都会改队列，
    /// 快速连点会连着重建整份文件。攒 1 秒与窗口位置那套防抖是同一个数量级。
    /// 注意这与偏好设置「改了就立刻落盘」不同 —— 那些是低频改动，队列不是。
    /// </remarks>
    private void RequestPersist()
    {
        if (!_restoreEnabled || _disposed || _queueStore is null || _switchingScope) { return; }

        _pending = new PendingSnapshot(_scope, CaptureSnapshot());
        _persistTimer?.Change(SnapshotDebounce, Timeout.InfiniteTimeSpan);
    }

    private void FlushPending()
    {
        // 后来的快照覆盖先前的，所以只要最新那份就够了。
        var pending = Interlocked.Exchange(ref _pending, null);
        if (pending is null || _queueStore is not { } store) { return; }

        _ = Task.Run(() =>
        {
            try { store.Save(pending.Scope, pending.Snapshot); }
            catch (Exception ex) { _logger.LogWarning(ex, "保存播放队列失败"); }
        });
    }

    private void OnQueueChangedForPersist(object? sender, EventArgs e) => RequestPersist();

    /// <summary>播到哪了的粗粒度检查点，供播放中长时间不切歌时兜底。</summary>
    /// <remarks>
    /// <b>按进度差而不是按墙钟</b>：暂停期间进度不动，就不会白写；拖动造成的回退也算一次变更
    /// （不落到新基准的话，拖动后要一直等追上原位置才会再次落盘）。
    /// </remarks>
    private void OnEnginePositionChangedForPersist(object? sender, PlaybackPositionChangedEventArgs e)
    {
        var delta = e.Position - _lastCheckpoint;
        if (delta >= TimeSpan.Zero && delta < PositionCheckpoint) { return; }

        _lastCheckpoint = e.Position;
        RequestPersist();
    }

    /// <summary>暂停、停止、回到闲置时补写一次 —— 这是用户最可能紧接着关应用的时刻。</summary>
    private void OnEngineStateChangedForPersist(object? sender, PlaybackStateChangedEventArgs e)
    {
        if (e.State is PlaybackState.Paused or PlaybackState.Stopped or PlaybackState.Idle)
        {
            RequestPersist();
        }
    }

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
                    // 第四个参数是起播位置：恢复出来的那首从上次停的地方接着放，其余为 null（从头播）。
                    // 走 start 参数而不是加载完再 Seek —— 文件还没打开时 seek 会踩到「位置属性不存在」，
                    // 引擎把状态砸成闲置并弹一条错误条。
                    await StartAsync(track, policy, playable.Source, ResumePointFor(track), null, ct).ConfigureAwait(true);
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

            // 续播意图到这一刻就用掉了：之后进度一律以引擎为准，不能让恢复时那个旧位置在播放结束、
            // 引擎回到闲置之后又被当成「当前进度」写进快照。
            _resumeTrackId = 0;
            _resumePosition = TimeSpan.Zero;
            _lastCheckpoint = TimeSpan.Zero;

            Started?.Invoke(this, new PlaybackStartedEventArgs(track, policy, source));
            QualityOptionsChanged?.Invoke(this, EventArgs.Empty);
            RecordHistory(track);
        }
        finally { _loadGate.Release(); }
    }

    /// <summary>
    /// 重新取当前曲目的指定音源，保留位置、暂停、队列及播放历史。
    /// </summary>
    /// <remarks>
    /// <b>只改这一次的档位，不动默认音质。</b> 默认音质（<see cref="PreferredQuality"/>）只由
    /// 设置页改写 —— 播放条上切一次档位不该影响下一首取源用哪个档位，详见 <c>docs/settings.md</c>。
    /// </remarks>
    public async Task SwitchQualityAsync(AudioQuality quality, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(quality)) { throw new ArgumentOutOfRangeException(nameof(quality)); }
        if (CurrentTrack is not { } track) { return; }
        if (CurrentPolicy?.IsAudition != false || !track.AvailableQualities.Contains(quality)) { return; }
        if (CurrentSource?.RequestedQuality == quality && CurrentSource.WasDowngraded == false) { return; }
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

    /// <summary>
    /// 只改「默认音质」这个偏好，**不动正在播放的那首**。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="SwitchQualityAsync"/> 的分工：那个是「现在就换档位」，会重新解析音源；
    /// 这个是设置页用的「以后按这个档位取源」，只落盘。
    /// 切歌路径（<see cref="SwitchQualityAsync"/> 内部）复用的也是本方法。
    /// </remarks>
    public void SetPreferredQuality(AudioQuality quality)
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
        _persistTimer?.Dispose();
        Queue.Changed -= OnQueueChangedForPersist;
        _engine.PositionChanged -= OnEnginePositionChangedForPersist;
        _engine.StateChanged -= OnEngineStateChangedForPersist;
        _engine.Ended -= OnEngineEnded;
        _engine.Failed -= OnEngineFailed;

        if (_account is not null)
        {
            _account.Changed -= OnAccountChanged;
        }
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

/// <summary>换了账号、队列跟着换了一份。</summary>
public sealed class QueueScopeSwitchedEventArgs(bool hadSomething) : EventArgs
{
    /// <summary>
    /// 换出时队列里有东西、或有歌正在播。
    /// </summary>
    /// <remarks>
    /// <b>外壳据此决定要不要弹提示</b>：启动时恢复会话也会走到「切号」这条路，而那一次的
    /// 旧作用域是空的 —— 每次都弹一条「播放队列已切换」会把正常的启动变成骚扰。
    /// </remarks>
    public bool HadSomething { get; } = hadSomething;
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

/// <summary>
/// 启动时从磁盘恢复出来的「上次停在这」。
/// </summary>
/// <remarks>
/// <b>刻意保持 internal。</b> 它带着 <see cref="Track"/>，而类型信息生成器会为 XAML 可见类型的公开
/// 属性生成激活代码，带 required 成员的 <see cref="Track"/> 造不出实例、会编译失败。
/// 只由播放条视图模型在构造时经 <c>PlaybackCoordinator.RestoredState</c> 读一次。
/// </remarks>
internal sealed record RestoredQueueState(Track Track, TimeSpan Position);
