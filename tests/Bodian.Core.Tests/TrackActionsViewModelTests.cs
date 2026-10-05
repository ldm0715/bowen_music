using Bodian.Core.Models;
using Bodian.Core.Services;
using Bodian.Core.Services.Abstractions;
using Bodian.Core.Tests.Support;
using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
using Xunit;

namespace Bodian.Core.Tests;

/// <summary>
/// 曲目行「更多」菜单的六项动作，以及它们各自的失败路径。
/// </summary>
public sealed class TrackActionsViewModelTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Track Track(
        long id = 1,
        string? album = "专辑",
        long albumId = 0,
        bool hasMv = false,
        string artistText = "",
        params TrackArtist[] artists) => new()
    {
        Id = id,
        Title = $"Song {id}",
        ArtistText = artistText,
        AlbumName = album,
        AlbumId = albumId,
        HasMv = hasMv,
        Artists = artists,
    };

    private static TrackMenuEntry Entry(TrackActionsViewModel viewModel, TrackMenuAction action) =>
        viewModel.MenuEntries.Single(entry => entry.Action == action);

    // ── 我喜欢 ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Favorite_WritesBothWaysAndFlipsItsMenuEntry()
    {
        var liked = new LikedSongsStub();
        var notices = new Notices();
        var viewModel = new TrackActionsViewModel(Track(), new PlaybackApiStub(), new Navigator(), notices, likedSongs: liked);

        await viewModel.InitializeAsync(Ct);
        Assert.Equal("我喜欢", Entry(viewModel, TrackMenuAction.Favorite).Text);

        await viewModel.ToggleFavoriteAsync(Ct);
        Assert.True(viewModel.IsLiked);
        Assert.Equal("取消喜欢", Entry(viewModel, TrackMenuAction.Favorite).Text);
        Assert.Equal([(1L, true)], liked.Requests);
        Assert.Equal("已喜欢", notices.Last);

        // 已喜欢时点按是取消，走反向。
        await viewModel.ToggleFavoriteAsync(Ct);
        Assert.False(viewModel.IsLiked);
        Assert.Equal([(1L, true), (1L, false)], liked.Requests);
        Assert.Equal("已取消喜欢", notices.Last);
    }

    [Fact]
    public async Task Favorite_ReflectsTheKnownSetWhenTheMenuOpens()
    {
        var liked = new LikedSongsStub();
        liked.Liked.Add(7);

        var viewModel = new TrackActionsViewModel(
            Track(7), new PlaybackApiStub(), new Navigator(), new Notices(), likedSongs: liked);

        await viewModel.InitializeAsync(Ct);

        Assert.True(viewModel.IsLiked);
        Assert.Equal("取消喜欢", Entry(viewModel, TrackMenuAction.Favorite).Text);
    }

    [Theory]
    [InlineData(LikedSongsOutcome.NotAuthenticated, "登录后可以喜欢。")]
    [InlineData(LikedSongsOutcome.NoLikedPlaylist, "账号还没有「我喜欢的」歌单，暂时无法喜欢。")]
    [InlineData(LikedSongsOutcome.Failed, "操作没成功，请稍后再试。")]
    public async Task Favorite_ExplainsWhyItCouldNotWrite(LikedSongsOutcome outcome, string message)
    {
        var liked = new LikedSongsStub { Next = outcome };
        var notices = new Notices();
        var viewModel = new TrackActionsViewModel(Track(), new PlaybackApiStub(), new Navigator(), notices, likedSongs: liked);

        await viewModel.ToggleFavoriteAsync(Ct);

        Assert.False(viewModel.IsLiked);
        Assert.Equal("我喜欢", Entry(viewModel, TrackMenuAction.Favorite).Text);
        Assert.Equal(message, notices.Last);
    }

    /// <summary>连点两次时服务端只认一次，被忽略的那次不出提示（与播放条那颗按钮一致）。</summary>
    [Fact]
    public async Task Favorite_StaysQuietWhenTheWriteIsAlreadyPending()
    {
        var liked = new LikedSongsStub { Next = LikedSongsOutcome.AlreadyPending };
        var notices = new Notices();
        var viewModel = new TrackActionsViewModel(Track(), new PlaybackApiStub(), new Navigator(), notices, likedSongs: liked);

        await viewModel.ToggleFavoriteAsync(Ct);

        Assert.Equal("", notices.Last);
    }

    [Fact]
    public async Task Favorite_ReportsFailureWhenTheWriteThrows()
    {
        var notices = new Notices();
        var viewModel = new TrackActionsViewModel(
            Track(), new PlaybackApiStub(), new Navigator(), notices, likedSongs: new ThrowingLikedSongs());

        await viewModel.ToggleFavoriteAsync(Ct);

        Assert.False(viewModel.IsLiked);
        Assert.Equal("操作没成功，请稍后再试。", notices.Last);
    }

    /// <summary>无法判定（null）不等于「未喜欢」：菜单仍显示「我喜欢」，点了写 true。</summary>
    [Fact]
    public async Task Favorite_TreatsAnUnknownStateAsNotLikedButStillWritesTrue()
    {
        var liked = new UnknownLikedSongs();
        var viewModel = new TrackActionsViewModel(Track(3), new PlaybackApiStub(), new Navigator(), new Notices(), likedSongs: liked);

        await viewModel.InitializeAsync(Ct);
        Assert.False(viewModel.IsLiked);

        await viewModel.ToggleFavoriteAsync(Ct);

        Assert.Equal([(3L, true)], liked.Requests);
        Assert.True(viewModel.IsLiked);
    }

    /// <summary>没有喜欢服务（离线宿主）时什么也不做，不能假装写成功。</summary>
    [Fact]
    public async Task Favorite_WithoutTheServiceDoesNothing()
    {
        var notices = new Notices();
        var viewModel = new TrackActionsViewModel(Track(), new PlaybackApiStub(), new Navigator(), notices);

        await viewModel.ToggleFavoriteAsync(Ct);

        Assert.False(viewModel.IsLiked);
        Assert.Equal("", notices.Last);
    }

    // ── 加入播放队列 ────────────────────────────────────────────────────────

    /// <summary>
    /// 七项，且顺序固定。
    /// </summary>
    /// <remarks>
    /// 两条队列动作紧挨「我喜欢」，与写服务器的「添加到歌单」用位置自然隔开 ——
    /// 那两项都带「歌单/列表」字样，挨着摆最容易被点错。
    /// 「播放 MV」与「查看歌手/专辑」同属「跳到别处」，排在写动作之后。
    /// </remarks>
    [Fact]
    public void Menu_ListsTheSevenActionsInOrder()
    {
        var viewModel = new TrackActionsViewModel(
            Track(hasMv: true), new PlaybackApiStub(), new Navigator(), new Notices());

        Assert.Equal(
            [
                TrackMenuAction.Favorite,
                TrackMenuAction.PlayNext,
                TrackMenuAction.AddToQueue,
                TrackMenuAction.AddToPlaylist,
                TrackMenuAction.Mv,
                TrackMenuAction.Artist,
                TrackMenuAction.Album,
            ],
            viewModel.MenuEntries.Select(entry => entry.Action));

        Assert.Equal("下一首播放", Entry(viewModel, TrackMenuAction.PlayNext).Text);
        Assert.Equal("加入播放队列", Entry(viewModel, TrackMenuAction.AddToQueue).Text);
    }

    // ── 播放 MV ─────────────────────────────────────────────────────────────

    [Fact]
    public void Mv_OpensTheTrackThroughTheNavigator()
    {
        var navigator = new Navigator();
        var viewModel = new TrackActionsViewModel(
            Track(7, hasMv: true), new PlaybackApiStub(), navigator, new Notices());

        Assert.True(Entry(viewModel, TrackMenuAction.Mv).IsEnabled);

        viewModel.OpenMv();

        Assert.Equal([7L], navigator.OpenedMv);
    }

    /// <summary>
    /// 没有 MV 时这一项<b>整项不出现</b>，不是灰着，且点了也不出去。
    /// </summary>
    /// <remarks>
    /// 与「查看专辑」的取舍相反是有意的：MV 的判据 <c>Track.HasMv</c> 与曲目行上那颗
    /// MV 角标是同一个 —— 角标已经不显示，菜单里却还挂一项点不动的「播放 MV」，
    /// 两处说法就矛盾了。播放条上那颗 MV 按钮同样是折叠，三处口径就此统一。
    /// </remarks>
    [Fact]
    public void Mv_IsAbsentWithoutMv()
    {
        var navigator = new Navigator();
        var viewModel = new TrackActionsViewModel(Track(), new PlaybackApiStub(), navigator, new Notices());

        Assert.DoesNotContain(viewModel.MenuEntries, entry => entry.Action == TrackMenuAction.Mv);

        viewModel.OpenMv();

        Assert.Empty(navigator.OpenedMv);
    }

    [Fact]
    public async Task PlayNext_HandsTheTrackToTheQueue()
    {
        var queue = new Queue();
        var notices = new Notices();
        var viewModel = new TrackActionsViewModel(
            Track(4), new PlaybackApiStub(), new Navigator(), notices, queue);

        await viewModel.PlayNextAsync();

        Assert.Equal([4L], queue.PlayedNext);
        Assert.Empty(queue.Appended);
        Assert.Equal("已设为下一首播放", notices.Last);
    }

    [Fact]
    public async Task AddToQueue_HandsTheTrackToTheQueue()
    {
        var queue = new Queue();
        var notices = new Notices();
        var viewModel = new TrackActionsViewModel(
            Track(9), new PlaybackApiStub(), new Navigator(), notices, queue);

        await viewModel.AddToQueueAsync();

        Assert.Equal([9L], queue.Appended);
        Assert.Empty(queue.PlayedNext);
        Assert.Equal("已加入播放队列", notices.Last);
    }

    /// <summary>没有曲目 id 就送不进队列，与「查看专辑」缺 id 时同一套处理。</summary>
    [Fact]
    public void Queue_IsDisabledWithoutATrackId()
    {
        var viewModel = new TrackActionsViewModel(Track(id: 0), new PlaybackApiStub(), new Navigator(), new Notices());

        Assert.False(Entry(viewModel, TrackMenuAction.PlayNext).IsEnabled);
        Assert.False(Entry(viewModel, TrackMenuAction.AddToQueue).IsEnabled);
    }

    /// <summary>没有队列出口（离线宿主）时什么也不做，不能假装加成功了。</summary>
    [Fact]
    public async Task Queue_WithoutTheSinkDoesNothing()
    {
        var notices = new Notices();
        var viewModel = new TrackActionsViewModel(Track(4), new PlaybackApiStub(), new Navigator(), notices);

        await viewModel.PlayNextAsync();
        await viewModel.AddToQueueAsync();

        Assert.Equal("", notices.Last);
    }

    // ── 查看歌手 ────────────────────────────────────────────────────────────
    //
    // 约定：OpenArtistAsync 返回空列表 = 「ViewModel 已经处理完」（直接跳了，或已提示），
    // 调用方什么都不用做；返回非空 = 「请弹窗让用户挑」。

    [Fact]
    public async Task Artist_UsesTheArtistListTheTrackAlreadyCarries()
    {
        var navigator = new Navigator();
        var api = new PlaybackApiStub { GetTrack = (_, _) => throw new InvalidOperationException("不该发请求") };
        var viewModel = new TrackActionsViewModel(
            Track(artists: [new TrackArtist(42, "周杰伦", null)]), api, navigator, new Notices());

        Assert.Empty(await viewModel.OpenArtistAsync(Ct));

        Assert.Equal(42, navigator.Artist?.Id);
        Assert.Equal("周杰伦", navigator.Artist?.Name);
    }

    /// <summary>「最近播放」的行是本地快照重建的，没有艺人明细，要现查一次详情。</summary>
    [Fact]
    public async Task Artist_FallsBackToTheTrackDetailWhenThereIsNoList()
    {
        var requested = new List<long>();
        var api = new PlaybackApiStub
        {
            GetTrack = (id, _) =>
            {
                requested.Add(id);
                return Task.FromResult<Track?>(Track(id, artists: [new TrackArtist(9, "陈奕迅", null)]));
            },
        };
        var navigator = new Navigator();
        var viewModel = new TrackActionsViewModel(Track(5), api, navigator, new Notices());

        Assert.Empty(await viewModel.OpenArtistAsync(Ct));

        Assert.Equal([5L], requested);
        Assert.Equal(9, navigator.Artist?.Id);
    }

    [Fact]
    public async Task Artist_SaysSoWhenTheDetailHasNoneEither()
    {
        var api = new PlaybackApiStub { GetTrack = (id, _) => Task.FromResult<Track?>(Track(id)) };
        var notices = new Notices();
        var navigator = new Navigator();
        var viewModel = new TrackActionsViewModel(Track(), api, navigator, notices);

        Assert.Empty(await viewModel.OpenArtistAsync(Ct));

        Assert.Null(navigator.Artist);
        Assert.Equal("这首歌没有歌手信息。", notices.Last);
    }

    /// <summary>合唱：不能只跳第一个，把全部交回去让用户挑。</summary>
    [Fact]
    public async Task Artist_ReturnsEveryArtistWhenThereAreSeveral()
    {
        var navigator = new Navigator();
        var viewModel = new TrackActionsViewModel(
            Track(artists: [new TrackArtist(1, "Hawk Nelson", null), new TrackArtist(2, "Jonathan Steingard", null)]),
            new PlaybackApiStub(), navigator, new Notices());

        var artists = await viewModel.OpenArtistAsync(Ct);

        Assert.Equal(["Hawk Nelson", "Jonathan Steingard"], artists.Select(a => a.Name));
        Assert.All(artists, a => Assert.True(a.IsAvailable));

        // 没有直接跳走，选择权在弹窗里。
        Assert.Null(navigator.Artist);
    }

    /// <summary>唯一那位服务端找不到时也弹窗，就是一张不可点的黑卡片。</summary>
    [Fact]
    public async Task Artist_OffersASingleArtistWithNoIdAsABlackCard()
    {
        var notices = new Notices();
        var navigator = new Navigator();
        var viewModel = new TrackActionsViewModel(
            Track(artists: [new TrackArtist(0, "佚名", null)]), new PlaybackApiStub(), navigator, notices);

        var artists = await viewModel.OpenArtistAsync(Ct);

        Assert.Single(artists);
        Assert.False(artists[0].IsAvailable);
        Assert.Null(navigator.Artist);

        // 有东西可显示，就不该说「没有歌手信息」。
        Assert.Equal("", notices.Last);
    }

    /// <summary>
    /// 曲目与详情都没有艺人明细时，按 &amp; 拆艺人串兜底。
    /// </summary>
    /// <remarks>
    /// 这条同时钉住「详情串非空才优先用它」：详情返回的是空串，若不去判空，
    /// 本地那份艺人串就被吞掉了。拆出来的条目一律无 id —— 乐队本名里带 &amp; 的
    /// （Chase &amp; Status&amp;Skrillex）会被拆错，所以绝不能让它可点。
    /// </remarks>
    [Fact]
    public async Task Artist_SplitsTheArtistTextWhenNeitherTheTrackNorTheDetailHasAList()
    {
        var api = new PlaybackApiStub { GetTrack = (id, _) => Task.FromResult<Track?>(Track(id)) };
        var viewModel = new TrackActionsViewModel(
            Track(artistText: "Chase & Status&Skrillex"), api, new Navigator(), new Notices());

        var artists = await viewModel.OpenArtistAsync(Ct);

        Assert.Equal(["Chase", "Status", "Skrillex"], artists.Select(a => a.Name));
        Assert.All(artists, a => Assert.False(a.IsAvailable));
    }

    [Fact]
    public async Task Artist_TrimsAndDropsDuplicatesWhenSplitting()
    {
        var api = new PlaybackApiStub { GetTrack = (id, _) => Task.FromResult<Track?>(Track(id)) };
        var viewModel = new TrackActionsViewModel(
            Track(artistText: " 周杰伦 & 周杰伦& 费玉清 "), api, new Navigator(), new Notices());

        var artists = await viewModel.OpenArtistAsync(Ct);

        Assert.Equal(["周杰伦", "费玉清"], artists.Select(a => a.Name));
    }

    /// <summary>没有有效曲目 id 就不该发详情请求（搜索页的性能样本、历史条目都可能是 0）。</summary>
    [Fact]
    public async Task Artist_WithoutATrackIdDoesNotAskForTheDetail()
    {
        var api = new PlaybackApiStub { GetTrack = (_, _) => throw new InvalidOperationException("不该发请求") };
        var viewModel = new TrackActionsViewModel(
            Track(id: 0, artistText: "周杰伦"), api, new Navigator(), new Notices());

        var artists = await viewModel.OpenArtistAsync(Ct);

        Assert.Single(artists);
        Assert.Equal("周杰伦", artists[0].Name);
    }

    [Fact]
    public async Task Artist_SaysThereIsNothingWhenTheTextHasOnlySeparators()
    {
        var api = new PlaybackApiStub { GetTrack = (id, _) => Task.FromResult<Track?>(Track(id)) };
        var notices = new Notices();
        var viewModel = new TrackActionsViewModel(
            Track(artistText: " & & "), api, new Navigator(), notices);

        Assert.Empty(await viewModel.OpenArtistAsync(Ct));

        Assert.Equal("这首歌没有歌手信息。", notices.Last);
    }

    /// <summary>弹窗里点中某一位之后跳过去；没有 id 的那位点了不跳。</summary>
    [Fact]
    public void Artist_OpensThePickedArtistAndIgnoresAnIdlessOne()
    {
        var navigator = new Navigator();
        var viewModel = new TrackActionsViewModel(Track(), new PlaybackApiStub(), navigator, new Notices());

        viewModel.OpenArtist(new ArtistChoice(7, "陈奕迅", null));
        Assert.Equal(7, navigator.Artist?.Id);

        viewModel.OpenArtist(new ArtistChoice(0, "佚名", null));
        Assert.Equal(7, navigator.Artist?.Id);
    }

    // ── 查看专辑 ────────────────────────────────────────────────────────────

    /// <summary>没有专辑 id 的曲目（旧的历史条目）这一项是灰的，点了也去不了。</summary>
    [Fact]
    public void Album_IsDisabledWithoutAnAlbumId()
    {
        var navigator = new Navigator();
        var viewModel = new TrackActionsViewModel(Track(albumId: 0), new PlaybackApiStub(), navigator, new Notices());

        Assert.False(Entry(viewModel, TrackMenuAction.Album).IsEnabled);

        viewModel.OpenAlbum();
        Assert.Null(navigator.Album);
    }

    [Fact]
    public void Album_OpensWithWhatTheTrackCarries()
    {
        var navigator = new Navigator();
        var viewModel = new TrackActionsViewModel(
            Track(albumId: 88, album: "叶惠美"), new PlaybackApiStub(), navigator, new Notices());

        Assert.True(Entry(viewModel, TrackMenuAction.Album).IsEnabled);

        viewModel.OpenAlbum();

        Assert.Equal(88, navigator.Album?.Id);
        Assert.Equal("叶惠美", navigator.Album?.Name);
    }

    // ── 添加到歌单 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Playlists_ShowsTheAccountsOwnLists()
    {
        var api = new PlaybackApiStub
        {
            GetCreatedPlaylists = _ => Task.FromResult<IReadOnlyList<Playlist>>(
                [new Playlist { Id = 5, Name = "通勤" }]),
        };
        var viewModel = new TrackActionsViewModel(Track(), api, new Navigator(), new Notices());

        await viewModel.LoadPlaylistsAsync(Ct);

        Assert.True(viewModel.IsPickingPlaylist);
        Assert.Equal("通勤", Assert.Single(viewModel.Playlists).Name);
        Assert.False(viewModel.HasPlaylistError);
    }

    [Fact]
    public async Task Playlists_ExplainsWhenNotSignedIn()
    {
        var api = new PlaybackApiStub
        {
            GetCreatedPlaylists = _ => throw new InvalidOperationException("未登录"),
        };
        var viewModel = new TrackActionsViewModel(Track(), api, new Navigator(), new Notices());

        await viewModel.LoadPlaylistsAsync(Ct);

        Assert.True(viewModel.IsPickingPlaylist);
        Assert.Empty(viewModel.Playlists);
        Assert.Equal("登录后可以添加到歌单。", viewModel.PlaylistError);
    }

    [Fact]
    public async Task Playlists_SaysWhenTheAccountHasNone()
    {
        var api = new PlaybackApiStub
        {
            GetCreatedPlaylists = _ => Task.FromResult<IReadOnlyList<Playlist>>([]),
        };
        var viewModel = new TrackActionsViewModel(Track(), api, new Navigator(), new Notices());

        await viewModel.LoadPlaylistsAsync(Ct);

        Assert.Equal("还没有自建歌单。", viewModel.PlaylistError);
    }

    [Fact]
    public async Task AddToPlaylist_WritesJustThisTrackAndNamesWhereItWent()
    {
        var added = new List<(long PlaylistId, IReadOnlyList<long> MusicIds)>();
        var api = new PlaybackApiStub
        {
            AddPlaylistMusic = (playlistId, musicIds, _) =>
            {
                added.Add((playlistId, musicIds));
                return Task.CompletedTask;
            },
        };
        var notices = new Notices();
        var viewModel = new TrackActionsViewModel(Track(7), api, new Navigator(), notices);

        var succeeded = await viewModel.AddToPlaylistAsync(new Playlist { Id = 5, Name = "通勤" }, Ct);

        Assert.True(succeeded);
        var request = Assert.Single(added);
        Assert.Equal(5, request.PlaylistId);
        Assert.Equal([7L], request.MusicIds);
        Assert.Equal("已加入「通勤」", notices.Last);
    }

    /// <summary>写失败时菜单不能关：错误就显示在这个面板里，关掉用户就看不见了。</summary>
    [Fact]
    public async Task AddToPlaylist_KeepsThePanelOpenWithTheReason()
    {
        var api = new PlaybackApiStub
        {
            AddPlaylistMusic = (_, _, _) => throw new HttpRequestException("网络不可用"),
        };
        var notices = new Notices();
        var viewModel = new TrackActionsViewModel(Track(7), api, new Navigator(), notices);

        var succeeded = await viewModel.AddToPlaylistAsync(new Playlist { Id = 5, Name = "通勤" }, Ct);

        Assert.False(succeeded);
        Assert.Equal("加入歌单失败，请稍后再试。", viewModel.PlaylistError);
        Assert.Equal("", notices.Last);
    }

    [Fact]
    public async Task ResettingThePickerReturnsToTheMenuAndDropsTheError()
    {
        var api = new PlaybackApiStub
        {
            GetCreatedPlaylists = _ => throw new InvalidOperationException("未登录"),
        };
        var viewModel = new TrackActionsViewModel(Track(), api, new Navigator(), new Notices());
        await viewModel.LoadPlaylistsAsync(Ct);

        viewModel.ResetPlaylistPicker();

        Assert.False(viewModel.IsPickingPlaylist);
        Assert.False(viewModel.HasPlaylistError);
    }

    // ── 假件 ────────────────────────────────────────────────────────────────

    private sealed class Navigator : ITrackNavigator
    {
        public Artist? Artist { get; private set; }

        public Album? Album { get; private set; }

        public List<long> OpenedMv { get; } = [];

        public void OpenArtist(Artist artist) => Artist = artist;

        public void OpenAlbum(Album album) => Album = album;

        public void OpenMv(Track track) => OpenedMv.Add(track.Id);
    }

    private sealed class Notices : INoticeSink
    {
        public string Last { get; private set; } = "";

        public void Show(string message) => Last = message;
    }

    private sealed class Queue : IQueueSink
    {
        public List<long> PlayedNext { get; } = [];

        public List<long> Appended { get; } = [];

        public Task PlayNextAsync(Track track)
        {
            PlayedNext.Add(track.Id);

            return Task.CompletedTask;
        }

        public Task<bool> AddToQueueAsync(Track track)
        {
            Appended.Add(track.Id);

            return Task.FromResult(true);
        }
    }

    private sealed class ThrowingLikedSongs : ILikedSongsService
    {
        public Task<bool?> IsLikedAsync(long musicId, CancellationToken cancellationToken = default)
            => Task.FromResult<bool?>(false);

        public Task<LikedSongsOutcome> SetLikedAsync(long musicId, bool liked,
            CancellationToken cancellationToken = default)
            => throw new HttpRequestException("网络不可用");

        public Task<LikedSongsBatchOutcome> SetLikedManyAsync(
            IReadOnlyList<long> musicIds, bool liked = true,
            IProgress<BatchProgress>? progress = null,
            CancellationToken cancellationToken = default)
            => throw new HttpRequestException("网络不可用");
    }

    private sealed class UnknownLikedSongs : ILikedSongsService
    {
        public List<(long Id, bool Liked)> Requests { get; } = [];

        public Task<bool?> IsLikedAsync(long musicId, CancellationToken cancellationToken = default)
            => Task.FromResult<bool?>(null);

        public Task<LikedSongsOutcome> SetLikedAsync(long musicId, bool liked,
            CancellationToken cancellationToken = default)
        {
            Requests.Add((musicId, liked));
            return Task.FromResult(LikedSongsOutcome.Succeeded);
        }

        public Task<LikedSongsBatchOutcome> SetLikedManyAsync(
            IReadOnlyList<long> musicIds, bool liked = true,
            IProgress<BatchProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            foreach (var id in musicIds)
            {
                Requests.Add((id, liked));
            }

            return Task.FromResult(new LikedSongsBatchOutcome(
                LikedSongsOutcome.Succeeded, musicIds.Count, 0, false));
        }
    }
}
