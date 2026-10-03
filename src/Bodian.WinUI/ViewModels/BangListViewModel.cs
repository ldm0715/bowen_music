using System.Collections.ObjectModel;
using Bodian.Core.Api;
using Bodian.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 榜列表页里的一个榜。
/// </summary>
/// <remarks>
/// <b>曲目直接用首页带来的预览，不为每个榜单独发请求。</b>
/// 完整榜单（实测 100 首）在 <c>service/bang/{id}/musics</c>，由详情页去取。
/// <para>
/// 一开始的设计是「每个榜再请求一次 <c>rn=10</c> 拿前十首」，那要 20 次请求才填满一页 ——
/// 既违背本项目「别把服务当压测」的底线，又因为守卫写错而整个卡住。
/// 改用预览之后整页只要 1 次请求。
/// </para>
/// </remarks>
public sealed partial class BangItemViewModel : ObservableObject
{
    public BangItemViewModel(Bang bang)
    {
        ArgumentNullException.ThrowIfNull(bang);

        Bang = bang;

        for (var i = 0; i < bang.PreviewTracks.Count; i++)
        {
            // 名次就是位次：预览是从第 1 名开始的。
            // 名次不补零 —— 「第 1 名」写成 01 是错的（曲目列表才补零）。
            Tracks.Add(new TrackRow
            {
                Source = bang.PreviewTracks[i],
                Ordinal = i + 1,
                PadOrdinal = false,
            });
        }
    }

    public Bang Bang { get; }

    /// <summary>
    /// 首页带来的那几首（实测 5 首），带名次。
    /// </summary>
    /// <remarks>
    /// 用 <see cref="TrackRow"/> 而不是 <see cref="RankedTrack"/>：预览行也要显示
    /// 「正在播放」，而那是一个随外部状态变化的可绑定属性，record 装不下。
    /// 行模板与曲目列表共用同一套三态序号列。
    /// </remarks>
    public ObservableCollection<TrackRow> Tracks { get; } = [];

    /// <summary>把「当前是哪一首」刷到预览行上。由页面在播放状态变化时调用。</summary>
    internal void SetCurrent(long? trackId)
    {
        foreach (var row in Tracks)
        {
            row.IsCurrent = trackId is not null && row.Source.Id == trackId;
        }
    }

    /// <summary>有没有完整榜单可看 —— 决定「更多」按钮显不显示。</summary>
    /// <remarks>
    /// 预览是**被服务端截断过的**，所以只要预览非空就一定有更多。
    /// 不用响应里的 <c>total</c> 判断：那个字段在这一族里并不总是给。
    /// </remarks>
    public bool HasMore => Tracks.Count > 0;

    public string Name => Bang.Name;

    public Uri? CoverImage => Bang.CoverImage;

    /// <summary><c>09-30更新</c> 这类。缺了就不显示。</summary>
    public string UpdateText => Bang.UpdateText;
}

/// <summary>榜列表页里的一组（置顶位 / 热力榜 / …）。</summary>
public sealed class BangSectionViewModel
{
    public BangSectionViewModel(BangSection section)
    {
        ArgumentNullException.ThrowIfNull(section);

        Title = section.Title;
        Bangs = [.. section.Bangs.Select(bang => new BangItemViewModel(bang))];
    }

    public string Title { get; }

    public IReadOnlyList<BangItemViewModel> Bangs { get; }
}

/// <summary>
/// 排行榜页（侧栏的独立一项）。
/// </summary>
/// <remarks>
/// <para>
/// <b>整页只要一次请求</b>：<c>service/home/bangNew</c> 一次返回所有分组与每个榜的前几首预览。
/// 完整榜单在详情页取。所以这里**没有懒加载，也不分页**。
/// </para>
/// <para>
/// 预览的条数由服务端决定（实测 5 首）。要更多就点榜上的「更多」进详情页。
/// </para>
/// </remarks>
public sealed partial class BangListViewModel : ObservableObject
{
    private readonly IBodianApi _api;
    private readonly ILogger<BangListViewModel> _logger;

    private bool _loaded;

    public BangListViewModel(IBodianApi api, ILogger<BangListViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(api);

        _api = api;
        _logger = logger ?? NullLogger<BangListViewModel>.Instance;
    }

    public ObservableCollection<BangSectionViewModel> Sections { get; } = [];
    // 标题、榜头与预览曲目各是一个可回收的条目，离屏榜单不创建 XAML 子树。
    public ObservableCollection<object> Rows { get; } = [];

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    /// <summary>首次进入时调。<b>已经加载过就什么都不做。</b></summary>
    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        IsBusy = true;
        StatusText = "正在加载排行榜…";

        try
        {
            var sections = await _api.GetBangSectionsAsync(cancellationToken).ConfigureAwait(true);

            Sections.Clear();
            Rows.Clear();
            foreach (var section in sections)
            {
                var group = new BangSectionViewModel(section);
                Sections.Add(group);
                Rows.Add(new ListSectionHeader { Title = group.Title });
                foreach (var bang in group.Bangs)
                {
                    Rows.Add(bang);
                    foreach (var track in bang.Tracks) Rows.Add(track);
                }
            }

            var bangs = Sections.Sum(section => section.Bangs.Count);

            StatusText = bangs == 0
                ? "这次没有取到榜单。"
                : $"{Sections.Count} 组、共 {bangs} 个榜";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载排行榜失败");

            // 失败要允许重试：把标志放回去，下次进来会再试一次。
            _loaded = false;
            StatusText = $"加载失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
