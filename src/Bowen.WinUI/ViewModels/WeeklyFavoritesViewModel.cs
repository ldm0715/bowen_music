using System.Collections.ObjectModel;
using System.Globalization;
using Bowen.Core.Api;
using Bowen.Core.Models;
using Bowen.Core.Models.AudioMemory;
using Bowen.WinUI.Playback;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bowen.WinUI.ViewModels;

/// <summary>
/// 音波回忆的「本周最爱」页签：最近一周播歌排行。
/// </summary>
/// <remarks>
/// 数据来自 <c>ucenter/playdata/play_ranking?type=1</c>，返回完整曲目 + 播放次数。
/// 只加载一次；失败后由刷新按钮重试，不自动重复请求。
/// </remarks>
public sealed partial class WeeklyFavoritesViewModel : ObservableObject, IDisposable
{
    private readonly IBodianApi _api;
    private readonly PlaybackCoordinator _coordinator;
    private readonly ILogger<WeeklyFavoritesViewModel> _logger;

    private CancellationTokenSource? _request;
    private int _generation;
    private bool _disposed;

    public WeeklyFavoritesViewModel(
        IBodianApi api,
        PlaybackCoordinator coordinator,
        ILogger<WeeklyFavoritesViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(coordinator);

        _api = api;
        _coordinator = coordinator;
        _logger = logger ?? NullLogger<WeeklyFavoritesViewModel>.Instance;
    }

    /// <summary>第 2 名及以后。第 1 名在 <see cref="Hero"/> 里单独展示。</summary>
    public ObservableCollection<PlayRankingRow> Rest { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHero))]
    public partial PlayRankingRow? Hero { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(ShowEmpty))]
    [NotifyPropertyChangedFor(nameof(ShowError))]
    public partial bool HasError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(ShowEmpty))]
    public partial bool HasLoaded { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial int TotalCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial long TotalPlays { get; set; }

    /// <summary>第一名的卡片。没有排行时为 <c>null</c>。</summary>
    public bool HasHero => Hero is not null;

    public bool HasRest => Rest.Count > 0;

    public bool ShowEmpty => HasLoaded && !HasError && TotalCount == 0;

    public bool ShowError => HasError && !IsBusy;

    public string StatusText
    {
        get
        {
            if (IsBusy)
            {
                return HasLoaded ? "正在刷新…" : "正在加载…";
            }

            if (HasError)
            {
                return "加载失败";
            }

            if (!HasLoaded)
            {
                return "";
            }

            return TotalCount == 0
                ? "最近一周还没有播放记录。"
                : $"最近一周 · 共 {TotalCount} 首 / {CommentCountLabel.Format(TotalPlays)} 次";
        }
    }

    /// <summary>第一次切到本页签时调；已经加载过就什么都不做。</summary>
    public Task EnsureLoadedAsync() => HasLoaded || IsBusy ? Task.CompletedTask : LoadAsync();

    [RelayCommand]
    public async Task ReloadAsync()
    {
        HasLoaded = false;
        await LoadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task PlayAsync(PlayRankingRow? row)
    {
        if (row is null)
        {
            return;
        }

        await _coordinator.EnqueueAndPlayAsync(row.Track).ConfigureAwait(true);
    }

    /// <summary>换账号：丢掉上一个账号的数据，下一次进入重新拉。</summary>
    public void Reset()
    {
        _request?.Cancel();
        _generation++;
        Hero = null;
        Rest.Clear();
        TotalCount = 0;
        TotalPlays = 0;
        HasLoaded = false;
        HasError = false;
        IsBusy = false;
        RaiseRestChanged();
    }

    private async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        _request?.Cancel();
        _request?.Dispose();
        _request = new CancellationTokenSource();
        var cancellationToken = _request.Token;
        var generation = ++_generation;

        IsBusy = true;
        HasError = false;

        try
        {
            var ranking = await _api.GetWeeklyPlayRankingAsync(cancellationToken).ConfigureAwait(true);

            if (generation != _generation || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (ranking is null)
            {
                HasError = true;
                HasLoaded = false;
                return;
            }

            Apply(ranking);
            HasLoaded = true;
        }
        catch (OperationCanceledException)
        {
            // 换号或重新加载取消，静默。
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "本周最爱读取失败");
            HasError = true;
            HasLoaded = false;
        }
        finally
        {
            if (generation == _generation)
            {
                IsBusy = false;
            }
        }
    }

    private void Apply(IReadOnlyList<PlayRankingEntry> ranking)
    {
        Hero = null;
        Rest.Clear();

        for (var i = 0; i < ranking.Count; i++)
        {
            var entry = ranking[i];
            var row = new PlayRankingRow(i + 1, entry.Track, entry.PlayCount);

            if (i == 0)
            {
                Hero = row;
            }
            else
            {
                Rest.Add(row);
            }
        }

        TotalCount = ranking.Count;
        TotalPlays = ranking.Sum(entry => entry.PlayCount);
        RaiseRestChanged();
    }

    private void RaiseRestChanged()
    {
        OnPropertyChanged(nameof(HasRest));
        OnPropertyChanged(nameof(StatusText));
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

/// <summary>本周排行的一行。</summary>
public sealed record PlayRankingRow(int Rank, Track Track, long PlayCount)
{
    public string RankLabel => Rank.ToString(CultureInfo.InvariantCulture);

    public string PlayCountLabel => $"{CommentCountLabel.Format(PlayCount)} 次";
}
