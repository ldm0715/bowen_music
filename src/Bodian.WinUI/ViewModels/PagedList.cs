using System.Collections.ObjectModel;
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

    private PagedCursor? _cursor;
    private bool _started;
    private bool _hasMore;
    private bool _isBusy;
    private string _statusText = "";

    /// <param name="fetch">取一页。调用方在这里拼请求。</param>
    /// <param name="what">日志与错误文案里用的名字，例如「已购单曲」。</param>
    /// <param name="emptyText">一条都没有时的说明。</param>
    public PagedList(
        Func<PagedCursor, CancellationToken, Task<PagedResult<T>>> fetch,
        ILogger logger,
        string what,
        string emptyText)
    {
        ArgumentNullException.ThrowIfNull(fetch);
        ArgumentNullException.ThrowIfNull(logger);

        _fetch = fetch;
        _logger = logger;
        _what = what;
        EmptyText = emptyText;

        // 手写命令而不是 [RelayCommand]：源生成器在泛型类上要额外折腾，这里两个命令不值得。
        LoadMoreCommand = new AsyncRelayCommand(LoadMoreAsync);
        ReloadCommand = new AsyncRelayCommand(ReloadAsync);
    }

    public ObservableCollection<T> Items { get; } = [];

    /// <summary>加载下一页。绑到页脚的「加载更多」按钮。</summary>
    public IAsyncRelayCommand LoadMoreCommand { get; }

    /// <summary>重新从第一页开始。</summary>
    public IAsyncRelayCommand ReloadCommand { get; }

    /// <summary>一条都没有时的说明。<b>由调用方给</b>：每节的措辞不一样。</summary>
    public string EmptyText { get; }

    public bool HasMore
    {
        get => _hasMore;
        private set => SetProperty(ref _hasMore, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

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
        Items.Clear();
        _cursor = new PagedCursor(PagingConvention.OneBased);

        try
        {
            await AppendNextPageAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载{What}失败", _what);
            StatusText = $"加载失败：{ex.Message}";
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

        try
        {
            await AppendNextPageAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载{What}的下一页失败", _what);
            StatusText = $"加载失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
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

        StatusText = Items.Count == 0
            ? EmptyText
            : $"{Items.Count} 项{(HasMore ? "（还有更多）" : "")}";
    }
}
