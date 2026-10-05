using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using Bodian.Core.Api.Paging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 一页一页加载的列表，含自己的游标与状态文案。
/// </summary>
/// <remarks>
/// <para>
/// 「已购单曲」「已购专辑」「收藏的专辑」三个列表的翻页逻辑完全一样，所以收在这里。
/// 歌单曲目那一族没有用它 —— 那边还多一层「先确定是哪个歌单」，见
/// <see cref="PlaylistTracksViewModel"/>。
/// </para>
/// <para>
/// <b>「还有没有下一页」只看游标，不信服务端给的总数。</b> 不可用条目会被省略，
/// 实测歌单标称 121 首时第一页只回 99 首 —— 按总数算页数会漏。
/// </para>
/// <para>
/// 用 <see cref="ObservableObject.SetProperty{T}"/> 而不是源生成的
/// <c>[ObservableProperty]</c>：这是个泛型类，手写属性省得跟源生成器纠缠。
/// </para>
/// </remarks>
public sealed class PagedList<T> : ObservableObject
{
    private readonly Func<PagedCursor, CancellationToken, Task<PagedResult<T>>> _fetch;
    private readonly ILogger _logger;
    private readonly string _what;
    private readonly PagingConvention _convention;
    private readonly string? _countUnit;

    private PagedCursor? _cursor;
    private bool _started;
    private bool _hasMore;
    private bool _isBusy;
    private bool _loadFailed;
    private string _statusText = "";

    /// <param name="fetch">取一页。调用方在这里拼请求。</param>
    /// <param name="what">日志与错误文案里用的名字，例如「已购单曲」。</param>
    /// <param name="emptyText">一条都没有时的说明。</param>
    /// <param name="countUnit">
    /// <see cref="StatusText"/> 里数量的单位，例如「首」。<b>不传则保持「N 项」</b> ——
    /// 这个类是泛型，也服务专辑、歌单这些非曲目列表，改默认值会连带换掉它们的文案。
    /// </param>
    public PagedList(
        Func<PagedCursor, CancellationToken, Task<PagedResult<T>>> fetch,
        ILogger logger,
        string what,
        string emptyText,
        PagingConvention? pagingConvention = null,
        string? countUnit = null)
    {
        ArgumentNullException.ThrowIfNull(fetch);
        ArgumentNullException.ThrowIfNull(logger);

        _fetch = fetch;
        _logger = logger;
        _what = what;
        _convention = pagingConvention ?? PagingConvention.OneBased;
        _countUnit = countUnit;
        EmptyText = emptyText;

        // 手写命令而不是 [RelayCommand]：源生成器在泛型类上要额外折腾，这里两个命令不值得。
        LoadMoreCommand = new AsyncRelayCommand(LoadMoreAsync);
        ReloadCommand = new AsyncRelayCommand(ReloadAsync);
    }

    public ObservableCollection<T> Items { get; } = [];

    /// <summary>加载下一页。滚到列表末尾时由 <c>AutoPaging</c> 触发。</summary>
    public IAsyncRelayCommand LoadMoreCommand { get; }

    /// <summary>重新从第一页开始。</summary>
    public IAsyncRelayCommand ReloadCommand { get; }

    /// <summary>一条都没有时的说明。<b>由调用方给</b>：每节的措辞不一样。</summary>
    public string EmptyText { get; }

    public bool HasMore
    {
        get => _hasMore;
        private set => SetDerived(ref _hasMore, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetDerived(ref _isBusy, value);
    }

    /// <summary>上一次加载失败了。页脚据此让出「重试」入口。</summary>
    public bool LoadFailed
    {
        get => _loadFailed;
        private set => SetDerived(ref _loadFailed, value);
    }

    /// <summary>已经取完，且列表非空。页脚据此显示「没有更多了哦~」。</summary>
    public bool ShowEnd => _started && !IsBusy && !HasMore && !LoadFailed && Items.Count > 0;

    /// <summary>翻页失败，且确实还有下一页可拉。页脚据此显示「重试」。</summary>
    public bool ShowRetry => LoadFailed && HasMore && !IsBusy;

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>首次进入时调。<b>已经加载过就什么都不做。</b></summary>
    public Task EnsureLoadedAsync(CancellationToken cancellationToken = default) =>
        _started ? Task.CompletedTask : ReloadAsync(cancellationToken);

    /// <summary>重新从第一页开始。</summary>
    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        _started = true;
        IsBusy = true;
        HasMore = false;
        LoadFailed = false;
        Items.Clear();
        _cursor = new PagedCursor(_convention);

        try
        {
            await AppendNextPageAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载{What}失败", _what);
            StatusText = $"加载失败：{ex.Message}";
            LoadFailed = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task LoadMoreAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy || _cursor is null || !HasMore)
        {
            return;
        }

        IsBusy = true;
        LoadFailed = false;

        try
        {
            await AppendNextPageAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载{What}的下一页失败", _what);
            StatusText = $"加载失败：{ex.Message}";
            LoadFailed = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 一页一页地把剩下的全部拉完，最多拉 <paramref name="maxPages"/> 页。
    /// </summary>
    /// <returns>
    /// 是否真的拉到了「没有下一页」。<b>撞上限、中途失败、以及首屏就失败，都返回 <c>false</c>。</b>
    /// </returns>
    /// <remarks>
    /// <para>
    /// 「播放全部」要用它：只把当前已加载的那些排进队列的话，11 首的专辑第一次点只会播 5 首，
    /// 后面 6 首永远进不了队列。
    /// </para>
    /// <para>
    /// <b>中途失败就停，并保留已经加载的部分</b> —— 队列短一点也比整次操作失败好。
    /// 调用方拿到 <c>false</c> 时应当照常播现有内容，而不是报错。
    /// </para>
    /// <para>
    /// <b>进来时就有加载在进行则直接返回 <c>false</c></b>：<see cref="LoadMoreAsync"/>
    /// 在忙时会早退，硬循环只会空转到上限。宁可少拉几页，也不要在这里抢同一份状态。
    /// </para>
    /// </remarks>
    public async Task<bool> LoadAllAsync(int maxPages = 20, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPages);

        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(true);

        if (IsBusy)
        {
            return false;
        }

        var pages = 0;

        while (HasMore && !LoadFailed && !IsBusy && pages < maxPages)
        {
            pages++;
            await LoadMoreAsync(cancellationToken).ConfigureAwait(true);
        }

        // ★ 失败也要算「没拉全」：首屏就失败时 HasMore 停在 false（ReloadAsync 开头会把它清掉），
        //   只看 !HasMore 会把「什么都没拉到」当成「拉完了」。
        return !HasMore && !LoadFailed;
    }

    /// <summary>
    /// 设一个会牵动 <see cref="ShowEnd"/> / <see cref="ShowRetry"/> 的字段。
    /// </summary>
    /// <remarks>
    /// 那两个是算出来的、没有自己的存储字段，所以任何会影响它们的状态一变就得一并通知，
    /// 否则页脚的结束文案与「重试」按钮不会跟着刷新。
    /// </remarks>
    private void SetDerived(ref bool field, bool value, [CallerMemberName] string? propertyName = null)
    {
        if (SetProperty(ref field, value, propertyName))
        {
            OnPropertyChanged(nameof(ShowEnd));
            OnPropertyChanged(nameof(ShowRetry));
        }
    }

    private async Task AppendNextPageAsync(CancellationToken cancellationToken)
    {
        if (_cursor is null)
        {
            return;
        }

        var page = await _fetch(_cursor, cancellationToken).ConfigureAwait(true);

        foreach (var item in page.Items)
        {
            Items.Add(item);
        }

        HasMore = !_cursor.Exhausted && page.Items.Count > 0;

        // 只说条数。曾经在还有下一页时补一个「（滚动加载）」，那是滚到底自动翻页刚上线时
        // 用来提示行为变化的，现在列表末尾本来就有「没有更多了哦~」，这句纯属噪音。
        StatusText = Items.Count == 0
            ? EmptyText
            : _countUnit is null ? $"{Items.Count} 项" : $"共 {Items.Count} {_countUnit}";
    }
}
