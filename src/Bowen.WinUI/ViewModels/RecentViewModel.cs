using System.Collections.ObjectModel;
using Bowen.Core.Models;
using Bowen.Core.Services.Abstractions;
using Bowen.WinUI.Playback;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bowen.WinUI.ViewModels;

/// <summary>
/// 「最近播放」页。
/// </summary>
/// <remarks>
/// <para>
/// 数据源是本地记录（<see cref="IPlayHistoryStore"/>），**没有任何网络请求** ——
/// 官方桌面端同样是纯本地的，见 <c>docs/library-sidebar.md</c> §6。
/// </para>
/// <para>
/// <b>每次进入都重读，不缓存。</b> 它是本地文件、条数有上限，重读的代价可以忽略；
/// 而缓存会让「我刚听完一首歌，切回来却没出现」这种明显错误发生。
/// </para>
/// <para>
/// <b>分页只在界面上做。</b> 记录是本地文件、上限 500 条，一次读进来很便宜；
/// 真正贵的是行容器与封面，所以每页只往列表里放 30 条，滚到末尾再放下一批。
/// 数据层不需要游标，见 <c>PagedList</c> 那套只用于服务端分页。
/// </para>
/// </remarks>
public sealed partial class RecentViewModel : ObservableObject
{
    /// <summary>一次取多少条。与存储上限一致。</summary>
    private const int MaxEntries = 500;

    /// <summary>每页放进列表的条数。</summary>
    private const int PageSize = 30;

    private readonly IPlayHistoryStore _history;
    private readonly PlaybackCoordinator _coordinator;
    private readonly ILogger<RecentViewModel> _logger;
    private IReadOnlyList<PlayHistoryEntry> _entries = [];
    private bool _hasMore;

    public RecentViewModel(
        IPlayHistoryStore history,
        PlaybackCoordinator coordinator,
        ILogger<RecentViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(coordinator);

        _history = history;
        _coordinator = coordinator;
        _logger = logger ?? NullLogger<RecentViewModel>.Instance;
        LoadMoreCommand = new AsyncRelayCommand(LoadMoreAsync);
    }

    public ObservableCollection<Track> Tracks { get; } = [];

    /// <summary>滚到列表末尾时由 <c>AutoPaging</c> 触发，放下一批 30 条。</summary>
    public IAsyncRelayCommand LoadMoreCommand { get; }

    /// <summary>还有没放进列表的记录。</summary>
    public bool HasMore
    {
        get => _hasMore;
        private set
        {
            if (!SetProperty(ref _hasMore, value)) return;
            OnPropertyChanged(nameof(ShowEnd));
        }
    }

    /// <summary>已经全部放进列表，页脚据此显示「没有更多了哦~」。</summary>
    public bool ShowEnd => !HasMore && Tracks.Count > 0;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    [ObservableProperty]
    public partial Track? CurrentTrack { get; set; }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;

        try
        {
            _entries = await _history.GetRecentAsync(MaxEntries, cancellationToken).ConfigureAwait(true);

            Tracks.Clear();
            AppendPage();

            // 计数报的是记录总数，不是已经放进列表的条数 —— 滚到底之前列表并不完整。
            StatusText = _entries.Count == 0
                ? "还没有播放记录。用本客户端播一首歌就会出现在这里。"
                : $"{Formats.TrackCount(_entries.Count)} · 最近的在最前";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取播放历史失败");
            _entries = [];
            Tracks.Clear();
            HasMore = false;
            OnPropertyChanged(nameof(ShowEnd));
            StatusText = $"读取失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>把下一批记录放进列表。</summary>
    private void AppendPage()
    {
        var end = Math.Min(_entries.Count, Tracks.Count + PageSize);

        for (var index = Tracks.Count; index < end; index++)
        {
            Tracks.Add(_entries[index].ToTrack());
        }

        HasMore = Tracks.Count < _entries.Count;
        OnPropertyChanged(nameof(ShowEnd));
    }

    private Task LoadMoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!HasMore) return Task.CompletedTask;
        AppendPage();
        return Task.CompletedTask;
    }

    /// <summary>刷新：重新读一次播放历史。工具栏的刷新按钮用它。</summary>
    [RelayCommand]
    private Task ReloadAsync(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    [RelayCommand]
    private async Task PlayAsync(Track? track)
    {
        if (track is null)
        {
            return;
        }

        CurrentTrack = track;

        // 加到队尾并立即播放：整个列表不再被拖进队列，要一次排进去走工具栏的「全部加入播放列表」。
        await _coordinator.EnqueueAndPlayAsync(track).ConfigureAwait(true);
    }

    /// <summary>
    /// 清空历史。
    /// </summary>
    /// <remarks>
    /// <b>调用方负责先确认。</b> 这是不可撤销的本地数据删除，界面上必须先问一次
    /// （见 <c>RecentPage</c> 的对话框），命令本身不做二次确认 ——
    /// 那会把「要不要问」这个界面决策埋进 ViewModel。
    /// </remarks>
    [RelayCommand]
    private async Task ClearAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _history.ClearAsync(cancellationToken).ConfigureAwait(true);
            _entries = [];
            Tracks.Clear();
            HasMore = false;
            OnPropertyChanged(nameof(ShowEnd));
            CurrentTrack = null;
            StatusText = "播放记录已清空。";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "清空播放历史失败");
            StatusText = $"清空失败：{ex.Message}";
        }
    }
}
