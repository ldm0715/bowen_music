using Bowen.Core.Lyrics;
using Bowen.Core.Models;
using Bowen.Core.Models.Lyrics;
using Bowen.Core.Services.Abstractions;
using Bowen.WinUI.Playback;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bowen.WinUI.ViewModels;

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
        _coordinator.ScopeSwitched += OnScopeSwitched;
        _coordinator.QueueExhausted += OnQueueExhausted;
        _engine.PositionChanged += OnPositionChanged;

        // 同步读，和主题一样：歌词页要在第一帧就按选择排好，异步读会先闪一下译文。
        // 读到的是**默认值**，当前这一次从它起步，之后两者各走各的（见 SetDefaultShowTranslation）。
        var loaded = settings.Load().Normalized();
        DefaultShowTranslation = loaded.ShowTranslation;
        ShowTranslation = DefaultShowTranslation;
        FontFamily = loaded.FontFamily;
        MiniPlayerFontFamily = loaded.MiniPlayerFontFamily;
        FontSize = loaded.FontSize;
        Alignment = loaded.Alignment;
        HighlightColorArgb = loaded.HighlightColorArgb;
        LineGap = loaded.LineGap;
        SyncTranslationHighlight = loaded.SyncTranslationHighlight;
        TranslationFontScale = loaded.TranslationFontScale;
        TranslationLineGap = loaded.TranslationLineGap;
        EdgeBlurRatio = loaded.EdgeBlurRatio;
        BackgroundMode = loaded.BackgroundMode;
        HorizontalOffsetPercent = loaded.HorizontalOffsetPercent;
        VerticalOffsetPercent = loaded.VerticalOffsetPercent;
        _loading = false;

        // 启动时恢复出来的那一首也算「有歌可看」：播放条已经铺好了曲目，
        // 只是引擎里还没加载文件。此时点封面应当能进歌词页。
        HasTrack = PlaybackTrack is not null;
    }

    /// <summary>
    /// 能展示歌词的曲目：真正在播的那首，或启动 / 换号后恢复出来、还没开播的那首。
    /// </summary>
    /// <remarks>
    /// <b>只看 <see cref="PlaybackCoordinator.CurrentTrack"/> 会漏掉恢复态。</b> 那个字段要等
    /// 引擎加载成功才赋值，而重启后用户不点播放时引擎一直是闲置的 —— 歌词页会误报「还没有在播放的歌曲」。
    /// </remarks>
    private Track? PlaybackTrack => _coordinator.CurrentTrack ?? _coordinator.RestoredState?.Track;

    /// <summary>
    /// 歌词时间轴当前应处的位置。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>引擎里没有加载文件时取恢复出来的续播位置。</b> 重启后不点播放就进歌词页，
    /// 引擎是闲置的、<c>Position</c> 为 0；只按它来对齐会让歌词停在第一句，
    /// 而播放条上的进度明明是上次停下的位置。
    /// </para>
    /// <para>
    /// 恢复态一旦真正起播就会被协调器清掉（见 <c>PlaybackCoordinator.StartAsync</c>），
    /// 之后这里与进度条同源，一律读引擎。
    /// </para>
    /// </remarks>
    public TimeSpan TimelinePosition => _engine.State is PlaybackState.Idle or PlaybackState.Stopped
        ? _coordinator.RestoredState?.Position ?? TimeSpan.Zero
        : _engine.Position;

    /// <summary>
    /// 默认是否显示译文。<b>只由设置页改写</b>，歌词页那颗按钮动不了它。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="ShowTranslation"/> 是两个东西：这个决定「以后每次打开歌词页默认怎么显示」，
    /// 那个是「当前这一次显示不显示」。歌词页上临时关掉译文，不该把默认值也改掉。
    /// </remarks>
    public bool DefaultShowTranslation { get; private set; }

    /// <summary>
    /// 全屏歌词的字体族名；空串表示跟随渲染层默认。
    /// </summary>
    /// <remarks>
    /// 只读属性 + 显式 <see cref="SetFontFamily"/>：与 <see cref="DefaultShowTranslation"/> 同一条规矩，
    /// 写入口有副作用（落盘），不该被直接赋值。渲染层订阅 <c>PropertyChanged</c> 后重排。
    /// </remarks>
    public string FontFamily { get; private set; } = string.Empty;

    /// <summary>
    /// 小窗歌词的字体族名；空串表示跟随界面字体。
    /// </summary>
    /// <remarks>
    /// 小窗窗口订阅 <c>PropertyChanged</c> 后即时应用；默认空串保持原来的「跟随界面字体」行为。
    /// </remarks>
    public string MiniPlayerFontFamily { get; private set; } = string.Empty;

    /// <summary>全屏歌词字号，DIP。</summary>
    public double FontSize { get; private set; } = LyricsSettings.DefaultFontSize;

    /// <summary>全屏歌词的横向对齐方式。</summary>
    public LyricsAlignment Alignment { get; private set; } = LyricsAlignment.Left;

    /// <summary>已唱部分的高亮色，<c>0xAARRGGBB</c>。</summary>
    public uint HighlightColorArgb { get; private set; } = LyricsSettings.DefaultHighlightColor;

    /// <summary>行与行之间的额外间距，DIP。</summary>
    public double LineGap { get; private set; } = LyricsSettings.DefaultLineGap;

    /// <summary>译文是否跟随原文同步扫色。</summary>
    public bool SyncTranslationHighlight { get; private set; } = true;

    /// <summary>译文相对歌词字号的缩放，0.5–1。</summary>
    public double TranslationFontScale { get; private set; } = LyricsSettings.DefaultTranslationFontScale;

    /// <summary>译文与原文之间的间距，DIP。</summary>
    public double TranslationLineGap { get; private set; } = LyricsSettings.DefaultTranslationLineGap;

    /// <summary>视口上下边缘的模糊范围，占视口高度的比例。</summary>
    public double EdgeBlurRatio { get; private set; } = LyricsSettings.DefaultEdgeBlurRatio;

    /// <summary>全屏歌词页的背景来源。</summary>
    public LyricsBackgroundMode BackgroundMode { get; private set; } = LyricsBackgroundMode.CoverBlur;

    /// <summary>在对齐结果之上的水平偏移，占歌词区域宽度的百分比。</summary>
    public double HorizontalOffsetPercent { get; private set; } = LyricsSettings.DefaultOffsetPercent;

    /// <summary>垂直偏移，占歌词区域高度的百分比；正数向下。</summary>
    public double VerticalOffsetPercent { get; private set; } = LyricsSettings.DefaultOffsetPercent;

    /// <summary>
    /// 设置页或歌词页面板改歌词字体：落盘，并发通知让渲染层重排当前文档（不必重启）。
    /// </summary>
    public void SetFontFamily(string fontFamily)
    {
        fontFamily ??= string.Empty;
        if (FontFamily == fontFamily)
        {
            return;
        }

        FontFamily = fontFamily;
        OnPropertyChanged(nameof(FontFamily));
        Persist();
    }

    /// <summary>
    /// 设置页改小窗歌词字体：落盘，小窗窗口订阅后即时重排。
    /// </summary>
    public void SetMiniPlayerFontFamily(string fontFamily)
    {
        fontFamily ??= string.Empty;
        if (MiniPlayerFontFamily == fontFamily)
        {
            return;
        }

        MiniPlayerFontFamily = fontFamily;
        OnPropertyChanged(nameof(MiniPlayerFontFamily));
        Persist();
    }

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
        Persist();

        // 同步应用到当前这次，否则用户改完默认值还要手动去歌词页再按一下才看到效果。
        ShowTranslation = value;
    }

    /// <summary>歌词页改字号：夹取后落盘，渲染层订阅后整份重排。</summary>
    public void SetFontSize(double fontSize)
    {
        fontSize = double.IsFinite(fontSize) && fontSize > 0
            ? Math.Clamp(fontSize, LyricsSettings.MinimumFontSize, LyricsSettings.MaximumFontSize)
            : LyricsSettings.DefaultFontSize;
        if (Math.Abs(FontSize - fontSize) < 0.01)
        {
            return;
        }

        FontSize = fontSize;
        OnPropertyChanged(nameof(FontSize));
        Persist();
    }

    /// <summary>歌词页改对齐：落盘，渲染层订阅后整份重排。</summary>
    public void SetAlignment(LyricsAlignment alignment)
    {
        if (!Enum.IsDefined(alignment))
        {
            alignment = LyricsAlignment.Left;
        }

        if (Alignment == alignment)
        {
            return;
        }

        Alignment = alignment;
        OnPropertyChanged(nameof(Alignment));
        Persist();
    }

    /// <summary>歌词页改高亮色：alpha 归一为不透明后落盘。</summary>
    public void SetHighlightColor(uint argb)
    {
        argb = argb == 0 ? LyricsSettings.DefaultHighlightColor : argb | 0xFF000000;
        if (HighlightColorArgb == argb)
        {
            return;
        }

        HighlightColorArgb = argb;
        OnPropertyChanged(nameof(HighlightColorArgb));
        Persist();
    }

    /// <summary>歌词页改行距：夹到 0–64 DIP 后落盘。</summary>
    public void SetLineGap(double lineGap)
    {
        lineGap = double.IsFinite(lineGap)
            ? Math.Clamp(lineGap, LyricsSettings.MinimumLineGap, LyricsSettings.MaximumLineGap)
            : LyricsSettings.DefaultLineGap;
        if (Math.Abs(LineGap - lineGap) < 0.01)
        {
            return;
        }

        LineGap = lineGap;
        OnPropertyChanged(nameof(LineGap));
        Persist();
    }

    /// <summary>歌词页改译文同步高亮：立即落盘，只影响绘制，不重排。</summary>
    public void SetSyncTranslationHighlight(bool enabled)
    {
        if (SyncTranslationHighlight == enabled)
        {
            return;
        }

        SyncTranslationHighlight = enabled;
        OnPropertyChanged(nameof(SyncTranslationHighlight));
        Persist();
    }

    /// <summary>歌词页改译文大小：夹到 50%–100% 后落盘。</summary>
    public void SetTranslationFontScale(double scale)
    {
        scale = double.IsFinite(scale) && scale > 0
            ? Math.Clamp(scale, LyricsSettings.MinimumTranslationFontScale, LyricsSettings.MaximumTranslationFontScale)
            : LyricsSettings.DefaultTranslationFontScale;
        if (Math.Abs(TranslationFontScale - scale) < 0.001)
        {
            return;
        }

        TranslationFontScale = scale;
        OnPropertyChanged(nameof(TranslationFontScale));
        Persist();
    }

    /// <summary>歌词页改译文间距：夹到 0–32 DIP 后落盘。</summary>
    public void SetTranslationLineGap(double gap)
    {
        gap = double.IsFinite(gap)
            ? Math.Clamp(gap, LyricsSettings.MinimumTranslationLineGap, LyricsSettings.MaximumTranslationLineGap)
            : LyricsSettings.DefaultTranslationLineGap;
        if (Math.Abs(TranslationLineGap - gap) < 0.01)
        {
            return;
        }

        TranslationLineGap = gap;
        OnPropertyChanged(nameof(TranslationLineGap));
        Persist();
    }

    /// <summary>歌词页改边缘模糊范围：夹到 0%–50% 后落盘。</summary>
    public void SetEdgeBlurRatio(double ratio)
    {
        ratio = double.IsFinite(ratio)
            ? Math.Clamp(ratio, LyricsSettings.MinimumEdgeBlurRatio, LyricsSettings.MaximumEdgeBlurRatio)
            : LyricsSettings.DefaultEdgeBlurRatio;
        if (Math.Abs(EdgeBlurRatio - ratio) < 0.001)
        {
            return;
        }

        EdgeBlurRatio = ratio;
        OnPropertyChanged(nameof(EdgeBlurRatio));
        Persist();
    }

    /// <summary>歌词页改背景来源：无效值退回封面模糊后落盘。</summary>
    public void SetBackgroundMode(LyricsBackgroundMode mode)
    {
        if (!Enum.IsDefined(mode)) mode = LyricsBackgroundMode.CoverBlur;
        if (BackgroundMode == mode)
        {
            return;
        }

        BackgroundMode = mode;
        OnPropertyChanged(nameof(BackgroundMode));
        Persist();
    }

    /// <summary>歌词页改水平偏移：夹到 -30%–30% 后落盘。</summary>
    public void SetHorizontalOffsetPercent(double percent)
    {
        percent = double.IsFinite(percent)
            ? Math.Clamp(percent, LyricsSettings.MinimumHorizontalOffsetPercent, LyricsSettings.MaximumHorizontalOffsetPercent)
            : LyricsSettings.DefaultOffsetPercent;
        if (Math.Abs(HorizontalOffsetPercent - percent) < 0.01)
        {
            return;
        }

        HorizontalOffsetPercent = percent;
        OnPropertyChanged(nameof(HorizontalOffsetPercent));
        Persist();
    }

    /// <summary>歌词页改垂直偏移：夹到 -25%–25% 后落盘。</summary>
    public void SetVerticalOffsetPercent(double percent)
    {
        percent = double.IsFinite(percent)
            ? Math.Clamp(percent, LyricsSettings.MinimumVerticalOffsetPercent, LyricsSettings.MaximumVerticalOffsetPercent)
            : LyricsSettings.DefaultOffsetPercent;
        if (Math.Abs(VerticalOffsetPercent - percent) < 0.01)
        {
            return;
        }

        VerticalOffsetPercent = percent;
        OnPropertyChanged(nameof(VerticalOffsetPercent));
        Persist();
    }

    /// <summary>
    /// 写回整份偏好。新增字段只改这一处，避免某个 setter 漏写。
    /// </summary>
    private void Persist() => _settings.Save(new LyricsSettings
    {
        ShowTranslation = DefaultShowTranslation,
        FontFamily = FontFamily,
        MiniPlayerFontFamily = MiniPlayerFontFamily,
        FontSize = FontSize,
        Alignment = Alignment,
        HighlightColorArgb = HighlightColorArgb,
        LineGap = LineGap,
        SyncTranslationHighlight = SyncTranslationHighlight,
        TranslationFontScale = TranslationFontScale,
        TranslationLineGap = TranslationLineGap,
        EdgeBlurRatio = EdgeBlurRatio,
        BackgroundMode = BackgroundMode,
        HorizontalOffsetPercent = HorizontalOffsetPercent,
        VerticalOffsetPercent = VerticalOffsetPercent,
    });

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
    private Task RetryAsync() => PlaybackTrack is { } track
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
            ? _engine.SeekAsync(StartForLine(lineIndex))
            : Task.CompletedTask;

    /// <summary>
    /// 点击某一行后要跳到的位置。
    /// </summary>
    /// <remarks>
    /// 译文行显示在上一句下面，自带的时间戳却是下一句的行首，所以要先映射回原文行，
    /// 否则点译文会跳到下一句（判据见 <see cref="LyricDocument.SourceLineIndex"/>）。
    /// </remarks>
    public TimeSpan StartForLine(int lineIndex)
    {
        var source = Document.SourceLineIndex(lineIndex);
        return source >= 0 ? Document.Lines[source].Start : TimeSpan.Zero;
    }

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
        if (PlaybackTrack is not { } track)
        {
            Clear();
            HasTrack = false;
            StatusText = "还没有在播放的歌曲";
            return;
        }

        if (_loadedTrackId == track.Id)
        {
            SetCurrent(-1);
            UpdateCurrentLine(TimelinePosition);
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
        // 空行（译文占位、段落分隔、结尾空行）没有可显示的内容，投影时一并剔掉，不占行高。
        var clean = _fullDocument.WithoutBlankLines();

        // 先问有没有译文，再决定投影成哪一份 —— 顺序反了就永远问不出 true。
        HasTranslation = clean.HasTranslation;

        Document = ShowTranslation ? clean : clean.WithoutTranslations();
        CurrentIndex = -1;
        UpdateCurrentLine(TimelinePosition);
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

    /// <summary>
    /// 换了账号：恢复出来的那一首跟着换，歌词页开着就重新取词。
    /// </summary>
    /// <remarks>
    /// 新作用域为空时 <see cref="PlaybackTrack"/> 为 <c>null</c>，<see cref="HasTrack"/> 跟着变假，
    /// 播放条上的封面与桌面歌词按钮才会禁用 —— 它们直接绑这一位。
    /// </remarks>
    private void OnScopeSwitched(object? sender, QueueScopeSwitchedEventArgs e)
    {
        HasTrack = PlaybackTrack is not null;

        if (IsActive)
        {
            _ = RefreshAsync();
        }
    }

    /// <summary>
    /// 队列走到头了。队列已经被清空时把歌词一并收掉。
    /// </summary>
    /// <remarks>
    /// <b>只在队列为空时清。</b> 顺序播放放完最后一首时队列里还有歌，播放条可以按播放整队重来，
    /// 歌词也该留着；而「清空队列 → 当前这首放完」之后没有任何可播的曲目，
    /// 播放条会回到空白态（见 <c>PlayerViewModel.OnQueueExhausted</c>），歌词不能还挂着旧的。
    /// </remarks>
    private void OnQueueExhausted(object? sender, EventArgs e)
    {
        if (_coordinator.Queue.Count > 0 || !HasTrack)
        {
            return;
        }

        HasTrack = false;
        Clear();
        StatusText = "还没有在播放的歌曲";
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
