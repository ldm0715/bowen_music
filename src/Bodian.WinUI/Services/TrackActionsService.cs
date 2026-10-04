using Bodian.Core.Api;
using Bodian.Core.Models;
using Bodian.Core.Services;
using Bodian.Core.Services.Abstractions;
using Bodian.WinUI.Playback;
using Bodian.WinUI.ViewModels;
using Bodian.WinUI.Views;
using Microsoft.Extensions.Logging;

namespace Bodian.WinUI.Services;

/// <summary>
/// 曲目级动作的装配点：把数据、导航与提示凑到一起。
/// </summary>
/// <remarks>
/// <para>
/// <b>两个消费方</b>：行尾「更多」菜单在这里现造一个 <see cref="TrackActionsViewModel"/>；
/// 列表级的 <see cref="ITrackBatchActions"/>（工具栏的批量入队 / 批量喜欢 / 批量入歌单）也由本类承担。
/// 后者本来该是另一个服务，但两边的依赖完全重合（<c>_api</c> / <c>_likedSongs</c> /
/// <c>_coordinator</c> / <c>_player</c>），拆开要多一份 DI 注册与一个 App 资源键，没有收益。
/// </para>
/// <para>
/// <b>为什么要有这一层</b>：<see cref="TrackActionsViewModel"/> 刻意不碰任何 WinUI 类型
/// —— 这样它才能被 link 进离线测试工程。代价是导航与提示必须从外面注入，这个类就是那个外面。
/// </para>
/// <para>
/// <b>走 App 资源而不是构造注入</b>：放它的行内控件是 XAML 实例化的，构造函数必须无参、
/// 拿不到 DI 容器。这与 <c>TrackListView.NowPlaying</c> 是同一处例外，理由见那里的注释。
/// </para>
/// </remarks>
public sealed class TrackActionsService : ITrackNavigator, INoticeSink, IQueueSink, ITrackBatchActions
{
    /// <summary>行内提示挂多久。播放条上那条提示平时要挂到下一首开播，这里不能那么久。</summary>
    private static readonly TimeSpan NoticeDuration = TimeSpan.FromSeconds(3);

    private readonly IBodianApi _api;
    private readonly ILikedSongsService _likedSongs;
    private readonly INavigationService _navigation;
    private readonly Func<Artist, ArtistDetailPage> _artistFactory;
    private readonly Func<Album, AlbumDetailPage> _albumFactory;
    private readonly Func<Track, MvPage> _mvFactory;
    private readonly PlayerViewModel _player;
    private readonly PlaybackCoordinator _coordinator;
    private readonly ILoggerFactory _loggerFactory;

    public TrackActionsService(
        IBodianApi api,
        ILikedSongsService likedSongs,
        INavigationService navigation,
        Func<Artist, ArtistDetailPage> artistFactory,
        Func<Album, AlbumDetailPage> albumFactory,
        Func<Track, MvPage> mvFactory,
        PlayerViewModel player,
        PlaybackCoordinator coordinator,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(likedSongs);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(artistFactory);
        ArgumentNullException.ThrowIfNull(albumFactory);
        ArgumentNullException.ThrowIfNull(mvFactory);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _api = api;
        _likedSongs = likedSongs;
        _navigation = navigation;
        _artistFactory = artistFactory;
        _albumFactory = albumFactory;
        _mvFactory = mvFactory;
        _player = player;
        _coordinator = coordinator;
        _loggerFactory = loggerFactory;
    }

    /// <summary>给一行曲目造菜单状态。行控件在 <c>Row</c> 变化时调它，旧实例丢掉即可。</summary>
    public TrackActionsViewModel Create(Track track) => new(
        track,
        _api,
        this,
        this,
        this,
        _likedSongs,
        _loggerFactory.CreateLogger<TrackActionsViewModel>());

    /// <remarks>
    /// <b>压栈而不是换根</b>：侧栏该继续高亮用户原来所在的那一页，
    /// 与搜索页点歌手/专辑结果时的行为一致。
    /// </remarks>
    void ITrackNavigator.OpenArtist(Artist artist) => _navigation.Navigate(_artistFactory(artist));

    /// <inheritdoc cref="ITrackNavigator.OpenArtist"/>
    void ITrackNavigator.OpenAlbum(Album album) => _navigation.Navigate(_albumFactory(album));

    /// <inheritdoc cref="ITrackNavigator.OpenArtist"/>
    void ITrackNavigator.OpenMv(Track track) => _navigation.Navigate(_mvFactory(track));

    void INoticeSink.Show(string message) => _player.TransientNotice(message, NoticeDuration);

    /// <remarks>
    /// 两条都落到同一个队列上：菜单里的「加入播放队列」与播放条上的「播放列表」看的是同一份数据。
    /// </remarks>
    Task IQueueSink.PlayNextAsync(Track track) => _coordinator.PlayNextAsync(track);

    /// <inheritdoc cref="IQueueSink.PlayNextAsync"/>
    Task<bool> IQueueSink.AddToQueueAsync(Track track) => _coordinator.AddToQueueAsync(track);

    // ── 列表级批量动作 ──────────────────────────────────────────────────────

    Task<int> ITrackBatchActions.AddToQueueAsync(IReadOnlyList<Track> tracks, CancellationToken cancellationToken)
        => _coordinator.AddToQueueAsync(tracks, cancellationToken);

    Task<LikedSongsBatchOutcome> ITrackBatchActions.SetLikedManyAsync(
        IReadOnlyList<Track> tracks, bool liked, IProgress<BatchProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        return _likedSongs.SetLikedManyAsync(TrackIds(tracks), liked, progress, cancellationToken);
    }

    Task<IReadOnlyList<Playlist>> ITrackBatchActions.GetCreatedPlaylistsAsync(CancellationToken cancellationToken)
        => _api.GetCreatedPlaylistsAsync(cancellationToken);

    async Task<BatchWriteResult> ITrackBatchActions.AddToPlaylistAsync(
        long playlistId, IReadOnlyList<Track> tracks,
        IProgress<BatchProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        var ids = TrackIds(tracks);

        if (ids.Count == 0)
        {
            return new BatchWriteResult(0, 0, false);
        }

        return await PlaylistMusicWriter.WriteAsync(
            ids,
            (chunk, token) => _api.AddPlaylistMusicAsync(playlistId, chunk, token),
            _loggerFactory.CreateLogger<TrackActionsService>(),
            progress,
            cancellationToken).ConfigureAwait(true);
    }

    async Task<BatchWriteResult> ITrackBatchActions.RemoveFromPlaylistAsync(
        long playlistId, IReadOnlyList<Track> tracks,
        IProgress<BatchProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        var ids = TrackIds(tracks);

        if (ids.Count == 0)
        {
            return new BatchWriteResult(0, 0, false);
        }

        return await PlaylistMusicWriter.WriteAsync(
            ids,
            (chunk, token) => _api.RemovePlaylistMusicAsync(playlistId, chunk, token),
            _loggerFactory.CreateLogger<TrackActionsService>(),
            progress,
            cancellationToken).ConfigureAwait(true);
    }

    void ITrackBatchActions.ShowNotice(string message) => ((INoticeSink)this).Show(message);

    /// <summary>
    /// 取需要提交的曲目 id。
    /// </summary>
    /// <remarks>
    /// <b>非正 id 一律剔掉</b>：搜索页的性能样本、播放历史重建的 Track 都可能没有有效 id，
    /// 拿它们去请求必然失败，还会让「成功 N 首」的账对不上。
    /// </remarks>
    private static List<long> TrackIds(IReadOnlyList<Track> tracks)
    {
        var ids = new List<long>(tracks.Count);

        foreach (var track in tracks)
        {
            if (track.Id > 0)
            {
                ids.Add(track.Id);
            }
        }

        return ids;
    }
}
