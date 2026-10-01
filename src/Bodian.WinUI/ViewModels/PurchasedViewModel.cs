using Bodian.Core.Api;
using Bodian.Core.Models;
using Bodian.WinUI.Playback;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 「已购音乐」页：已购单曲 + 已购专辑。
/// </summary>
/// <remarks>
/// <para>
/// 两个列表放在同一页（官方桌面端的「已购」也是这么组织的：<c>purchased_music_page</c> 下辖
/// <c>purchased_single_view</c> 与 <c>purchased_album_view</c> 两个视图）。
/// 做成上下两节而不是切换页签：少一套选中状态，两个列表都一眼能看见。
/// </para>
/// <para>
/// <b>打开页面会并发发两个请求</b>（单曲与专辑各一页）。这与「不做启动时全量预取」不冲突 ——
/// 那条说的是别在启动时把六个入口全拉一遍；这里用户明确打开了这一页，这两个列表就是它的内容。
/// </para>
/// </remarks>
public sealed partial class PurchasedViewModel : ObservableObject
{
    private readonly PlaybackCoordinator _coordinator;

    public PurchasedViewModel(
        IBodianApi api,
        PlaybackCoordinator coordinator,
        ILogger<PurchasedViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(coordinator);

        _coordinator = coordinator;

        var log = logger ?? NullLogger<PurchasedViewModel>.Instance;

        Singles = new PagedList<Track>(
            api.GetPurchasedSinglesAsync,
            log,
            "已购单曲",
            "没有已购单曲。");

        Albums = new PagedList<Album>(
            api.GetPurchasedAlbumsAsync,
            log,
            "已购专辑",
            "没有已购专辑。");
    }

    public PagedList<Track> Singles { get; }

    public PagedList<Album> Albums { get; }

    /// <summary>首次进入时调。两个列表并行加载。</summary>
    public Task EnsureLoadedAsync(CancellationToken cancellationToken = default) =>
        Task.WhenAll(
            Singles.EnsureLoadedAsync(cancellationToken),
            Albums.EnsureLoadedAsync(cancellationToken));

    /// <summary>
    /// 点播已购单曲。
    /// </summary>
    /// <remarks>
    /// 队列就是「已购单曲」这一节：点第 N 首 → 整节入队并从第 N 首开始，
    /// 所以「下一首」在这一节内有效。
    /// </remarks>
    [RelayCommand]
    private async Task PlayAsync(Track? track)
    {
        if (track is null)
        {
            return;
        }

        var index = Singles.Items.IndexOf(track);

        if (index < 0)
        {
            return;
        }

        await _coordinator.PlayFromAsync([.. Singles.Items], index).ConfigureAwait(true);
    }
}
