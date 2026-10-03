using Bodian.Core.Api;
using Bodian.Core.Api.Paging;
using Bodian.Core.Models;
using Bodian.Core.Services;
using Bodian.WinUI.Playback;
using Bodian.WinUI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 歌手详情页。
/// </summary>
/// <remarks>
/// <para>
/// <b>三个页签各有自己的数据</b>，且都按需加载：歌曲进页面就拉，
/// 专辑与介绍要等用户切过去 —— 大多数人只看歌曲，没必要为一次点击付三个请求。
/// </para>
/// <para>
/// <b>详情是补的，不是必须的</b>：导航过来时只有 id、名字与头像（从曲目行进来时连计数都没有），
/// 别名、粉丝数、简介都要靠 <c>service/artist/{id}</c> 单独取一次。
/// 取不到就退回传进来的那份，页面照常可用。
/// </para>
/// </remarks>
public sealed partial class ArtistDetailViewModel : ObservableObject
{
    /// <summary>页签名。顺序与下标的含义绑死在 <see cref="SelectedTab"/> 上，改这里要一并改分支。</summary>
    private static readonly string[] TabHeaders = ["歌曲", "专辑", "介绍"];

    private readonly IBodianApi _api;
    private readonly PlaybackCoordinator _coordinator;
    private readonly BodianSession _session;
    private readonly IClipboardService _clipboard;
    private readonly INoticeSink _notice;
    private readonly ILogger<ArtistDetailViewModel> _logger;

    private bool _infoRequested;

    public ArtistDetailViewModel(
        IBodianApi api,
        PlaybackCoordinator coordinator,
        BodianSession session,
        IClipboardService clipboard,
        Artist artist,
        INoticeSink notice,
        ILogger<ArtistDetailViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(artist);
        ArgumentNullException.ThrowIfNull(notice);

        _api = api;
        _coordinator = coordinator;
        _session = session;
        _clipboard = clipboard;
        _notice = notice;
        _logger = logger ?? NullLogger<ArtistDetailViewModel>.Instance;

        Artist = artist;

        // ★ 两条都是 1 基（不是 search 那种 0 基）。
        //   写成 ZeroBased 时首屏照样正常 —— 服务端把 pn=0 当成第 1 页，不报错 ——
        //   但游标推进后发的是 pn=1，拿回来的还是第 1 页，追加进去正好翻倍。
        Tracks = new((cursor, token) => api.GetArtistTracksAsync(artist.Id, cursor, token),
            _logger, "歌手歌曲", "暂无歌曲", PagingConvention.OneBased);
        Albums = new((cursor, token) => api.GetArtistAlbumsAsync(artist.Id, cursor, token),
            _logger, "歌手专辑", "暂无专辑", PagingConvention.OneBased);
    }

    /// <summary>歌手。<b>详情拉回来后会整体换掉</b>，头部那几个绑定要写成 OneWay。</summary>
    [ObservableProperty]
    public partial Artist Artist { get; set; }

    public IReadOnlyList<string> Tabs => TabHeaders;

    /// <summary>当前页签的下标，与 <see cref="Tabs"/> 一一对应。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSongsTab))]
    [NotifyPropertyChangedFor(nameof(IsAlbumsTab))]
    [NotifyPropertyChangedFor(nameof(IsIntroTab))]
    public partial int SelectedTab { get; set; }

    // 三块内容都留在可视树上，只切可见性。
    // 用 Visibility 而不是换内容：换内容会让已经加载好的列表在来回切页签时重建，
    // 滚动位置与已加载的页全丢。三块都是惰性的，留着不花代价。
    public bool IsSongsTab => SelectedTab == 0;

    public bool IsAlbumsTab => SelectedTab == 1;

    public bool IsIntroTab => SelectedTab == 2;

    public PagedList<Track> Tracks { get; }

    public PagedList<Album> Albums { get; }

    /// <summary>页面进入时调一次：歌手信息与歌曲列表。</summary>
    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        await LoadInfoAsync(cancellationToken).ConfigureAwait(true);
        await Tracks.EnsureLoadedAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>点播。队列是当前已加载的歌曲列表。</summary>
    public async Task PlayAsync(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);

        var index = Tracks.Items.IndexOf(track);

        if (index >= 0)
        {
            await _coordinator.PlayFromAsync([.. Tracks.Items], index).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// 关注。
    /// </summary>
    /// <remarks>
    /// <b>本轮只做界面，不发写请求。</b> 关注走的是 <c>service/collect</c> 上另一条实现
    /// （<c>api_service.dart::submitUserSource</c>），报文与 <c>op</c> 方向都没实测过。
    /// 也没有读回「是否已关注」的路径（要读 <c>service/collect/7/list</c>），
    /// 所以按钮固定显示「关注」—— <b>不假造一个可能是错的已关注状态</b>。
    /// </remarks>
    [RelayCommand]
    private void Follow() => _notice.Show("关注功能暂未开放");

    /// <summary>
    /// 复制歌手分享链接。
    /// </summary>
    /// <remarks>
    /// <b>不做上报</b>，与专辑分享一致：<c>shareSource=1</c> 虽然已实测就是歌手，
    /// 但这一轮只做复制链接，所以服务端的分享数不会 +1。
    /// 复制失败吞掉异常只提示 —— 剪贴板拿不到不该让页面炸掉。
    /// </remarks>
    [RelayCommand]
    private void Share()
    {
        try
        {
            _clipboard.SetText(ShareLinks.BuildArtistLink(Artist.Id, _session.Uid));
            _notice.Show("链接已复制");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "复制歌手 {ArtistId} 分享链接失败", Artist.Id);
            _notice.Show("复制链接失败");
        }
    }

    /// <summary>
    /// 切页签时按需加载对应的那一块。
    /// </summary>
    /// <remarks>
    /// 出错不往外抛：<see cref="PagedList{T}"/> 自己会把失败写进状态文案与重试按钮，
    /// 信息那一路也一样（拿不到只是少几行字），都不该让页面炸掉。
    /// </remarks>
    partial void OnSelectedTabChanged(int value)
    {
        switch (value)
        {
            case 1:
                _ = Albums.EnsureLoadedAsync();
                break;

            case 2:
                _ = LoadInfoAsync(CancellationToken.None);
                break;
        }
    }

    /// <summary>
    /// 取歌手信息（别名 / 粉丝数 / 简介 / 两个计数）。
    /// </summary>
    /// <remarks>
    /// <b>只请求一次</b>，失败之后不再重试 —— 切页签会反复调到这里，
    /// 每次都重发的话，一个取不到的歌手会在每次切页时都发一次请求。
    /// </remarks>
    private async Task LoadInfoAsync(CancellationToken cancellationToken)
    {
        if (_infoRequested)
        {
            return;
        }

        _infoRequested = true;

        try
        {
            var info = await _api.GetArtistInfoAsync(Artist.Id, cancellationToken).ConfigureAwait(true);

            if (info is not null)
            {
                Artist = info;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载歌手 {ArtistId} 详情失败", Artist.Id);
        }
    }
}
