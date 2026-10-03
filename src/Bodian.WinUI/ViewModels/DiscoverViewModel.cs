using System.Collections.ObjectModel;
using Bodian.Core.Api;
using Bodian.Core.Models.Home;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 发现页。
/// </summary>
/// <remarks>
/// <para>
/// <b>两段式：先拿布局，再逐个拿内容。</b> <c>service/home/index</c> 只返回模块清单
/// （实测 12 个），每个模块的内容要单独请求一次。所以打开这一页的请求数
/// **等于加载了几个模块**，不是 1。
/// </para>
/// <para>
/// <b>分批懒加载，不一次拉全。</b> 首屏只拉 <see cref="InitialBatch"/> 个，
/// 滚到底再拉下一批。一次并发 12 个请求是把第三方服务当压测，本项目不这么做。
/// </para>
/// <para>
/// <b>单个模块失败不影响其他模块。</b> 一个模块的请求出错只记日志、跳过它 ——
/// 发现页是一堆并列的内容，一个模块挂掉不该让整页空白。
/// </para>
/// </remarks>
public sealed partial class DiscoverViewModel : ObservableObject
{
    /// <summary>首屏拉几个模块。</summary>
    private const int InitialBatch = 4;

    /// <summary>之后每批拉几个。</summary>
    private const int BatchSize = 3;

    private readonly IBodianApi _api;
    private readonly ILogger<DiscoverViewModel> _logger;

    /// <summary>布局里本项目支持渲染的模块，按服务端给的顺序。</summary>
    private IReadOnlyList<HomeModule> _supported = [];

    /// <summary>已经取过内容的模块数（在 <see cref="_supported"/> 里的下标）。</summary>
    private int _consumed;
    private bool _layoutLoaded;

    public DiscoverViewModel(IBodianApi api, ILogger<DiscoverViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(api);

        _api = api;
        _logger = logger ?? NullLogger<DiscoverViewModel>.Instance;
    }

    /// <summary>已经取到内容的模块，按服务端给的顺序。</summary>
    public ObservableCollection<HomeFeed> Feeds { get; } = [];
    public ObservableCollection<object> Rows { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEnd))]
    [NotifyPropertyChangedFor(nameof(ShowRetry))]
    public partial bool IsBusy { get; set; }

    /// <summary>还没取完的模块还有没有。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEnd))]
    [NotifyPropertyChangedFor(nameof(ShowRetry))]
    public partial bool HasMore { get; set; }

    /// <summary>上一次加载失败了。页脚据此让出「重试」入口。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEnd))]
    [NotifyPropertyChangedFor(nameof(ShowRetry))]
    public partial bool LoadFailed { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    /// <summary>已经取完，且列表非空。页脚据此显示「没有更多了哦~」。</summary>
    public bool ShowEnd => Feeds.Count > 0 && !HasMore && !IsBusy && !LoadFailed;

    /// <summary>翻页失败，且确实还有下一批可拉。页脚据此显示「重试」。</summary>
    public bool ShowRetry => LoadFailed && HasMore && !IsBusy;

    /// <summary>首次进入时调。<b>已经加载过就什么都不做</b>（滚回来的位置与已取的内容都要留住）。</summary>
    public Task EnsureLoadedAsync(CancellationToken cancellationToken = default) =>
        _layoutLoaded ? Task.CompletedTask : LoadLayoutAsync(cancellationToken);

    /// <summary>滚到底时调，取下一批。</summary>
    /// <remarks>
    /// 这里必须自己吞异常：自动翻页触发得频繁，异常一旦逃到 UI 线程就是闪退。
    /// 「重新加载」是失败后重取这一批的入口。
    /// </remarks>
    [RelayCommand]
    public async Task LoadMoreAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy || !HasMore)
        {
            return;
        }

        LoadFailed = false;

        try
        {
            await LoadBatchAsync(BatchSize, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "发现页加载下一批失败");
            StatusText = $"加载失败：{ex.Message}";
            LoadFailed = true;
        }
    }

    /// <summary>重新加载：布局也重新拿。</summary>
    [RelayCommand]
    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        _layoutLoaded = false;
        _consumed = 0;
        LoadFailed = false;
        Feeds.Clear();
        Rows.Clear();

        await LoadLayoutAsync(cancellationToken).ConfigureAwait(true);
    }

    private async Task LoadLayoutAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        StatusText = "正在加载发现页…";

        try
        {
            var modules = await _api.GetHomeModulesAsync(cancellationToken).ConfigureAwait(true);

            // 不支持的类型直接滤掉：它们的形状本项目没有实现，留着只会得到一排空标题。
            _supported = [.. modules.Where(module => module.IsSupported)];
            _consumed = 0;
            _layoutLoaded = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载发现页布局失败");
            StatusText = $"加载失败：{ex.Message}";
            LoadFailed = true;
            _layoutLoaded = false;
            return;
        }
        finally
        {
            IsBusy = false;
        }

        await LoadBatchAsync(InitialBatch, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>取接下来 <paramref name="count"/> 个模块的内容。</summary>
    private async Task LoadBatchAsync(int count, CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;

        try
        {
            var end = Math.Min(_consumed + count, _supported.Count);

            for (var i = _consumed; i < end; i++)
            {
                var module = _supported[i];

                try
                {
                    // 顺序请求，不并发 —— 一批只有两三个，顺序发对服务端更友好，
                    // 而且能保证界面上的顺序与服务端给的布局一致。
                    if (await _api.GetHomeModuleAsync(module, cancellationToken).ConfigureAwait(true) is { } feed)
                    {
                        Feeds.Add(feed);
                        Rows.Add(new ListSectionHeader { Title = feed.Title });
                        foreach (var section in feed.Sections) Rows.Add(section);
                    }
                }
                catch (Exception ex)
                {
                    // 单个模块失败只跳过它：发现页是一堆并列内容，一个挂掉不该让整页空白。
                    _logger.LogWarning(ex, "发现页模块 {Id}（{Name}）加载失败，跳过", module.Id, module.Name);
                }
            }

            _consumed = end;
            HasMore = _consumed < _supported.Count;

            // 「（滚动加载）」已去掉 —— 理由同 PagedList 里那一处。
            StatusText = Feeds.Count == 0 ? "这次没有取到内容。" : $"{Feeds.Count} 个模块";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
