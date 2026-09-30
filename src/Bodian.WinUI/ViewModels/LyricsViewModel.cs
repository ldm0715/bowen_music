using System.Collections.ObjectModel;
using Bodian.Core.Lyrics;
using Bodian.Core.Models;
using Bodian.Core.Models.Lyrics;
using Bodian.WinUI.Playback;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 歌词面板。**单例**，与播放条一样常驻。
/// </summary>
/// <remarks>
/// <para>
/// <b>面板关着就不取词。</b> 没人看的时候为每一首播过的歌都发一次歌词请求，属于把别人的服务当压测。
/// 取词只发生在「面板开着」这个前提下：打开的那一刻，以及开着的时候换歌。
/// </para>
/// <para>
/// 歌词走 <see cref="LyricRepository"/>：同一首歌重播、面板反复开关都不会重复请求。
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
    private readonly ILogger<LyricsViewModel> _logger;

    private LyricDocument _document = LyricDocument.Empty;

    /// <summary>已经装载歌词的曲目 id；<c>0</c> 表示还没装载过。</summary>
    private long _loadedTrackId;

    /// <summary>装载代次，用来丢弃迟到的结果（切歌比取词快时会发生）。</summary>
    private int _generation;

    public LyricsViewModel(
        LyricRepository repository,
        PlaybackCoordinator coordinator,
        IPlaybackService engine,
        ILogger<LyricsViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(engine);

        _repository = repository;
        _coordinator = coordinator;
        _engine = engine;
        _logger = logger ?? NullLogger<LyricsViewModel>.Instance;

        _coordinator.Started += OnStarted;
        _engine.PositionChanged += OnPositionChanged;
    }

    /// <summary>歌词行的列表。</summary>
    public ObservableCollection<LyricLineView> Lines { get; } = [];

    /// <summary>面板是否展开。</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

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

    /// <summary>当前行的下标；不在任何行里时为 <c>-1</c>。</summary>
    [ObservableProperty]
    public partial int CurrentIndex { get; set; } = -1;

    [RelayCommand]
    private void Toggle() => IsOpen = !IsOpen;

    [RelayCommand]
    private void Close() => IsOpen = false;

    [RelayCommand]
    private Task RetryAsync() => _coordinator.CurrentTrack is { } track
        ? LoadAsync(track)
        : Task.CompletedTask;

    /// <summary>点某一行跳到那一句。</summary>
    [RelayCommand]
    private Task SeekToLineAsync(LyricLineView? line) => line is null
        ? Task.CompletedTask
        : _engine.SeekAsync(line.Start);

    partial void OnIsOpenChanged(bool value)
    {
        if (!value)
        {
            return;
        }

        _ = RefreshAsync();
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
        _document = document;
        _loadedTrackId = track.Id;
        HasTrack = true;

        Lines.Clear();
        CurrentIndex = -1;

        foreach (var line in document.Lines)
        {
            Lines.Add(new LyricLineView(line.Start, line.Text));
        }

        // 空文档不是错误：服务端对没有歌词的歌返回空串，业务码仍是 200。
        StatusText = document.IsEmpty ? "这首歌还没有歌词" : "";

        // 打开面板时歌可能已经唱到一半了，立刻对齐一次。
        UpdateCurrentLine(_engine.Position);
    }

    private void Clear()
    {
        _document = LyricDocument.Empty;
        _loadedTrackId = 0;
        Lines.Clear();
        CurrentIndex = -1;
    }

    // ── 事件 ────────────────────────────────────────────────────────────────

    private void OnStarted(object? sender, PlaybackStartedEventArgs e)
    {
        HasTrack = true;

        if (IsOpen)
        {
            _ = LoadAsync(e.Track);
            return;
        }

        // 面板关着就先不取词，把「已装载」作废，等打开时再按当前曲目加载。
        _loadedTrackId = 0;
    }

    private void OnPositionChanged(object? sender, PlaybackPositionChangedEventArgs e)
        => UpdateCurrentLine(e.Position);

    private void UpdateCurrentLine(TimeSpan position)
    {
        if (_document.IsEmpty)
        {
            return;
        }

        var index = _document.IndexOfLineAt(position);

        if (index != CurrentIndex)
        {
            SetCurrent(index);
        }
    }

    /// <summary>只动两行的高亮 —— 63 行每行都刷一遍是白费。</summary>
    private void SetCurrent(int index)
    {
        if (CurrentIndex >= 0 && CurrentIndex < Lines.Count)
        {
            Lines[CurrentIndex].IsCurrent = false;
        }

        CurrentIndex = index;

        if (index >= 0 && index < Lines.Count)
        {
            Lines[index].IsCurrent = true;
        }
    }
}
