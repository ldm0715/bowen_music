using System.Collections.ObjectModel;
using Bodian.Core.Api;
using Bodian.Core.Models;
using Bodian.Core.Services.Abstractions;
using Bodian.WinUI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 曲目行「更多」菜单的状态与动作。
/// </summary>
/// <remarks>
/// <para>
/// <b>一次菜单打开用一个实例</b>：由 <c>TrackActionsService.Create</c> 现造，菜单关掉就作废。
/// 状态跟着这一行走，不需要「切歌失效」那一套。
/// </para>
/// <para>
/// <b>这里只依赖 Core 的模型与两个自制接口</b>（<see cref="ITrackNavigator"/>、
/// <see cref="INoticeSink"/>），不含任何 WinUI 类型 —— 于是它和那两个接口被 link 进离线测试工程，
/// 四条动作的分支（含未登录、写失败、缺 id）全都能单测。真正的导航与提示在 <c>TrackActionsService</c>。
/// </para>
/// </remarks>
public sealed partial class TrackActionsViewModel : ObservableObject
{
    /// <summary>空心心形。与播放条上那颗喜欢按钮同一对码位。</summary>
    private const string UnlikedIcon = "IconHeart";

    /// <summary>实心心形。</summary>
    private const string LikedIcon = "IconHeartFilled";

    /// <summary>加号。「添加到歌单」用它。</summary>
    private const string AddToPlaylistIcon = "IconAdd";

    /// <summary>人像轮廓，与搜索页的歌手结果同一个意象。</summary>
    private const string ArtistIcon = "IconPerson";

    /// <summary>唱片。</summary>
    private const string AlbumIcon = "IconAlbum";

    /// <summary>下一首。与播放条上那颗「下一首」同一码位。</summary>
    private const string PlayNextIcon = "IconNext";

    /// <summary>播放列表。与播放条上那颗「播放列表」同一码位。</summary>
    private const string AddToQueueIcon = "IconQueue";

    /// <summary>摄像机。</summary>
    private const string MvIcon = "IconMv";

    private readonly Track _track;
    private readonly IBodianApi _api;
    private readonly ILikedSongsService? _likedSongs;
    private readonly ITrackNavigator _navigator;
    private readonly INoticeSink _notice;
    private readonly IQueueSink? _queue;
    private readonly ILogger<TrackActionsViewModel> _logger;

    private readonly TrackMenuEntry _favoriteEntry = new()
    {
        Action = TrackMenuAction.Favorite,
        IconKey = UnlikedIcon,
        Text = "我喜欢",
    };

    private readonly TrackMenuEntry _playNextEntry = new()
    {
        Action = TrackMenuAction.PlayNext,
        IconKey = PlayNextIcon,
        Text = "下一首播放",
    };

    private readonly TrackMenuEntry _addToQueueEntry = new()
    {
        Action = TrackMenuAction.AddToQueue,
        IconKey = AddToQueueIcon,
        Text = "加入播放队列",
    };

    private readonly TrackMenuEntry _playlistEntry = new()
    {
        Action = TrackMenuAction.AddToPlaylist,
        IconKey = AddToPlaylistIcon,
        Text = "添加到歌单",
    };

    private readonly TrackMenuEntry _artistEntry = new()
    {
        Action = TrackMenuAction.Artist,
        IconKey = ArtistIcon,
        Text = "查看歌手",
    };

    private readonly TrackMenuEntry _mvEntry = new()
    {
        Action = TrackMenuAction.Mv,
        IconKey = MvIcon,
        Text = "播放 MV",
    };

    private readonly TrackMenuEntry _albumEntry = new()
    {
        Action = TrackMenuAction.Album,
        IconKey = AlbumIcon,
        Text = "查看专辑",
    };

    /// <summary>任何一次写操作正在跑。用来防止连点。</summary>
    private bool _busy;

    /// <param name="likedSongs">
    /// 喜欢状态的来源。为 <c>null</c> 时这一项只是不生效（离线宿主），不抛。
    /// </param>
    public TrackActionsViewModel(
        Track track,
        IBodianApi api,
        ITrackNavigator navigator,
        INoticeSink notice,
        IQueueSink? queue = null,
        ILikedSongsService? likedSongs = null,
        ILogger<TrackActionsViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(navigator);
        ArgumentNullException.ThrowIfNull(notice);

        _track = track;
        _api = api;
        _navigator = navigator;
        _notice = notice;
        _queue = queue;
        _likedSongs = likedSongs;
        _logger = logger ?? NullLogger<TrackActionsViewModel>.Instance;

        // 没带专辑 id 就灰着（点了也去不了，见 Track.AlbumId 的说明）。
        _albumEntry.IsEnabled = track.AlbumId > 0;

        // 没有曲目 id 就送不进队列，与「添加到歌单」同一条判据。
        _playNextEntry.IsEnabled = track.Id > 0;
        _addToQueueEntry.IsEnabled = track.Id > 0;

        var entries = new List<TrackMenuEntry>
        {
            _favoriteEntry,
            _playNextEntry,
            _addToQueueEntry,
            _playlistEntry,
        };

        // **没有 MV 就整项不出现**，不是灰着。判据 Track.HasMv（离线推断，不发请求）
        // 与曲目行上那颗 MV 角标是同一个 —— 角标已经不显示，菜单里却还挂一项点不动的
        // 「播放 MV」，两处说法就矛盾了。播放条那颗 MV 按钮同样是折叠，三处口径统一。
        // 「查看专辑」仍是灰着：它的情况相反 —— 那是个**数据缺失**（旧历史条目没存 albumId），
        // 项本身该在，只是这次点不了。
        if (track.HasMv)
        {
            entries.Add(_mvEntry);
        }

        entries.Add(_artistEntry);
        entries.Add(_albumEntry);

        MenuEntries = entries;
    }

    public IReadOnlyList<TrackMenuEntry> MenuEntries { get; }

    /// <summary>这首歌是否已在「我喜欢的」歌单里。菜单打开时拉一次。</summary>
    [ObservableProperty]
    public partial bool IsLiked { get; set; }

    /// <summary>歌单选择页是不是当前这一面。</summary>
    [ObservableProperty]
    public partial bool IsPickingPlaylist { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlaylistError))]
    public partial string PlaylistError { get; set; } = "";

    public bool HasPlaylistError => PlaylistError.Length > 0;

    [ObservableProperty]
    public partial bool IsPlaylistsBusy { get; set; }

    public ObservableCollection<Playlist> Playlists { get; } = [];

    /// <summary>
    /// 菜单打开时调一次，拉一次喜欢状态。
    /// </summary>
    /// <remarks>
    /// 状态没回来之前按「未喜欢」显示，回来再翻 —— 与播放条上那颗喜欢按钮同一套做法。
    /// </remarks>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_likedSongs is null || _track.Id <= 0)
        {
            return;
        }

        try
        {
            var liked = await _likedSongs.IsLikedAsync(_track.Id, cancellationToken).ConfigureAwait(true);
            // null 是「无法判定」（未登录 / 没有红心歌单 / 读失败），按未喜欢显示。
            IsLiked = liked == true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "曲目 {MusicId} 喜欢状态获取失败", _track.Id);
        }

        UpdateFavoriteEntry();
    }

    /// <summary>喜欢 / 取消喜欢。已喜欢时点按是<b>取消</b>，走删歌接口。</summary>
    public async Task ToggleFavoriteAsync(CancellationToken cancellationToken = default)
    {
        if (_likedSongs is null || _track.Id <= 0 || _busy)
        {
            return;
        }

        _busy = true;
        try
        {
            var desired = !IsLiked;
            var outcome = await _likedSongs.SetLikedAsync(_track.Id, desired, cancellationToken).ConfigureAwait(true);

            if (outcome == LikedSongsOutcome.Succeeded)
            {
                IsLiked = desired;
                UpdateFavoriteEntry();
                _notice.Show(desired ? "已喜欢" : "已取消喜欢");
                return;
            }

            // 文案与播放条上那颗喜欢按钮一字不差：同一个动作不该有两种说法。
            var message = outcome switch
            {
                LikedSongsOutcome.NotAuthenticated => "登录后可以喜欢。",
                LikedSongsOutcome.NoLikedPlaylist => "账号还没有「我喜欢的」歌单，暂时无法喜欢。",
                LikedSongsOutcome.AlreadyPending => "", // 连点，静默
                _ => "操作没成功，请稍后再试。",
            };

            if (message.Length > 0)
            {
                _notice.Show(message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "曲目 {MusicId} 喜欢写入失败", _track.Id);
            _notice.Show("操作没成功，请稍后再试。");
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// 插到播放队列的当前曲目之后。
    /// </summary>
    /// <remarks>
    /// 队列为空时这一首会直接开播 —— 见 <c>PlaybackCoordinator.PlayNextAsync</c>，
    /// 否则加了没反应，用户会以为没生效。
    /// </remarks>
    public async Task PlayNextAsync()
    {
        if (_queue is null || _track.Id <= 0 || _busy)
        {
            return;
        }

        _busy = true;
        try
        {
            await _queue.PlayNextAsync(_track).ConfigureAwait(true);
            _notice.Show("已设为下一首播放");
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>加到播放队列的队尾。空队列时同样会直接开播；队列里已有这一首时只提示、不重复添加。</summary>
    public async Task AddToQueueAsync()
    {
        if (_queue is null || _track.Id <= 0 || _busy)
        {
            return;
        }

        _busy = true;
        try
        {
            var added = await _queue.AddToQueueAsync(_track).ConfigureAwait(true);
            _notice.Show(added ? "已加入播放队列" : "这首歌已经在播放队列里");
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// 查看歌手。唯一且有效的那位直接跳，其余交给调用方弹窗。
    /// </summary>
    /// <remarks>
    /// <b>弹窗不在这里建</b>：本类不碰任何 WinUI 类型，<c>XamlRoot</c> 也拿不进来。
    /// 于是这里只负责「算出有哪些人」与「该不该直接跳」，需要用户选的那一份原样交回去。
    /// </remarks>
    /// <returns>
    /// 空列表 = 已经处理完（已直接跳转，或已提示「没有歌手信息」），调用方什么都不用做；
    /// 非空 = 请调用方弹窗展示这些候选人。
    /// </returns>
    public async Task<IReadOnlyList<ArtistChoice>> OpenArtistAsync(CancellationToken cancellationToken = default)
    {
        var artists = await GetArtistsAsync(cancellationToken).ConfigureAwait(true);

        if (artists.Count == 0)
        {
            _notice.Show("这首歌没有歌手信息。");
            return artists;
        }

        if (artists.Count == 1 && artists[0].IsAvailable)
        {
            _navigator.OpenArtist(artists[0].ToArtist());
            return [];
        }

        return artists;
    }

    /// <summary>弹窗里点中某一位后跳过去。没有有效 id 的直接忽略。</summary>
    public void OpenArtist(ArtistChoice artist)
    {
        ArgumentNullException.ThrowIfNull(artist);

        if (!artist.IsAvailable)
        {
            return;
        }

        _navigator.OpenArtist(artist.ToArtist());
    }

    /// <summary>
    /// 这首歌的全部歌手。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>顺序是可信度递减的</b>：曲目自带的艺人明细（服务端数组）最准，其次是补查到的详情，
    /// 最后才按 <c>&amp;</c> 拆艺人串 —— 那条<b>不可靠</b>，乐队本名里就有 <c>&amp;</c>
    /// （实测 <c>"Chase &amp; Status&amp;Skrillex"</c> 会被拆成三段）。
    /// </para>
    /// <para>
    /// 拆出来的条目一律没有 id，于是最坏情况只是多几格不可点的黑卡片，<b>绝不会跳错人</b>。
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<ArtistChoice>> GetArtistsAsync(CancellationToken cancellationToken = default)
    {
        var artists = Convert(_track.Artists);

        if (artists.Count > 0)
        {
            return artists;
        }

        // 「最近播放」是本地快照重建的，没有艺人明细（见 PlayHistoryEntry.ToTrack）；
        // 详情接口不要求登录，补一次代价很小。
        var detail = await FetchDetailAsync(cancellationToken).ConfigureAwait(true);

        if (detail is null)
        {
            // 没有曲目 id、请求失败、或有写操作在跑：退回本地那份艺人串快照，离线也有东西可显示。
            return SplitArtists(_track.ArtistText);
        }

        artists = Convert(detail.Artists);

        if (artists.Count > 0)
        {
            return artists;
        }

        return SplitArtists(string.IsNullOrWhiteSpace(detail.ArtistText) ? _track.ArtistText : detail.ArtistText);
    }

    /// <summary>查看专辑。曲目没带专辑 id 时这一项是灰的，正常点不到。</summary>
    public void OpenAlbum()
    {
        if (_track.AlbumId <= 0)
        {
            return;
        }

        // 专辑名与封面先按曲目带的填，详情页拉到数据后会覆盖 —— 比首屏空白好。
        _navigator.OpenAlbum(new Album
        {
            Id = _track.AlbumId,
            Name = _track.AlbumName ?? "",
            ArtistText = _track.ArtistText,
            CoverImage = _track.CoverImage,
        });
    }

    /// <summary>
    /// 播放这首歌的 MV。没有 MV 时菜单里根本没有这一项，正常点不到。
    /// </summary>
    /// <remarks>
    /// 这里**不取 MV 地址**：那要发请求，点菜单时不该等。目标页自己拉，
    /// 拉不到就按「这首歌没有 MV」收场。
    /// 开头那道 <c>HasMv</c> 守卫留着：菜单项已经不出现，但命令还能被别处调到。
    /// </remarks>
    public void OpenMv()
    {
        if (!_track.HasMv)
        {
            return;
        }

        _navigator.OpenMv(_track);
    }

    /// <summary>切到歌单选择页，并拉一次自建歌单。</summary>
    public async Task LoadPlaylistsAsync(CancellationToken cancellationToken = default)
    {
        IsPickingPlaylist = true;

        if (Playlists.Count > 0 || IsPlaylistsBusy)
        {
            return;
        }

        IsPlaylistsBusy = true;
        PlaylistError = "";
        try
        {
            var playlists = await _api.GetCreatedPlaylistsAsync(cancellationToken).ConfigureAwait(true);

            Playlists.Clear();
            foreach (var playlist in playlists)
            {
                Playlists.Add(playlist);
            }

            if (Playlists.Count == 0)
            {
                PlaylistError = "还没有自建歌单。";
            }
        }
        catch (InvalidOperationException)
        {
            // 未登录。没发请求就没有别的失败可能，所以单独认它。
            PlaylistError = "登录后可以添加到歌单。";
        }
        catch (OperationCanceledException)
        {
            // 菜单已经关掉，不用再改状态。
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "自建歌单读取失败");
            PlaylistError = "歌单读取失败，请稍后再试。";
        }
        finally
        {
            IsPlaylistsBusy = false;
        }
    }

    /// <summary>
    /// 把这首歌加进指定歌单。
    /// </summary>
    /// <returns>成功为 <c>true</c>，调用方据此关闭菜单；失败时错误留在面板内，菜单保持打开。</returns>
    public async Task<bool> AddToPlaylistAsync(Playlist playlist, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playlist);

        if (_track.Id <= 0 || _busy)
        {
            return false;
        }

        _busy = true;
        PlaylistError = "";
        try
        {
            await _api.AddPlaylistMusicAsync(playlist.Id, [_track.Id], cancellationToken).ConfigureAwait(true);
            _notice.Show($"已加入「{playlist.Name}」");
            return true;
        }
        catch (InvalidOperationException)
        {
            PlaylistError = "登录后可以添加到歌单。";
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "曲目 {MusicId} 加入歌单 {PlaylistId} 失败", _track.Id, playlist.Id);
            PlaylistError = "加入歌单失败，请稍后再试。";
            return false;
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>菜单关掉时复位，下次打开不残留上一首的加载态与错误。</summary>
    public void ResetPlaylistPicker()
    {
        IsPickingPlaylist = false;
        PlaylistError = "";
    }

    private static IReadOnlyList<ArtistChoice> Convert(IReadOnlyList<TrackArtist> artists) =>
        artists.Select(a => new ArtistChoice(a.Id, a.Name, a.Avatar)).ToArray();

    /// <summary>
    /// 把服务端的艺人串按 <c>&amp;</c> 拆开。去空白、丢空串、按名字去重。
    /// </summary>
    /// <remarks>
    /// <b>只在艺人明细缺失时才会走到这里。</b> 分隔符只有 <c>&amp;</c>：另一条拼接路径
    /// （<c>BodianApi.JoinArtists</c>）用的是顿号，但那条只在明细非空时执行，与这里互斥。
    /// </remarks>
    private static IReadOnlyList<ArtistChoice> SplitArtists(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var choices = new List<ArtistChoice>();

        foreach (var part in text.Split('&'))
        {
            var name = part.Trim();

            // 拆出来的没有 id，只能给成不可点的那一支。
            if (name.Length > 0 && seen.Add(name))
            {
                choices.Add(new ArtistChoice(0, name, null));
            }
        }

        return choices;
    }

    /// <summary>
    /// 现查一次曲目详情，供艺人明细缺失时用。
    /// </summary>
    /// <remarks>
    /// <b>非正 id 不查</b>（搜索页的性能样本、播放历史重建的曲目都可能没有 id），
    /// <b>正忙时也不查</b> —— 同一时刻只跑一次写操作，避免与喜欢 / 入队互相挤。
    /// </remarks>
    private async Task<Track?> FetchDetailAsync(CancellationToken cancellationToken)
    {
        if (_busy || _track.Id <= 0)
        {
            return null;
        }

        _busy = true;
        try
        {
            return await _api.GetTrackAsync(_track.Id, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "曲目 {MusicId} 详情补取失败", _track.Id);
            return null;
        }
        finally
        {
            _busy = false;
        }
    }

    private void UpdateFavoriteEntry()
    {
        _favoriteEntry.IconKey = IsLiked ? LikedIcon : UnlikedIcon;
        _favoriteEntry.Text = IsLiked ? "取消喜欢" : "我喜欢";
    }
}
