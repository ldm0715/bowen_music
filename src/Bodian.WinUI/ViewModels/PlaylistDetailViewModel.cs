using Bodian.Core.Api;
using Bodian.Core.Models;
using Bodian.WinUI.Playback;
using Bodian.WinUI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 歌单详情页。
/// </summary>
/// <remarks>
/// <para>
/// 歌单对象由调用方传进来（侧栏本来就持有那份列表），所以这里**不需要再查一次歌单**，
/// 直接拿 <see cref="Playlist.Id"/> 去取曲目。
/// </para>
/// <para>
/// <b><c>source</c> 必须由调用方给</b>：侧栏里的自建歌单是 <c>5</c>，
/// 而发现页点进来的**公开歌单是 4**。填错的表现是「曲目列表是空的」——
/// 服务端对不上的 <c>source</c> 只回空、不报错。
/// </para>
/// </remarks>
public sealed partial class PlaylistDetailViewModel : PlaylistTracksViewModel
{
    private readonly int _source;
    private readonly IBodianApi _api;
    private readonly INoticeSink _notice;
    private readonly ILogger<PlaylistDetailViewModel> _logger;

    public PlaylistDetailViewModel(
        IBodianApi api,
        PlaybackCoordinator coordinator,
        Playlist playlist,
        int source,
        INoticeSink notice,
        ILogger<PlaylistDetailViewModel>? logger = null)
        : base(api, coordinator, logger ?? NullLogger<PlaylistDetailViewModel>.Instance)
    {
        ArgumentNullException.ThrowIfNull(playlist);
        ArgumentNullException.ThrowIfNull(notice);

        _api = api;
        _notice = notice;
        _logger = logger ?? NullLogger<PlaylistDetailViewModel>.Instance;
        Playlist = playlist;
        _source = source;
        Title = playlist.Name;
    }

    /// <summary>点进来的那个歌单。也用它的 id 作为导航身份的一部分。</summary>
    public Playlist Playlist { get; }

    /// <summary>
    /// 这个歌单的来源。**同时是取曲目的参数与导航身份的一部分** ——
    /// 同一个 id 在不同 source 下是不同的歌单，只按 id 判等会让两者互相顶掉。
    /// </summary>
    public int Source => _source;

    /// <inheritdoc />
    protected override int PlaylistSource => _source;

    /// <summary>
    /// 这个歌单当前是否已被我收藏。
    /// </summary>
    /// <remarks>
    /// <c>null</c> = 还没判定出来（未登录或读取失败），按钮按「未收藏」显示。
    /// 判据是歌单详情里的 <c>collectTime</c>，**不是 <c>isFond</c>** ——
    /// 见 <c>reverse/findings/13-collect-playlist-follow-artist.md</c> §3.3。
    /// </remarks>
    [ObservableProperty]
    public partial bool? IsCollected { get; set; }

    /// <summary>收藏/取消正在进行。<b>挡住重复点击</b> —— 服务端把 <c>op</c> 当一次设置，重发会打架。</summary>
    [ObservableProperty]
    public partial bool IsCollectBusy { get; set; }

    /// <summary>
    /// 歌单取不到曲目时的说明。
    /// </summary>
    /// <remarks>
    /// 自建歌单是可以被删掉的，而侧栏那份列表是启动时拉的 —— 期间用户在别处删了歌单，
    /// 点进来就会是空的。这条文案说的是「找不到」，不是「加载失败」。
    /// </remarks>
    protected override string MissingText => "这个歌单不存在，可能已经被删除了。";

    protected override string EmptyText => "这个歌单里还没有歌。";

    /// <summary>页面进入时调一次：曲目列表 + 收藏态。</summary>
    public async Task EnsureDetailLoadedAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(true);
        await LoadCollectStateAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// 收藏 / 取消收藏，并就地更新按钮状态。
    /// </summary>
    /// <remarks>
    /// <b>确认弹窗不在这里</b>：那是界面决策（要不要弹、按钮怎么摆），且 <c>XamlRoot</c>
    /// 拿不到 ViewModel 里来。取消操作由页面在调用前先确认，见
    /// <c>PlaylistDetailPage.OnCollectClick</c>。
    /// </remarks>
    public async Task SetCollectedAsync(bool collected, CancellationToken cancellationToken = default)
    {
        if (IsCollectBusy)
        {
            return;
        }

        IsCollectBusy = true;
        try
        {
            await _api.SetPlaylistCollectedAsync(Playlist.Id, _source, collected, cancellationToken)
                .ConfigureAwait(true);

            IsCollected = collected;
            _notice.Show(collected ? "已收藏" : "已取消收藏");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "歌单 {PlaylistId} {Operation}失败", Playlist.Id, collected ? "收藏" : "取消收藏");
            _notice.Show(collected ? "收藏失败" : "取消收藏失败");
        }
        finally
        {
            IsCollectBusy = false;
        }
    }

    protected override Task<Playlist?> ResolvePlaylistAsync(CancellationToken cancellationToken) =>
        Task.FromResult<Playlist?>(Playlist);

    /// <summary>
    /// 取这个歌单的收藏态（详情里的 <c>collectTime</c>）。
    /// </summary>
    /// <remarks>拿不到就保持 <c>null</c>，只记日志 —— 不因为一个可选的按钮状态打断整个页面。</remarks>
    private async Task LoadCollectStateAsync(CancellationToken cancellationToken)
    {
        try
        {
            IsCollected = await _api.IsPlaylistCollectedAsync(Playlist.Id, _source, cancellationToken)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载歌单 {PlaylistId} 收藏状态失败", Playlist.Id);
            IsCollected = null;
        }
    }
}
