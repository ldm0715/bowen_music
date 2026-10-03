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
    private const string UnlikedGlyph = "\uEB51";

    /// <summary>实心心形。</summary>
    private const string LikedGlyph = "\uEB52";

    /// <summary>加号。「添加到歌单」用它。</summary>
    private const string AddToPlaylistGlyph = "\uE710";

    /// <summary>人像轮廓，与搜索页的歌手结果同一个意象。</summary>
    private const string ArtistGlyph = "\uE77B";

    /// <summary>唱片。</summary>
    private const string AlbumGlyph = "\uE8FD";

    /// <summary>下一首。与播放条上那颗「下一首」同一码位。</summary>
    private const string PlayNextGlyph = "\uE893";

    /// <summary>播放列表。与播放条上那颗「播放列表」同一码位。</summary>
    private const string AddToQueueGlyph = "\uE142";

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
        Glyph = UnlikedGlyph,
        Text = "我喜欢",
    };

    private readonly TrackMenuEntry _playNextEntry = new()
    {
        Action = TrackMenuAction.PlayNext,
        Glyph = PlayNextGlyph,
        Text = "下一首播放",
    };

    private readonly TrackMenuEntry _addToQueueEntry = new()
    {
        Action = TrackMenuAction.AddToQueue,
        Glyph = AddToQueueGlyph,
        Text = "加入播放队列",
    };

    private readonly TrackMenuEntry _playlistEntry = new()
    {
        Action = TrackMenuAction.AddToPlaylist,
        Glyph = AddToPlaylistGlyph,
        Text = "添加到歌单",
    };

    private readonly TrackMenuEntry _artistEntry = new()
    {
        Action = TrackMenuAction.Artist,
        Glyph = ArtistGlyph,
        Text = "查看歌手",
    };

    private readonly TrackMenuEntry _albumEntry = new()
    {
        Action = TrackMenuAction.Album,
        Glyph = AlbumGlyph,
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

        MenuEntries =
        [
            _favoriteEntry,
            _playNextEntry,
            _addToQueueEntry,
            _playlistEntry,
            _artistEntry,
            _albumEntry,
        ];
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

    /// <summary>加到播放队列的队尾。空队列时同样会直接开播。</summary>
    public async Task AddToQueueAsync()
    {
        if (_queue is null || _track.Id <= 0 || _busy)
        {
            return;
        }

        _busy = true;
        try
        {
            await _queue.AddToQueueAsync(_track).ConfigureAwait(true);
            _notice.Show("已加入播放队列");
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// 查看歌手。
    /// </summary>
    /// <remarks>
    /// 曲目自带艺人明细时直接用；<b>为空时现查一次详情</b> ——「最近播放」是本地快照重建的，
    /// 那里没有艺人明细（见 <c>PlayHistoryEntry.ToTrack</c>），而详情接口不要求登录，补一次代价很小。
    /// </remarks>
    public async Task OpenArtistAsync(CancellationToken cancellationToken = default)
    {
        var artist = FirstArtist(_track) ?? await FetchFirstArtistAsync(cancellationToken).ConfigureAwait(true);

        if (artist is null)
        {
            _notice.Show("这首歌没有歌手信息。");
            return;
        }

        _navigator.OpenArtist(artist);
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

    private static Artist? FirstArtist(Track track)
    {
        if (track.Artists.Count == 0)
        {
            return null;
        }

        var artist = track.Artists[0];

        // id 无效的条目当成没有 —— 拿它去请求歌手歌曲只会得到空列表。
        return artist.Id <= 0 ? null : new Artist { Id = artist.Id, Name = artist.Name, CoverImage = artist.Avatar };
    }

    private async Task<Artist?> FetchFirstArtistAsync(CancellationToken cancellationToken)
    {
        if (_busy || _track.Id <= 0)
        {
            return null;
        }

        _busy = true;
        try
        {
            var detail = await _api.GetTrackAsync(_track.Id, cancellationToken).ConfigureAwait(true);
            return detail is null ? null : FirstArtist(detail);
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
        _favoriteEntry.Glyph = IsLiked ? LikedGlyph : UnlikedGlyph;
        _favoriteEntry.Text = IsLiked ? "取消喜欢" : "我喜欢";
    }
}
