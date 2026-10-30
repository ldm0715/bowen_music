using System.Diagnostics;
using Bowen.Core.Api;
using Bowen.Core.Api.Paging;
using Bowen.Core.Models;
using Bowen.Core.Models.Home;
using Bowen.WinUI.ViewModels;
using Bowen.WinUI.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Bowen.WinUI.Services;

/// <summary>可重复的大数据 UI 验证入口；只有显式设置诊断环境变量时启用。</summary>
internal static class PerformanceScenarios
{
    public static async Task RunAsync(IServiceProvider services)
    {
        if (Environment.GetEnvironmentVariable("BODIAN_LYRICS_DIAGNOSTICS") != "1") return;
        switch (Environment.GetEnvironmentVariable("BODIAN_UI_PERF_SCENARIO"))
        {
            case "search-list": await RunSearchListAsync(services); break;
            case "browse-loop": await RunBrowseLoopAsync(services); break;
            case "daily-loop": await RunDailyLoopAsync(services); break;
        }
    }

    private static async Task RunSearchListAsync(IServiceProvider services)
    {
        var count = int.TryParse(Environment.GetEnvironmentVariable("BODIAN_UI_PERF_ITEMS"), out var requested)
            ? Math.Clamp(requested, 1, 50000) : 10000;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("PerformanceScenarios");
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            var start = Stopwatch.GetTimestamp();
            services.GetRequiredService<SearchViewModel>().PopulatePerformanceSample(count);
            services.GetRequiredService<INavigationService>().NavigateRoot<SearchPage>();
            logger.LogInformation("性能场景 search-list：{Count} 首，准备 {Elapsed:F2} ms", count, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
        catch (Exception exception) { logger.LogError(exception, "启动本机 UI 性能场景失败"); }
    }

    /// <summary>
    /// 复现日常浏览：排行榜为根，依次打开专辑、歌手、歌单再逐层返回，最后打开一个榜单详情再返回。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 每轮使用不同的专辑、歌手、歌单和榜单，与真实浏览一样每次都是新页面。
    /// 页面全部经由 DI 中的工厂创建，走正式导航入口。
    /// </para>
    /// <para>
    /// <c>BODIAN_NAV_DROP_HISTORY=1</c> 时详情页一律换根打开，返回栈始终为空，
    /// 用来确认「页面离开导航栈后能否被回收」。
    /// <c>BODIAN_MEMORY_COLLECT_EACH_ROUND=1</c> 时每轮结束做一次诊断回收，
    /// 用来区分「仍被引用」与「只是 GC 还没跑」。
    /// </para>
    /// <para>
    /// 不播放、不写搜索历史、不做任何写操作。每页停留 <see cref="Dwell"/>，请求数与手动浏览相当。
    /// </para>
    /// </remarks>
    private static async Task RunBrowseLoopAsync(IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("PerformanceScenarios");
        var rounds = int.TryParse(Environment.GetEnvironmentVariable("BODIAN_UI_PERF_ROUNDS"), out var requested)
            ? Math.Clamp(requested, 1, 30) : 20;
        var keyword = Environment.GetEnvironmentVariable("BODIAN_UI_PERF_KEYWORD") is { Length: > 0 } value ? value : "周杰伦";
        var dropHistory = Environment.GetEnvironmentVariable("BODIAN_NAV_DROP_HISTORY") == "1";
        var collectEachRound = Environment.GetEnvironmentVariable("BODIAN_MEMORY_COLLECT_EACH_ROUND") == "1";
        var extended = Environment.GetEnvironmentVariable("BODIAN_UI_PERF_EXTENDED") == "1";
        try
        {
            var api = services.GetRequiredService<IBodianApi>();
            var navigation = services.GetRequiredService<INavigationService>();
            var albumPage = services.GetRequiredService<Func<Album, AlbumDetailPage>>();
            var artistPage = services.GetRequiredService<Func<Artist, ArtistDetailPage>>();
            var playlistPage = services.GetRequiredService<Func<Playlist, int, PlaylistDetailPage>>();
            var bangPage = services.GetRequiredService<Func<Bang, BangDetailPage>>();
            var player = services.GetRequiredService<PlayerViewModel>();

            await Task.Delay(TimeSpan.FromSeconds(3));
            var albums = await CollectAsync((cursor, token) => api.SearchAlbumsAsync(keyword, cursor, token), extended ? 3 : 1);
            var artists = await CollectAsync((cursor, token) => api.SearchArtistsAsync(keyword, cursor, token), extended ? 2 : 1);
            var playlists = await CollectAsync((cursor, token) => api.SearchPlaylistsAsync(keyword, cursor, token), extended ? 3 : 1);
            var bangs = (await api.GetBangSectionsAsync()).SelectMany(section => section.Bangs).ToList();
            if (albums.Count == 0 || artists.Count == 0 || playlists.Count == 0 || bangs.Count == 0)
            {
                logger.LogWarning("性能场景 browse-loop：素材不足（专辑 {Albums}，歌手 {Artists}，歌单 {Playlists}，榜 {Bangs}）",
                    albums.Count, artists.Count, playlists.Count, bangs.Count);
                return;
            }
            logger.LogInformation("性能场景 browse-loop：{Rounds} 轮，丢弃历史 {Drop}，素材 专辑 {Albums} 歌手 {Artists} 歌单 {Playlists} 榜 {Bangs}",
                rounds, dropHistory, albums.Count, artists.Count, playlists.Count, bangs.Count);

            navigation.NavigateRoot<BangListPage>();
            await Task.Delay(Dwell);
            PageLifetimeDiagnostics.Report(logger, "初始");

            for (var round = 0; round < rounds; round++)
            {
                var details = new Page[]
                {
                    albumPage(albums[round % albums.Count]),
                    artistPage(artists[round % artists.Count]),
                    playlistPage(playlists[round % playlists.Count], playlists[round % playlists.Count].SourceType),
                };
                foreach (var page in details)
                {
                    if (dropHistory) navigation.NavigateRoot(page);
                    else navigation.Navigate(page);
                    await Task.Delay(Dwell);
                    if (extended) await ExerciseAsync(page, round, player, logger);
                }
                if (dropHistory) navigation.NavigateRoot<BangListPage>();
                else
                    for (var step = 0; step < details.Length; step++)
                    {
                        navigation.GoBack();
                        await Task.Delay(Step);
                    }
                details = [];
                await Task.Delay(Step);

                var bang = bangPage(bangs[round % bangs.Count]);
                if (dropHistory) navigation.NavigateRoot(bang);
                else navigation.Navigate(bang);
                await Task.Delay(Dwell);
                if (dropHistory) navigation.NavigateRoot<BangListPage>();
                else navigation.GoBack();
                bang = null;
                await Task.Delay(Step);

                PageLifetimeDiagnostics.Report(logger, $"第 {round + 1} 轮");
                if (collectEachRound)
                {
                    await PageLifetimeDiagnostics.CollectAsync();
                    PageLifetimeDiagnostics.Report(logger, $"第 {round + 1} 轮回收后");
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(10));
            PageLifetimeDiagnostics.Report(logger, "结束后静置 10 秒");
            await PageLifetimeDiagnostics.CollectAsync();
            PageLifetimeDiagnostics.Report(logger, "诊断回收后");
            logger.LogInformation("性能场景 browse-loop 完成");
        }
        catch (Exception exception) { logger.LogError(exception, "性能场景 browse-loop 失败"); }
    }

    /// <summary>
    /// 复现一次完整的日常使用：静音播放一首歌，打开歌词页和播放列表，然后依次走一遍
    /// 排行榜每个榜、发现页的歌单卡片、收藏的每张专辑（连同其歌手）、我喜欢的、关注的每位歌手。
    /// </summary>
    /// <remarks>
    /// 每轮走同一条路线，与用户反复做同一套操作一致；轮数取 <c>BODIAN_UI_PERF_ROUNDS</c>。
    /// 每类详情最多打开 <see cref="DailyItemLimit"/> 个，免得收藏很多时单轮过长。
    /// 只读浏览，播放前把音量设为 0；不收藏、不改歌单。
    /// </remarks>
    private static async Task RunDailyLoopAsync(IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("PerformanceScenarios");
        var rounds = int.TryParse(Environment.GetEnvironmentVariable("BODIAN_UI_PERF_ROUNDS"), out var requested)
            ? Math.Clamp(requested, 1, 30) : 5;
        // 逐项排除用：只跑列出的环节，默认全跑。
        var parts = (Environment.GetEnvironmentVariable("BODIAN_DAILY_PARTS") is { Length: > 0 } list
            ? list : "favorites,lyrics,bangs,discover,albums,artists")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var play = Environment.GetEnvironmentVariable("BODIAN_DAILY_PLAY") != "0";
        var lyricsRepeat = int.TryParse(Environment.GetEnvironmentVariable("BODIAN_DAILY_LYRICS_REPEAT"), out var repeat)
            ? Math.Clamp(repeat, 1, 50) : 1;
        try
        {
            var navigation = services.GetRequiredService<INavigationService>();
            var window = services.GetRequiredService<MainWindow>();
            var player = services.GetRequiredService<PlayerViewModel>();
            var bangPage = services.GetRequiredService<Func<Bang, BangDetailPage>>();
            var albumPage = services.GetRequiredService<Func<Album, AlbumDetailPage>>();
            var artistPage = services.GetRequiredService<Func<Artist, ArtistDetailPage>>();
            var playlistPage = services.GetRequiredService<Func<Playlist, int, PlaylistDetailPage>>();
            var aiPage = services.GetRequiredService<Func<AiPlaylistRef, string, AiPlaylistPage>>();

            await Task.Delay(TimeSpan.FromSeconds(3));
            logger.LogInformation("性能场景 daily-loop：{Rounds} 轮，环节 {Parts}，播放 {Play}，歌词页每轮 {Repeat} 次",
                rounds, string.Join(",", parts), play, lyricsRepeat);
            PageLifetimeDiagnostics.Report(logger, "初始");

            for (var round = 0; round < rounds; round++)
            {
                // 我喜欢的：第一轮从这里静音开播，之后保持播放。
                navigation.NavigateRoot<FavoritesPage>();
                await Task.Delay(Dwell);
                if (play && round == 0 && navigation.Current is FavoritesPage favorites)
                {
                    await WaitForAsync(() => favorites.ViewModel.Tracks.Count > 0);
                    if (favorites.ViewModel.Tracks.Count > 0)
                    {
                        player.Volume = 0;
                        favorites.ViewModel.PlayCommand.Execute(favorites.ViewModel.Tracks[0]);
                        await Task.Delay(Dwell);
                    }
                }
                if (parts.Contains("favorites") && navigation.Current is { } likedPage) await ScrollToEndAsync(likedPage);

                // 歌词页，打开播放列表再关上，然后退回。
                for (var pass = 0; parts.Contains("lyrics") && pass < lyricsRepeat; pass++)
                {
                    navigation.Navigate<LyricsPage>();
                    await Task.Delay(Dwell);
                    window.ToggleQueue();
                    await Task.Delay(Dwell);
                    window.ToggleQueue();
                    await Task.Delay(Step);
                    navigation.GoBack();
                    await Task.Delay(Step);
                }

                // 排行榜：每个榜进一次。
                if (parts.Contains("bangs")) navigation.NavigateRoot<BangListPage>();
                if (parts.Contains("bangs") && navigation.Current is BangListPage bangList)
                {
                    await WaitForAsync(() => bangList.ViewModel.Sections.Count > 0);
                    foreach (var bang in bangList.ViewModel.Sections.SelectMany(section => section).Select(item => item.Bang).Take(DailyItemLimit))
                        await VisitAsync(navigation, bangPage(bang));
                }

                // 发现页：每日推荐与个性化歌单等歌单卡片各进一次。
                if (parts.Contains("discover")) navigation.NavigateRoot<DiscoverPage>();
                if (parts.Contains("discover") && navigation.Current is DiscoverPage discover)
                {
                    await WaitForAsync(() => discover.ViewModel.Feeds.Count > 0);
                    var cards = discover.ViewModel.Feeds.SelectMany(feed => feed.Sections).SelectMany(section => section.Cards)
                        .Where(card => card.Ai is not null || card.Playlist is not null).Take(DailyItemLimit).ToList();
                    foreach (var card in cards)
                    {
                        Page page = card.Ai is { } ai ? aiPage(ai, card.Title)
                            : playlistPage(card.Playlist!, card.Playlist!.SourceType > 0 ? card.Playlist.SourceType : 4);
                        await VisitAsync(navigation, page);
                    }
                }

                // 收藏的专辑：每张进一次，有歌手的再点进歌手。
                if (parts.Contains("albums")) navigation.NavigateRoot<CollectedAlbumsPage>();
                if (parts.Contains("albums") && navigation.Current is CollectedAlbumsPage collected)
                {
                    await WaitForAsync(() => collected.ViewModel.Albums.Items.Count > 0);
                    foreach (var album in collected.ViewModel.Albums.Items.Take(DailyItemLimit).ToList())
                    {
                        var detail = albumPage(album);
                        navigation.Navigate(detail);
                        await Task.Delay(Dwell);
                        await ScrollToEndAsync(detail);
                        if (detail.ViewModel.HeaderArtistId > 0)
                        {
                            await VisitAsync(navigation, artistPage(new Artist
                            {
                                Id = detail.ViewModel.HeaderArtistId,
                                Name = detail.ViewModel.Album.ArtistText,
                            }));
                        }
                        navigation.GoBack();
                        await Task.Delay(Step);
                    }
                }

                // 关注的歌手：每位进一次。
                if (parts.Contains("artists")) navigation.NavigateRoot<FollowedArtistsPage>();
                if (parts.Contains("artists") && navigation.Current is FollowedArtistsPage followed)
                {
                    await WaitForAsync(() => followed.ViewModel.Artists.Items.Count > 0);
                    foreach (var artist in followed.ViewModel.Artists.Items.Take(DailyItemLimit).ToList())
                        await VisitAsync(navigation, artistPage(artist));
                }

                PageLifetimeDiagnostics.Report(logger, $"第 {round + 1} 轮");
            }

            await Task.Delay(TimeSpan.FromSeconds(10));
            PageLifetimeDiagnostics.Report(logger, "结束后静置 10 秒");
            await PageLifetimeDiagnostics.CollectAsync();
            PageLifetimeDiagnostics.Report(logger, "诊断回收后");
            logger.LogInformation("性能场景 daily-loop 完成");
        }
        catch (Exception exception) { logger.LogError(exception, "性能场景 daily-loop 失败"); }
    }

    /// <summary>压栈打开一个详情页，停留、滚到底，再返回。</summary>
    private static async Task VisitAsync(INavigationService navigation, Page page)
    {
        navigation.Navigate(page);
        await Task.Delay(Dwell);
        await ScrollToEndAsync(page);
        navigation.GoBack();
        await Task.Delay(Step);
    }

    /// <summary>等列表加载出来，最多等 10 秒。</summary>
    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 40 && !condition(); attempt++) await Task.Delay(250);
    }

    private const int DailyItemLimit = 20;

    /// <summary>顺着游标连取几页结果，凑出足够多互不重复的条目。</summary>
    private static async Task<List<T>> CollectAsync<T>(
        Func<PagedCursor, CancellationToken, Task<PagedResult<T>>> fetch, int pages)
    {
        var cursor = new PagedCursor(PagingConvention.ZeroBased);
        var items = new List<T>();
        for (var page = 0; page < pages; page++)
        {
            var result = await fetch(cursor, CancellationToken.None);
            if (result.IsEmpty) break;
            items.AddRange(result.Items);
            cursor.Advance(result.Items.Count);
            if (cursor.Exhausted) break;
        }
        return items;
    }

    /// <summary>
    /// 在详情页上做日常操作：歌单与专辑滚到底触发续页，歌手页切过三个页签，
    /// 第一轮的第一张专辑静音开播，之后一直播着。
    /// </summary>
    private static async Task ExerciseAsync(Page page, int round, PlayerViewModel player, ILogger logger)
    {
        try
        {
            switch (page)
            {
                case ArtistDetailPage artist:
                    for (var tab = 1; tab <= 3; tab++)
                    {
                        artist.ViewModel.SelectedTab = tab % 3;
                        await Task.Delay(Step);
                        await ScrollToEndAsync(page);
                    }
                    break;
                case AlbumDetailPage album:
                    if (round == 0 && album.ViewModel.Tracks.Items.Count > 0)
                    {
                        player.Volume = 0;
                        await album.ViewModel.PlayAsync(album.ViewModel.Tracks.Items[0]);
                    }
                    await ScrollToEndAsync(page);
                    break;
                default:
                    await ScrollToEndAsync(page);
                    break;
            }
        }
        catch (Exception exception) { logger.LogWarning(exception, "性能场景操作失败：{Page}", page.GetType().Name); }
    }

    /// <summary>把页面里每个可滚动的列表分几段滚到底，给续页和容器回收留出时间。</summary>
    private static async Task ScrollToEndAsync(Page page)
    {
        for (var pass = 0; pass < 6; pass++)
        {
            var moved = false;
            foreach (var scroll in ScrollViewers(page))
            {
                if (scroll.ScrollableHeight - scroll.VerticalOffset < 1) continue;
                scroll.ChangeView(null, Math.Min(scroll.ScrollableHeight, scroll.VerticalOffset + scroll.ViewportHeight * 1.5), null, true);
                moved = true;
            }
            if (!moved) return;
            await Task.Delay(Step);
        }
    }

    private static IEnumerable<ScrollViewer> ScrollViewers(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is ScrollViewer { Visibility: Visibility.Visible, ScrollableHeight: > 0 } viewer) yield return viewer;
            foreach (var nested in ScrollViewers(child)) yield return nested;
        }
    }

    private static readonly TimeSpan Dwell = TimeSpan.FromSeconds(2.5);
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(600);
}
