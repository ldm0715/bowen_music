using System.Collections.ObjectModel;
using Bodian.Core.Models;
using Bodian.Core.Services.Abstractions;
using Bodian.WinUI.Playback;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.WinUI.ViewModels;

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
/// <b>没有分页。</b> 记录本来就有上限（<c>JsonPlayHistoryStore.Capacity</c>），
/// 一次取完即可，不必套 <c>PagedCursor</c>。
/// </para>
/// </remarks>
public sealed partial class RecentViewModel : ObservableObject
{
    /// <summary>一次取多少条。与存储上限一致。</summary>
    private const int MaxEntries = 500;

    private readonly IPlayHistoryStore _history;
    private readonly PlaybackCoordinator _coordinator;
    private readonly ILogger<RecentViewModel> _logger;

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
    }

    public ObservableCollection<Track> Tracks { get; } = [];

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
            var entries = await _history.GetRecentAsync(MaxEntries, cancellationToken).ConfigureAwait(true);

            Tracks.Clear();

            foreach (var entry in entries)
            {
                Tracks.Add(entry.ToTrack());
            }

            StatusText = Tracks.Count == 0
                ? "还没有播放记录。用本客户端播一首歌就会出现在这里。"
                : $"{Tracks.Count} 首，最近的在最前";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取播放历史失败");
            StatusText = $"读取失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task PlayAsync(Track? track)
    {
        if (track is null)
        {
            return;
        }

        var index = Tracks.IndexOf(track);

        if (index < 0)
        {
            return;
        }

        CurrentTrack = track;

        await _coordinator.PlayFromAsync([.. Tracks], index).ConfigureAwait(true);
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
            Tracks.Clear();
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
