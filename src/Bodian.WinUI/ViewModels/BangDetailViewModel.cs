using Bodian.Core.Api;
using Bodian.Core.Api.Paging;
using Bodian.Core.Models;
using Bodian.WinUI.Playback;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 一个榜的详情页：完整榜单（实测 100 首），可翻页。
/// </summary>
/// <remarks>
/// 页号从 1 起、<c>rn</c> 取 30（与项目其余列表一致）。
/// </remarks>
public sealed partial class BangDetailViewModel : ObservableObject
{
    private readonly IBodianApi _api;
    private readonly PlaybackCoordinator _coordinator;

    public BangDetailViewModel(
        IBodianApi api,
        PlaybackCoordinator coordinator,
        Bang bang,
        ILogger<BangDetailViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(bang);

        _api = api;
        _coordinator = coordinator;
        Bang = bang;

        // 榜详情也要名次，所以包一层 RankedTrack。
        // 名次 = 这一页的起始偏移 + 页内下标 + 1 —— 用游标的 Offset，不能用已加载条数累加，
        // 否则服务端省略条目时名次会整体前移。
        Tracks = new PagedList<RankedTrack>(
            async (cursor, token) =>
            {
                var page = await api.GetBangTracksAsync(bang.Id, cursor, token).ConfigureAwait(false);

                return new PagedResult<RankedTrack>(
                    [.. page.Items.Select((track, index) => new RankedTrack(page.Offset + index + 1, track))],
                    page.Offset,
                    page.PageSize,
                    page.Total);
            },
            logger ?? NullLogger<BangDetailViewModel>.Instance,
            $"榜「{bang.Name}」",
            "这个榜暂时取不到曲目。");
    }

    public Bang Bang { get; }

    public PagedList<RankedTrack> Tracks { get; }

    public string Title => Bang.Name;

    public Uri? CoverImage => Bang.CoverImage;

    public string UpdateText => Bang.UpdateText;

    /// <summary>首次进入时调。</summary>
    public Task EnsureLoadedAsync(CancellationToken cancellationToken = default) =>
        Tracks.EnsureLoadedAsync(cancellationToken);

    /// <summary>点播榜里的第 N 首：队列就是这个榜，所以「下一首」在榜内有效。</summary>
    public async Task PlayAsync(RankedTrack entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var index = Tracks.Items.IndexOf(entry);

        if (index < 0)
        {
            return;
        }

        // 队列要的是纯曲目，名次是榜单自己的展示概念，不进队列。
        await _coordinator.PlayFromAsync([.. Tracks.Items.Select(entry => entry.Track)], index)
            .ConfigureAwait(true);
    }
}
