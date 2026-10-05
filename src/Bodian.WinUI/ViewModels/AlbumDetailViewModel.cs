using Bodian.Core.Api;
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
/// 专辑详情页。
/// </summary>
/// <remarks>
/// <para>
/// <b>两次请求</b>：一次取专辑信息（简介、发行日、歌手 id 与头像），一次取曲目。
/// 列表页点进来时已经知道专辑名与封面了，所以头部先显示得出来，
/// 简介那部分要等详情回来 —— 少一次「白屏等」。
/// </para>
/// <para>
/// 曲目**可分页**（<c>pn</c> 从 0），所以用 <see cref="PagedList{T}"/>；
/// 而详情是一次给全的，不走分页。
/// </para>
/// </remarks>
public sealed partial class AlbumDetailViewModel : ObservableObject
{
    private readonly IBodianApi _api;
    private readonly PlaybackCoordinator _coordinator;
    private readonly BodianSession _session;
    private readonly IClipboardService _clipboard;
    private readonly INoticeSink _notice;
    private readonly ILogger<AlbumDetailViewModel> _logger;

    private bool _infoLoaded;

    public AlbumDetailViewModel(
        IBodianApi api,
        PlaybackCoordinator coordinator,
        BodianSession session,
        IClipboardService clipboard,
        INoticeSink notice,
        Album album,
        ILogger<AlbumDetailViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(notice);
        ArgumentNullException.ThrowIfNull(album);

        _api = api;
        _coordinator = coordinator;
        _session = session;
        _clipboard = clipboard;
        _notice = notice;
        _logger = logger ?? NullLogger<AlbumDetailViewModel>.Instance;

        Album = album;

        Tracks = new PagedList<Track>(
            (cursor, token) => api.GetAlbumTracksAsync(album.Id, cursor, token),
            _logger,
            $"专辑「{album.Name}」",
            "这张专辑暂时取不到曲目。",
            // 1 基，理由同 ArtistDetailViewModel：pn=0 会被服务端当成第 1 页，
            // 于是首屏正常、下一页重复。
            Bodian.Core.Api.Paging.PagingConvention.OneBased);

        // 工具栏左侧那段文案读的是列表状态，列表一变就转发一次通知。
        Tracks.PropertyChanged += (_, _) => OnPropertyChanged(nameof(CountText));
    }

    /// <summary>
    /// 专辑。<b>详情拉回来后会整体换掉</b>。
    /// </summary>
    /// <remarks>
    /// 列表页带过来的那份是够用的（名字与封面已经是对的），但从曲目行的「查看专辑」
    /// 合成出来的那份<b>没有歌手 id</b>，头部那个歌手入口要靠详情补上，
    /// 所以这里不是「填完就不动」，而是整体替换。XAML 上的绑定要写成 OneWay。
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasArtist))]
    [NotifyPropertyChangedFor(nameof(CountText))]
    public partial Album Album { get; set; }

    public PagedList<Track> Tracks { get; }

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSubtitle))]
    public partial string Subtitle { get; set; } = "";

    /// <summary>发行日拼出来了没有。没有时那行整个不显示，不留一行空白。</summary>
    public bool HasSubtitle => Subtitle.Length > 0;

    /// <summary>
    /// 工具栏左边那行。有服务端的曲目总数就报它，否则让位给状态文案。
    /// </summary>
    /// <remarks>
    /// <b>为什么用服务端总数而不是已加载条数</b>：这个数原来就在页头副标题里
    /// （<c>2003-07-31 · 11 首</c>），首屏只拉回一页时它照样是 11。改成已加载条数
    /// 会让数字从 11 掉到 5，是看得见的回归。
    /// <para>
    /// 加载失败或一条都没拉到时报总数没用 —— 列表是空的，用户看不到对应内容，
    /// 那时「加载失败：…」「这张专辑暂时取不到曲目。」才是该说的话。
    /// </para>
    /// </remarks>
    public string CountText =>
        Album.MusicCount > 0 && !Tracks.LoadFailed && Tracks.Items.Count > 0
            ? Formats.TrackCount(Album.MusicCount)
            : Tracks.StatusText;

    /// <summary>专辑简介。**实测很长**（整篇企划文案），界面上折叠显示。</summary>
    [ObservableProperty]
    public partial string Description { get; set; } = "";

    [ObservableProperty]
    public partial bool HasDescription { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>「播放全部」正在把剩下的页拉完。与 <see cref="IsBusy"/> 分开，两者不是一件事。</summary>
    [ObservableProperty]
    public partial bool IsPlayingAll { get; set; }

    /// <summary>歌手 id 拿到了才显示头部那个歌手入口。</summary>
    public bool HasArtist => Album.ArtistId > 0;

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        await LoadInfoAsync(cancellationToken).ConfigureAwait(true);
        await Tracks.EnsureLoadedAsync(cancellationToken).ConfigureAwait(true);
        await LoadCollectStateAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>点播：加到队尾并立即播放它。要整张专辑入队走工具栏的「全部加入播放列表」或页头的「播放全部」。</summary>
    public async Task PlayAsync(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);

        await _coordinator.EnqueueAndPlayAsync(track).ConfigureAwait(true);
    }

    /// <summary>
    /// 播放全部。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>先把剩余的页拉完再播</b>：专辑首屏只回来一页（实测 5 首），
    /// 直接拿已加载的那几条排队列的话，11 首的专辑点一次只能听到第 5 首。
    /// </para>
    /// <para>
    /// 拉不完（撞上限或中途失败）也照常从第一首开始播 —— 队列短一点比什么都不播好，
    /// <see cref="PagedList{T}.LoadAllAsync"/> 已经把失败收在状态文案里了。
    /// </para>
    /// </remarks>
    [RelayCommand]
    private async Task PlayAllAsync()
    {
        if (IsPlayingAll)
        {
            return;
        }

        IsPlayingAll = true;

        try
        {
            await Tracks.LoadAllAsync().ConfigureAwait(true);

            if (Tracks.Items.Count > 0)
            {
                await _coordinator.PlayFromAsync([.. Tracks.Items], 0).ConfigureAwait(true);
            }
        }
        finally
        {
            IsPlayingAll = false;
        }
    }

    /// <summary>
    /// 这张专辑当前是否已被我收藏。
    /// </summary>
    /// <remarks>
    /// <c>null</c> = 还没判定出来（未登录或读取失败），按钮按「未收藏」显示。
    /// 专辑详情里**没有收藏标志**，判据走 <c>service/collect/multipleState?source=6</c>，
    /// 见 <c>reverse/findings/13-collect-playlist-follow-artist.md</c>。
    /// </remarks>
    [ObservableProperty]
    public partial bool? IsCollected { get; set; }

    /// <summary>收藏/取消正在进行。<b>挡住重复点击</b> —— 服务端把 <c>op</c> 当一次设置，重发会打架。</summary>
    [ObservableProperty]
    public partial bool IsCollectBusy { get; set; }

    /// <summary>
    /// 收藏 / 取消收藏，并就地更新按钮状态。
    /// </summary>
    /// <remarks>
    /// <b>确认弹窗不在这里</b>：那是界面决策，且 <c>XamlRoot</c> 拿不到 ViewModel 里来。
    /// 取消收藏由页面在调用前先确认，见 <c>AlbumDetailPage.OnCollectClick</c>。
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
            await _api.SetAlbumCollectedAsync(Album.Id, collected, cancellationToken).ConfigureAwait(true);

            IsCollected = collected;
            _notice.Show(collected ? "已收藏" : "已取消收藏", NoticeSeverity.Success);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "专辑 {AlbumId} {Operation}失败", Album.Id, collected ? "收藏" : "取消收藏");
            _notice.Show(collected ? "收藏失败" : "取消收藏失败", NoticeSeverity.Error);
        }
        finally
        {
            IsCollectBusy = false;
        }
    }

    /// <summary>
    /// 复制专辑分享链接。
    /// </summary>
    /// <remarks>
    /// <b>不做上报</b>：<c>service/share/text</c> 的 <c>shareSource</c> 只实测过
    /// <c>0</c>（歌曲）与 <c>1</c>（歌手），专辑取什么值没有证据，所以不猜。
    /// 代价是这里的分享不会让服务端分享数 +1 —— 这是有意的，不是漏做。
    /// </remarks>
    [RelayCommand]
    private void Share()
    {
        try
        {
            _clipboard.SetText(ShareLinks.BuildAlbumLink(Album.Id, _session.Uid));
            _notice.Show("链接已复制", NoticeSeverity.Success);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "复制专辑 {AlbumId} 分享链接失败", Album.Id);
            _notice.Show("复制链接失败", NoticeSeverity.Error);
        }
    }

    /// <summary>
    /// 取详情（简介 / 发行日 / 歌手 id 与头像）。
    /// </summary>
    /// <remarks>
    /// <b>失败不挡曲目列表</b>：简介拿不到只是少一段文字，
    /// 让整页都加载不出来是过度反应。所以这里自己吞异常、只记日志。
    /// </remarks>
    private async Task LoadInfoAsync(CancellationToken cancellationToken)
    {
        if (_infoLoaded)
        {
            return;
        }

        _infoLoaded = true;
        IsBusy = true;

        // 先用列表页带来的信息把头部填上，请求回来再覆盖。
        Title = Album.Name;
        Subtitle = BuildSubtitle(Album);

        try
        {
            var detail = await _api.GetAlbumAsync(Album.Id, cancellationToken).ConfigureAwait(true);

            if (detail is null)
            {
                return;
            }

            // 整体换掉：歌手 id 与头像只有详情会给，头部那个入口靠它。
            Album = detail;

            Title = detail.Name;
            Subtitle = BuildSubtitle(detail);
            Description = detail.Description;
            HasDescription = detail.HasDescription;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载专辑 {AlbumId} 详情失败", Album.Id);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 取这张专辑的收藏态（<c>service/collect/multipleState?source=6</c>）。
    /// </summary>
    /// <remarks>拿不到就保持 <c>null</c>，只记日志 —— 不因为一个可选的按钮状态打断整页。</remarks>
    private async Task LoadCollectStateAsync(CancellationToken cancellationToken)
    {
        try
        {
            IsCollected = await _api.IsAlbumCollectedAsync(Album.Id, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载专辑 {AlbumId} 收藏状态失败", Album.Id);
            IsCollected = null;
        }
    }

    /// <summary>
    /// <c>2003-07-31</c>。发行日一个字段，没有就整行不显示（<see cref="HasSubtitle"/>）。
    /// </summary>
    /// <remarks>
    /// <b>不再拼歌手名</b>：头部已经有一个可点的歌手入口了，
    /// 同一个人名在一屏里出现两次是重复信息。
    /// <para>
    /// <b>也不再拼曲目数</b>：已挪到曲目工具栏左侧（<see cref="CountText"/>）。
    /// </para>
    /// </remarks>
    private static string BuildSubtitle(Album album)
        => string.IsNullOrWhiteSpace(album.ReleaseDate) ? "" : album.ReleaseDate;
}
