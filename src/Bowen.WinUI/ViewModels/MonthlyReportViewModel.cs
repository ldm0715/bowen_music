using System.Collections.ObjectModel;
using System.Globalization;
using Bowen.Core.Api;
using Bowen.Core.Models;
using Bowen.Core.Models.AudioMemory;
using Bowen.WinUI.Playback;
using Bowen.WinUI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Xaml.Media;

namespace Bowen.WinUI.ViewModels;

/// <summary>
/// 音波回忆的「听歌月报」页签。
/// </summary>
/// <remarks>
/// 首次进入先用 <c>summary</c> 的 <c>monthDesc</c> 定位最近有数据的月份，再读
/// <c>info</c>。之后月份下拉的每次选择都按 <c>YYYYMM</c> 缓存，切回同一个月不再请求。
/// </remarks>
public sealed partial class MonthlyReportViewModel : ObservableObject, IDisposable
{
    private const int VisibleMonthCount = 12;

    private readonly IBodianApi _api;
    private readonly PlaybackCoordinator _coordinator;
    private readonly INoticeSink _notice;
    private readonly ILogger<MonthlyReportViewModel> _logger;

    private readonly Dictionary<MonthPeriod, MonthReport?> _cache = [];
    private CancellationTokenSource? _request;
    private int _generation;
    private bool _loaded;
    private bool _suppressSelection;
    private bool _disposed;

    public MonthlyReportViewModel(
        IBodianApi api,
        PlaybackCoordinator coordinator,
        INoticeSink notice,
        ILogger<MonthlyReportViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(notice);

        _api = api;
        _coordinator = coordinator;
        _notice = notice;
        _logger = logger ?? NullLogger<MonthlyReportViewModel>.Instance;

        RebuildMonths(resetSelection: true);
    }

    /// <summary>月份下拉，默认最近 12 个月。</summary>
    public ObservableCollection<MonthOption> Months { get; } = [];

    /// <summary>第 4 名及以后。前三名由擂台卡单独展示。</summary>
    public ObservableCollection<ReportTrackRow> TopRest { get; } = [];

    public ObservableCollection<AlbumCoverItem> AlbumCovers { get; } = [];

    [ObservableProperty]
    public partial MonthOption? SelectedMonth { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(ShowEmpty))]
    [NotifyPropertyChangedFor(nameof(ShowError))]
    [NotifyPropertyChangedFor(nameof(CanCreatePlaylist))]
    [NotifyPropertyChangedFor(nameof(CanExport))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(ShowEmpty))]
    [NotifyPropertyChangedFor(nameof(ShowError))]
    public partial bool HasError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreatePlaylist))]
    public partial bool IsCreatingPlaylist { get; set; }

    private MonthReport? _report;

    /// <summary>当前月份的完整月报。无数据或加载失败时为 <c>null</c>。</summary>
    public MonthReport? Report
    {
        get => _report;
        private set
        {
            if (!SetProperty(ref _report, value))
            {
                return;
            }

            RaiseReportChanged();
        }
    }

    public bool HasReport => Report is not null;

    public bool HasTopRest => TopRest.Count > 0;

    public bool HasAlbumCovers => AlbumCovers.Count > 0;

    /// <summary>擂台上的第 1 / 2 / 3 名。不足三名时对应属性为 <c>null</c>。</summary>
    public ReportTrackRow? PodiumFirst { get; private set; }

    public ReportTrackRow? PodiumSecond { get; private set; }

    public ReportTrackRow? PodiumThird { get; private set; }

    public bool HasPodium => PodiumFirst is not null;

    public bool HasPodiumFirst => PodiumFirst is not null;

    public bool HasPodiumSecond => PodiumSecond is not null;

    public bool HasPodiumThird => PodiumThird is not null;

    public bool CanCreatePlaylist => HasReport && !IsCreatingPlaylist && !IsBusy;

    public bool CanExport => HasReport && !IsBusy;

    public bool ShowEmpty => !HasReport && !HasError && !IsBusy;

    public bool ShowError => HasError && !IsBusy;

    public string StatusText
    {
        get
        {
            if (IsBusy)
            {
                return "正在加载…";
            }

            if (HasError)
            {
                return "加载失败";
            }

            return HasReport ? "" : "暂无月报";
        }
    }

    /// <summary>空数据月与未生成月的提示文案。</summary>
    public string EmptyText { get; private set; } = "";

    public string ListenTotalText => Report?.ListenTotalMinutes is { Length: > 0 } value
        ? long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            ? $"{minutes} 分钟"
            : value
        : "—";

    public string PlayCountText => Report?.PlayCount is { } count
        ? $"{CommentCountLabel.Format(count)} 次"
        : "—";

    public string SongCountText => Report?.SongCount is { } count
        ? $"{CommentCountLabel.Format(count)} 首"
        : "—";

    public string ArtistNameText => Report?.ArtistName is { Length: > 0 } name ? name : "—";

    public string ArtistContentText => Report?.ArtistContent ?? "";

    public string ArtistMusicNameText => Report?.ArtistMusicName ?? "";

    /// <summary>听歌时长环比，拆成「比上月」与「+12%」两段，供变化卡排版。</summary>
    public string ListenChangeLabel => SplitChange(Report?.ListenChain).Label;

    public string ListenChangeValueText => HasListenChange
        ? SplitChange(Report?.ListenChain).Value
        : "—";

    public bool HasListenChange => SplitChange(Report?.ListenChain).Value.Length > 0;

    /// <summary>服务端有时只给「比上月疯狂暴涨」这类描述，没有数字。</summary>
    public bool ListenChangeIsNumeric => HasListenChange
        && ListenChangeValueText.Any(char.IsAsciiDigit)
        && ListenChangeValueText.Contains('%');

    public string ListenChangeDescription => Report?.ListenChain is { Length: > 0 } text
        ? text.Replace("\n", "", StringComparison.Ordinal).Trim()
        : "";

    public bool HasListenChangeDescription =>
        !ListenChangeIsNumeric && ListenChangeDescription.Length > 0;

    public string ListenChangeArrow => HasListenChange
        ? ListenChangeValueText.StartsWith('-') ? "↓" : "↑"
        : "";

    public string PlayChangeText => Report?.PlayChain is { Length: > 0 } text
        ? text.Replace("\n", "", StringComparison.Ordinal)
        : "";

    public bool HasPlayChange => PlayChangeText.Length > 0;

    public ImageSource? ArtistCoverImage => Formats.CoverSource(Report?.ArtistCover);

    public ImageSource? ArtistMusicCoverImage => Formats.CoverSource(Report?.ArtistMusicCover);

    /// <summary>本月听歌最长的一天。服务端文案形如「10-02与音乐共度81分钟」。</summary>
    public string LongestDayDateText => FormatDay(SplitLongestDay(Report?.ListenContent).Date);

    public string LongestDayMinutesText => SplitLongestDay(Report?.ListenContent).Minutes;

    public bool HasLongestDay => LongestDayDateText.Length > 0 || LongestDayMinutesText.Length > 0;

    /// <summary>
    /// 服务端有时不给 <c>listenContent</c>，这时用「听得最久的一首」补位；
    /// 连曲名也没有就显示破折号，保证卡片不消失、版面不缺一块。
    /// </summary>
    public bool HasListenTopTrack => Report?.ListenTopTrackTitle is { Length: > 0 };

    public string LongestDayCardLabel => HasLongestDay
        ? "最长一天"
        : HasListenTopTrack ? "听得最久" : "最长一天";

    public string LongestDayCardValue => HasLongestDay
        ? LongestDayMinutesText
        : HasListenTopTrack ? Report!.ListenTopTrackTitle! : "—";

    public string LongestDayCardSubtitle => HasLongestDay
        ? LongestDayDateText
        : HasListenTopTrack ? "本月单曲时长最多" : "本月没有单日峰值";

    /// <summary>本月播放次数最多的一首。与擂台冠军是同一首。</summary>
    public bool HasMostPlayedSong => PodiumFirst is not null;

    public string MostPlayedSongTitle => PodiumFirst?.Source.Title ?? "";

    public string MostPlayedSongPlayCountText => PodiumFirst?.PlayCountLabel ?? "";

    /// <summary>没有「单曲最多」卡时，让补位卡占满整行。</summary>
    public int LongestDayCardColumnSpan => HasMostPlayedSong ? 1 : 2;

    public bool HasArtist => Report?.ArtistName is { Length: > 0 } || Report?.ArtistCover is not null;

    public bool HasArtistMusic => Report?.ArtistMusicName is { Length: > 0 } || Report?.ArtistMusicCover is not null;

    /// <summary>第一次切到本页签时调。已经加载过就什么都不做。</summary>
    public async Task EnsureLoadedAsync()
    {
        if (_loaded || IsBusy)
        {
            return;
        }

        _loaded = true;
        IsBusy = true;

        var generation = _generation;
        MonthPeriod? target = null;

        try
        {
            var summary = await _api.GetMonthReportSummaryAsync().ConfigureAwait(true);

            // 等请求期间换号或重置过，这一份结果作废。
            if (generation != _generation)
            {
                return;
            }

            if (summary?.MonthDesc is { Length: > 0 } description
                && MonthPeriod.TryParseMonthDesc(description, Today(), out var parsed))
            {
                target = parsed;
            }

            if (generation != _generation)
            {
                return;
            }

            var period = target ?? CurrentMonth();
            var option = EnsureMonthOption(period);

            _suppressSelection = true;
            SelectedMonth = option;
            _suppressSelection = false;

            await LoadMonthAsync(period).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // 换号或重置取消，静默。
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "月报预览读取失败，退回当前月");
        }
        finally
        {
            if (generation == _generation)
            {
                IsBusy = false;
            }
        }
    }

    partial void OnSelectedMonthChanged(MonthOption? value)
    {
        if (_suppressSelection || value is null)
        {
            return;
        }

        _ = LoadMonthAsync(value.Period);
    }

    [RelayCommand]
    public async Task ReloadAsync()
    {
        if (SelectedMonth is not { } option)
        {
            return;
        }

        _cache.Remove(option.Period);
        await LoadMonthAsync(option.Period).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task PlayTrackAsync(ReportTrackRow? row)
    {
        if (row is null)
        {
            return;
        }

        try
        {
            // 月报 Top10 是简化歌曲对象，没有音源档位；必须先补全再交给播放器。
            var track = await _api.GetTrackAsync(row.Source.Id).ConfigureAwait(true);

            if (track is null)
            {
                _notice.Show("歌曲信息读取失败", NoticeSeverity.Error);
                return;
            }

            await _coordinator.EnqueueAndPlayAsync(track).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "月报曲目 {MusicId} 播放失败", row.Source.Id);
            _notice.Show("播放失败", NoticeSeverity.Error);
        }
    }

    /// <summary>
    /// 让服务端按当前月份新建月度歌单。
    /// </summary>
    /// <remarks><b>写操作。</b> 确认框由页面弹，这里只负责请求与结果提示。</remarks>
    public async Task CreatePlaylistAsync()
    {
        if (Report is not { } report || IsCreatingPlaylist)
        {
            return;
        }

        var period = new MonthPeriod(report.Year, report.Month);
        IsCreatingPlaylist = true;

        try
        {
            var created = await _api.CreateMonthReportPlaylistAsync(period).ConfigureAwait(true);
            _notice.Show(
                created ? "已为你新建月度歌单" : "歌单生成失败",
                created ? NoticeSeverity.Success : NoticeSeverity.Error);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "新建月度歌单失败：{Month}", period.Value);
            _notice.Show("歌单生成失败", NoticeSeverity.Error);
        }
        finally
        {
            IsCreatingPlaylist = false;
        }
    }

    /// <summary>换账号：丢掉缓存与当前月报，下一次进入重新定位月份。</summary>
    public void Reset()
    {
        _request?.Cancel();
        _generation++;
        _cache.Clear();
        _loaded = false;
        IsBusy = false;
        HasError = false;
        IsCreatingPlaylist = false;
        Report = null;
        TopRest.Clear();
        PodiumFirst = null;
        PodiumSecond = null;
        PodiumThird = null;
        AlbumCovers.Clear();
        EmptyText = "";
        OnPropertyChanged(nameof(EmptyText));
        RaiseCollectionsChanged();
        RebuildMonths(resetSelection: true);
    }

    private async Task LoadMonthAsync(MonthPeriod period)
    {
        _request?.Cancel();
        _request?.Dispose();
        _request = new CancellationTokenSource();
        var cancellationToken = _request.Token;
        var generation = ++_generation;

        IsBusy = true;
        HasError = false;

        if (_cache.TryGetValue(period, out var cached))
        {
            Apply(period, cached);
            IsBusy = false;
            return;
        }

        try
        {
            var report = await _api.GetMonthReportAsync(period, cancellationToken).ConfigureAwait(true);

            if (generation != _generation || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            _cache[period] = report;
            Apply(period, report);
        }
        catch (OperationCanceledException)
        {
            // 换月或换号取消，静默。
        }
        catch (Exception ex)
        {
            if (generation == _generation)
            {
                _logger.LogDebug(ex, "月报读取失败：{Month}", period.Value);
                HasError = true;
                Apply(period, null);
            }
        }
        finally
        {
            if (generation == _generation)
            {
                IsBusy = false;
            }
        }
    }

    private void Apply(MonthPeriod period, MonthReport? report)
    {
        TopRest.Clear();
        PodiumFirst = null;
        PodiumSecond = null;
        PodiumThird = null;
        AlbumCovers.Clear();
        Report = report;
        EmptyText = report is null ? EmptyMessage(period) : "";
        OnPropertyChanged(nameof(EmptyText));

        if (report is not null)
        {
            for (var i = 0; i < report.TopTracks.Count; i++)
            {
                var row = new ReportTrackRow(i + 1, report.TopTracks[i]);

                switch (i)
                {
                    case 0:
                        PodiumFirst = row;
                        break;
                    case 1:
                        PodiumSecond = row;
                        break;
                    case 2:
                        PodiumThird = row;
                        break;
                    default:
                        TopRest.Add(row);
                        break;
                }
            }

            for (var i = 0; i < report.AlbumCovers.Count; i++)
            {
                AlbumCovers.Add(new AlbumCoverItem(report.AlbumCovers[i], i));
            }
        }

        RaiseCollectionsChanged();
    }

    private void RaiseReportChanged()
    {
        OnPropertyChanged(nameof(HasReport));
        OnPropertyChanged(nameof(CanCreatePlaylist));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(ShowEmpty));
        OnPropertyChanged(nameof(ListenTotalText));
        OnPropertyChanged(nameof(PlayCountText));
        OnPropertyChanged(nameof(SongCountText));
        OnPropertyChanged(nameof(ArtistNameText));
        OnPropertyChanged(nameof(ArtistContentText));
        OnPropertyChanged(nameof(ArtistMusicNameText));
        OnPropertyChanged(nameof(ListenChangeLabel));
        OnPropertyChanged(nameof(ListenChangeValueText));
        OnPropertyChanged(nameof(HasListenChange));
        OnPropertyChanged(nameof(ListenChangeIsNumeric));
        OnPropertyChanged(nameof(ListenChangeDescription));
        OnPropertyChanged(nameof(HasListenChangeDescription));
        OnPropertyChanged(nameof(ListenChangeArrow));
        OnPropertyChanged(nameof(PlayChangeText));
        OnPropertyChanged(nameof(HasPlayChange));
        OnPropertyChanged(nameof(ArtistCoverImage));
        OnPropertyChanged(nameof(ArtistMusicCoverImage));
        OnPropertyChanged(nameof(LongestDayDateText));
        OnPropertyChanged(nameof(LongestDayMinutesText));
        OnPropertyChanged(nameof(HasLongestDay));
        OnPropertyChanged(nameof(HasListenTopTrack));
        OnPropertyChanged(nameof(LongestDayCardLabel));
        OnPropertyChanged(nameof(LongestDayCardValue));
        OnPropertyChanged(nameof(LongestDayCardSubtitle));
        OnPropertyChanged(nameof(HasArtist));
        OnPropertyChanged(nameof(HasArtistMusic));
        OnPropertyChanged(nameof(StatusText));
    }

    private void RaiseCollectionsChanged()
    {
        OnPropertyChanged(nameof(HasTopRest));
        OnPropertyChanged(nameof(HasAlbumCovers));
        OnPropertyChanged(nameof(HasPodium));
        OnPropertyChanged(nameof(HasPodiumFirst));
        OnPropertyChanged(nameof(HasPodiumSecond));
        OnPropertyChanged(nameof(HasPodiumThird));
        OnPropertyChanged(nameof(PodiumFirst));
        OnPropertyChanged(nameof(PodiumSecond));
        OnPropertyChanged(nameof(PodiumThird));
        OnPropertyChanged(nameof(HasMostPlayedSong));
        OnPropertyChanged(nameof(MostPlayedSongTitle));
        OnPropertyChanged(nameof(MostPlayedSongPlayCountText));
        OnPropertyChanged(nameof(LongestDayCardColumnSpan));
    }

    /// <summary>把「10-02与音乐共度81分钟」拆成日期与分钟数。</summary>
    private static (string Date, string Minutes) SplitLongestDay(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ("", "");
        }

        var content = text.Trim();
        var dateEnd = 0;

        while (dateEnd < content.Length
               && (char.IsAsciiDigit(content[dateEnd]) || content[dateEnd] == '-'))
        {
            dateEnd++;
        }

        var date = content[..dateEnd].Trim('-');
        var minuteIndex = content.IndexOf("分钟", StringComparison.Ordinal);

        if (minuteIndex <= 0)
        {
            return (date, "");
        }

        var start = minuteIndex - 1;
        while (start >= 0 && char.IsAsciiDigit(content[start]))
        {
            start--;
        }

        var minutes = content[(start + 1)..minuteIndex];

        return (date, minutes.Length > 0 ? $"{minutes} 分钟" : "");
    }

    /// <summary><c>10-02</c> → <c>10月2日</c>；解析不了就保留原样。</summary>
    private static string FormatDay(string date)
    {
        var parts = date.Split('-');

        return parts.Length == 2
            && int.TryParse(parts[0], out var month)
            && int.TryParse(parts[1], out var day)
            ? $"{month}月{day}日"
            : date;
    }

    /// <summary>把「比上月\n+18909%」拆成标签与数值两段。</summary>
    private static (string Label, string Value) SplitChange(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ("", "");
        }

        var cleaned = text.Replace("\n", "", StringComparison.Ordinal).Trim();
        var index = cleaned.IndexOfAny(['+', '-']);

        return index > 0
            ? (cleaned[..index].Trim(), cleaned[index..].Trim())
            : ("", cleaned);
    }

    private void RebuildMonths(bool resetSelection)
    {
        var current = CurrentMonth();
        Months.Clear();

        for (var i = 0; i < VisibleMonthCount; i++)
        {
            Months.Add(new MonthOption(current.AddMonths(-i)));
        }

        if (!resetSelection)
        {
            return;
        }

        _suppressSelection = true;
        SelectedMonth = Months[0];
        _suppressSelection = false;
    }

    private MonthOption EnsureMonthOption(MonthPeriod period)
    {
        foreach (var option in Months)
        {
            if (option.Period == period)
            {
                return option;
            }
        }

        var added = new MonthOption(period);
        var index = 0;

        while (index < Months.Count
               && (Months[index].Period.Year > period.Year
                   || (Months[index].Period.Year == period.Year
                       && Months[index].Period.Month > period.Month)))
        {
            index++;
        }

        Months.Insert(index, added);
        return added;
    }

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.Now);

    private static MonthPeriod CurrentMonth()
    {
        var today = Today();
        return new MonthPeriod(today.Year, today.Month);
    }

    private static string EmptyMessage(MonthPeriod period)
    {
        if (period == CurrentMonth())
        {
            return "本月音波报告还在路上，请耐心等待～";
        }

        var current = CurrentMonth();

        if (period.Year > current.Year
            || (period.Year == current.Year && period.Month > current.Month))
        {
            return "本月音波报告还在路上，请耐心等待～";
        }

        return "这个月音波强度不足，报告空空如也";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _generation++;
        _request?.Cancel();
        _request?.Dispose();
        _request = null;
    }
}

/// <summary>月份下拉的一项。</summary>
public sealed record MonthOption(MonthPeriod Period)
{
    public string Label => Period.Label;
}

/// <summary>月报 Top 列表的一行。</summary>
public sealed record ReportTrackRow(int Rank, ReportTrack Source)
{
    public string RankLabel => Rank.ToString(CultureInfo.InvariantCulture);

    public string PlayCountLabel => Source.PlayCount is { } count
        ? $"{CommentCountLabel.Format(count)} 次"
        : "";
}

/// <summary>月报「最爱专辑」封面墙的一格。</summary>
public sealed record AlbumCoverItem(Uri Uri, int Index)
{
    /// <summary>轻微倾斜，让封面墙像一叠随手摆开的唱片。</summary>
    public double Rotation => (Index % 5) switch
    {
        0 => -2.4,
        1 => 1.8,
        2 => -1.2,
        3 => 2.6,
        _ => -0.8,
    };
}
