using Bodian.Core.Lyrics;
using Bodian.Core.Models;
using Bodian.Core.Models.Lyrics;
using Bodian.Core.Services.Abstractions;
using Bodian.WinUI.Playback;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 歌词的装载与当前行。**单例**，与播放条一样常驻。
/// </summary>
/// <remarks>
/// <para>
/// <b>没有消费者就不取词。</b> 没人看的时候为每一首播过的歌都发一次歌词请求，属于把别人的服务当压测。
/// 「有消费者」有两个来源：沉浸歌词页可见（<see cref="IsOpen"/>），或桌面歌词条开着
/// （<see cref="IsDesktopLyricsOpen"/>）。任一为真就取词，换歌就重载。
/// </para>
/// <para>
/// 歌词走 <see cref="LyricRepository"/>：同一首歌重播、两处反复开关都不会重复请求。
/// </para>
/// <para>
/// <b>当前行不是「跟随界面，是跟随引擎」</b> —— 位置一律取 <see cref="IPlaybackService.Position"/>，
/// 与进度条同源。拖动进度条之后两者也一起跳，不会出现歌词和进度对不上的情况。
/// </para>
/// </remarks>
public sealed partial class LyricsViewModel : ObservableObject
{
    private readonly LyricRepository _repository;
    private readonly PlaybackCoordinator _coordinator;
    private readonly IPlaybackService _engine;
    private readonly ILyricsSettingsStore _settings;
    private readonly ILogger<LyricsViewModel> _logger;

    /// <summary>已经装载歌词的曲目 id；<c>0</c> 表示还没装载过。</summary>
    private long _loadedTrackId;

    /// <summary>装载代次，用来丢弃迟到的结果（切歌比取词快时会发生）。</summary>
    private int _generation;

    /// <summary>
    /// 未过滤的完整文档。
    /// </summary>
    /// <remarks>
    /// <b>取词只发生一次，<see cref="Document"/> 是它的一个视图。</b>
    /// 切「显示译文」是纯本地的剔除，不该再走一趟 <see cref="LyricRepository"/>，
    /// 所以原样留一份在这里；两处 <c>Clear</c> 时一起清掉。
    /// </remarks>
    private LyricDocument _fullDocument = LyricDocument.Empty;

    /// <summary>装载期间不许把刚读出来的设置再写回去。</summary>
    private bool _loading = true;

    public LyricsViewModel(
        LyricRepository repository,
        PlaybackCoordinator coordinator,
        IPlaybackService engine,
        ILyricsSettingsStore settings,
        ILogger<LyricsViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(settings);

        _repository = repository;
        _coordinator = coordinator;
        _engine = engine;
        _settings = settings;
        _logger = logger ?? NullLogger<LyricsViewModel>.Instance;

        _coordinator.Started += OnStarted;
        _engine.PositionChanged += OnPositionChanged;

        // 同步读，和主题一样：歌词页要在第一帧就按选择排好，异步读会先闪一下译文。
        // 读到的是**默认值**，当前这一次从它起步，之后两者各走各的（见 SetDefaultShowTranslation）。
        DefaultShowTranslation = settings.Load().Normalized().ShowTranslation;
        ShowTranslation = DefaultShowTranslation;
        _loading = false;
    }

    /// <summary>
    /// 默认是否显示译文。<b>只由设置页改写</b>，歌词页那颗按钮动不了它。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="ShowTranslation"/> 是两个东西：这个决定「以后每次打开歌词页默认怎么显示」，
    /// 那个是「当前这一次显示不显示」。歌词页上临时关掉译文，不该把默认值也改掉。
    /// </remarks>
    public bool DefaultShowTranslation { get; private set; }

    /// <summary>
    /// 设置页改默认值：落盘，并让当前这一次也立刻跟上（不必重启）。
    /// </summary>
    public void SetDefaultShowTranslation(bool value)
    {
        if (DefaultShowTranslation == value)
        {
            return;
        }

        DefaultShowTranslation = value;
        OnPropertyChanged(nameof(DefaultShowTranslation));
        _settings.Save(new LyricsSettings { ShowTranslation = value });

        // 同步应用到当前这次，否则用户改完默认值还要手动去歌词页再按一下才看到效果。
        ShowTranslation = value;
    }

    /// <summary>
    /// 当前装载的歌词文档。
    /// </summary>
    /// <remarks>
    /// <b>渲染层要的是它，不是「行文本 + 是否当前行」。</b> 逐字扫光需要音节时间轴与
    /// <see cref="LyricKind"/>，把那些信息压成一行文本就再也拿不回来了。
    /// 变化会触发 <c>PropertyChanged</c>，Win2D 宿主据此把新文档投到渲染线程。
    /// </remarks>
    [ObservableProperty]
    public partial LyricDocument Document { get; set; } = LyricDocument.Empty;

    /// <summary>
    /// 沉浸歌词页是否<b>活跃</b>（当前显示的就是它）。
    /// </summary>
    /// <remarks>
    /// 由 <c>LyricsPage</c> 通过 <c>INavigationAware</c> 设置，不靠控件生命周期事件。
    /// 置为 <c>true</c> 会触发取词。
    /// </remarks>
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    /// <summary>
    /// 桌面歌词条是否开着。与 <see cref="IsOpen"/> <b>并列的第二个取词来源</b>。
    /// </summary>
    /// <remarks>
    /// 两处都能看歌词，互不依赖：只开桌面歌词条时也要取词，只进歌词页时也一样。
    /// 由 <c>DesktopLyricsViewModel</c> 驱动。
    /// </remarks>
    [ObservableProperty]
    public partial bool IsDesktopLyricsOpen { get; set; }

    /// <summary>
    /// 小窗开着。与上面两个<b>并列的第三个取词来源</b>。
    /// </summary>
    /// <remarks>
    /// 小窗在播放时那一行位置显示的就是当前歌词，所以它也需要一份文档 ——
    /// 跟前两处一样，只开小窗时同样要取词。由 <c>MiniPlayerViewModel</c> 驱动。
    /// </remarks>
    [ObservableProperty]
    public partial bool IsMiniPlayerOpen { get; set; }

    /// <summary>还有没有人需要歌词。它是「取不取词」的唯一判据。</summary>
    private bool IsActive => IsOpen || IsDesktopLyricsOpen || IsMiniPlayerOpen;

    /// <summary>当前有没有在播的歌。没有时歌词按钮是灰的。</summary>
    [ObservableProperty]
    public partial bool HasTrack { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; } = "";

    [ObservableProperty]
    public partial string Subtitle { get; set; } = "";

    /// <summary>空态与失败态的提示文案。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusText))]
    public partial string StatusText { get; set; } = "";

    public bool HasStatusText => StatusText.Length > 0;

    /// <summary>取词失败 —— 只有这一种情况给「重试」按钮，没歌词是正常结果，重试没有意义。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusText))]
    public partial bool LoadFailed { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>
    /// 歌词页是否显示译文。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>只做剔除，不重新取词。</b> 译文本来就和原文在同一份内容里（外文歌的原文行与译文行
    /// 成对出现），解析时已经分好（<see cref="LyricLine.IsTranslation"/>）。这里改的只是
    /// <see cref="Document"/> 这个视图。
    /// </para>
    /// <para>
    /// <b>它只影响全屏歌词页</b>，桌面歌词条不读这一位 —— 那条是单行/双行布局，加译文要重排。
    /// </para>
    /// </remarks>
    [ObservableProperty]
    public partial bool ShowTranslation { get; set; }

    /// <summary>
    /// 当前这首有没有译文行。没有时「显示译文」那颗开关<b>禁用</b>。
    /// </summary>
    /// <remarks>
    /// <b>由文档本身判，不去问 <c>lrc_info</c>。</b> 渲染什么就问什么 ——
    /// 接口说有译文轨、内容里却没有这种不一致坑不到我们；中文歌与逐行版自然为 <c>false</c>。
    /// 它读的是那个未过滤的 <see cref="_fullDocument"/>，见 <see cref="SyncDocument"/>。
    /// </remarks>
    [ObservableProperty]
    public partial bool HasTranslation { get; set; }

    /// <summary>当前行的下标；不在任何行里时为 <c>-1</c>。</summary>
    [ObservableProperty]
    public partial int CurrentIndex { get; set; } = -1;

    /// <summary>
    /// 当前行的文本；没有歌词、或还停在第一行之前（唱片头空白）时是空串。
    /// </summary>
    /// <remarks>
    /// <b>小窗那一行歌词读它。</b> 行下标跟随引擎低频上报，不是每帧更新，
    /// 所以这里当普通绑定属性用不会把高频刷新带进界面。
    /// </remarks>
    public string CurrentLineText => CurrentIndex >= 0 && CurrentIndex < Document.Lines.Count
        ? Document.Lines[CurrentIndex].Text
        : "";

    partial void OnCurrentIndexChanged(int value) => OnPropertyChanged(nameof(CurrentLineText));

    partial void OnDocumentChanged(LyricDocument value) => OnPropertyChanged(nameof(CurrentLineText));

    [RelayCommand]
    private Task RetryAsync() => _coordinator.CurrentTrack is { } track
        ? LoadAsync(track)
        : Task.CompletedTask;

    /// <summary>点某一行跳到那一句。参数是行下标（Win2D 那边命中测试拿到的就是下标）。</summary>
    /// <remarks>
    /// <b>引擎里没有加载文件时不跳。</b> 与 <c>PlayerViewModel.SeekToAsync</c> 同一条理由：
    /// 此时 mpv 的 <c>time-pos</c> 不存在，设它会抛 <c>property unavailable</c> 并弹红条。
    /// </remarks>
    [RelayCommand]
    private Task SeekToLineAsync(int lineIndex)
        => lineIndex >= 0 && lineIndex < Document.Lines.Count
            && _engine.State is not (PlaybackState.Idle or PlaybackState.Stopped)
            ? _engine.SeekAsync(Document.Lines[lineIndex].Start)
            : Task.CompletedTask;

    /// <remarks>
    /// <b>不落盘。</b> 歌词页那颗译文按钮改的是「当前这一次」，默认值只由设置页改写 ——
    /// 在歌词页临时关掉译文，下次打开仍按默认值来。
    /// </remarks>
    partial void OnShowTranslationChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        SyncDocument();
    }

    /// <remarks>
    /// <b>每次打开歌词页都从默认值起步。</b> 「默认」的语义就是「打开时用哪个」——
    /// 只在构造函数里读一次的话，应用启动那一刻的值会一直粘到进程结束，
    /// 中途在设置页改了默认值，已经打开过的这次也不会重新按默认值来。
    /// </remarks>
    partial void OnIsOpenChanged(bool value)
    {
        if (value)
        {
            ShowTranslation = DefaultShowTranslation;
        }

        OnActivationChanged();
    }

    partial void OnIsDesktopLyricsOpenChanged(bool value) => OnActivationChanged();

    partial void OnIsMiniPlayerOpenChanged(bool value) => OnActivationChanged();

    /// <summary>三个来源任一被打开时刷新一次；都关掉时不动已装载的内容。</summary>
    private void OnActivationChanged()
    {
        if (IsActive)
        {
            _ = RefreshAsync();
        }
    }

    // ── 数据 ────────────────────────────────────────────────────────────────

    /// <summary>打开面板时调用：已装载就只是把高亮对齐，没装载才取词。</summary>
    private async Task RefreshAsync()
    {
        if (_coordinator.CurrentTrack is not { } track)
        {
            Clear();
            StatusText = "还没有在播放的歌曲";
            return;
        }

        if (_loadedTrackId == track.Id)
        {
            SetCurrent(-1);
            UpdateCurrentLine(_engine.Position);
            return;
        }

        await LoadAsync(track).ConfigureAwait(true);
    }

    private async Task LoadAsync(Track track)
    {
        var generation = ++_generation;

        IsLoading = true;
        LoadFailed = false;
        StatusText = "";
        Title = track.Title;
        Subtitle = track.ArtistText;

        try
        {
            var document = await _repository.GetAsync(track).ConfigureAwait(true);

            if (generation != _generation)
            {
                return;   // 切歌了，这份结果已经过期
            }

            Apply(track, document);
        }
        catch (Exception ex)
        {
            if (generation != _generation)
            {
                return;
            }

            _logger.LogWarning(ex, "取歌词失败：{Title}", track.Title);

            Clear();
            Title = track.Title;
            Subtitle = track.ArtistText;
            LoadFailed = true;
            StatusText = "歌词加载失败，检查网络后重试";
        }
        finally
        {
            if (generation == _generation)
            {
                IsLoading = false;
            }
        }
    }

    private void Apply(Track track, LyricDocument document)
    {
        _fullDocument = document;
        _loadedTrackId = track.Id;
        HasTrack = true;

        // 空文档不是错误：服务端对没有歌词的歌返回空串，业务码仍是 200。
        StatusText = document.IsEmpty ? "这首歌还没有歌词" : "";

        SyncDocument();
    }

    /// <summary>
    /// 按当前设置把完整文档投影成 <see cref="Document"/>，并把高亮对齐回来。
    /// </summary>
    /// <remarks>
    /// 剔除译文会改变行下标，所以每次都要重新算 <see cref="CurrentIndex"/>；
    /// 位置一律取引擎上报的值，与进度条同源。
    /// </remarks>
    private void SyncDocument()
    {
        // 先问完整文档有没有译文，再决定投影成哪一份 —— 顺序反了就永远问不出 true。
        HasTranslation = _fullDocument.HasTranslation;

        Document = ShowTranslation ? _fullDocument : _fullDocument.WithoutTranslations();
        CurrentIndex = -1;
        UpdateCurrentLine(_engine.Position);
    }

    private void Clear()
    {
        _fullDocument = LyricDocument.Empty;
        Document = LyricDocument.Empty;
        HasTranslation = false;
        _loadedTrackId = 0;
        CurrentIndex = -1;
    }

    // ── 事件 ────────────────────────────────────────────────────────────────

    private void OnStarted(object? sender, PlaybackStartedEventArgs e)
    {
        HasTrack = true;

        if (IsActive)
        {
            _ = LoadAsync(e.Track);
            return;
        }

        // 两处都没开就先不取词，把「已装载」作废，等打开时再按当前曲目加载。
        _loadedTrackId = 0;
    }

    private void OnPositionChanged(object? sender, PlaybackPositionChangedEventArgs e)
        => UpdateCurrentLine(e.Position);

    private void UpdateCurrentLine(TimeSpan position)
    {
        if (Document.IsEmpty)
        {
            return;
        }

        var index = Document.IndexOfLineAt(position);

        if (index != CurrentIndex)
        {
            SetCurrent(index);
        }
    }

    /// <summary>
    /// 记录当前行下标。
    /// </summary>
    /// <remarks>
    /// <b>两处渲染路径都不读它。</b> 这一位跟随引擎低频上报，给页面之外的状态展示使用。
    /// 沉浸歌词页与桌面歌词各自通过 <c>LyricsPlaybackClock</c> 查询文档时间轴和逐字进度，
    /// 不让低频行下标与每帧渲染同时驱动高亮。
    /// </remarks>
    private void SetCurrent(int index)
    {
        CurrentIndex = index;
    }
}
