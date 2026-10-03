using Bodian.Core.Api;
using Bodian.Core.Diagnostics;
using Bodian.Core.Lyrics;
using Bodian.Core.Models;
using Bodian.Core.Models.Home;
using Bodian.Core.Models.Login;
using Bodian.Core.Services;
using Bodian.Core.Services.Abstractions;
using Bodian.Core.Services.Implementations;
using Bodian.WinUI.Playback;
using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
using Bodian.WinUI.Controls;
using Bodian.WinUI.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Serilog;
using Serilog.Extensions.Logging;

namespace Bodian.WinUI;

/// <summary>
/// 应用入口，同时是 DI 组合根。
/// </summary>
/// <remarks>
/// <para>
/// P1 的 WinUI 只做到「能启动的空窗口 + 组合根」，不做导航、页面与 ViewModel。
/// 组合根是 P1 唯一无法用单测覆盖、又必须存在的东西——依赖装配只在这里发生。
/// </para>
/// <para>
/// 启动路径变长本身是风险（unpackaged + self-contained 的 bootstrapper 是本仓库踩过的坑），
/// 所以 Hosting 在这一阶段就接上，而不是留到 P2。
/// </para>
/// </remarks>
public partial class App : Application
{
    private readonly IHost _host;

    public App()
    {
        // ★ 必须在创建任何窗口之前。
        //   同一个 AUMID 也要写进开始菜单快捷方式（见 StartMenuShortcutInstaller），两处必须一致。
        NativeMethods.SetCurrentProcessExplicitAppUserModelID(AppIdentity.AppUserModelId);

        InitializeComponent();

        var builder = Host.CreateApplicationBuilder();

        // 只留 Serilog，去掉默认的控制台与 EventLog provider
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<ILoggerProvider>(CreateSerilogProvider());

        // ★ 脱敏收口：包住整个 ILoggerFactory，任何 provider 都绕不过
        //
        // 内层工厂用 GetServices<ILoggerProvider>() 显式搭出来，**不能**解析 ILoggerFactory 自身
        // ——那会无限递归。
        builder.Services.AddSingleton<ILoggerFactory>(sp =>
            new RedactingLoggerFactory(new LoggerFactory(sp.GetServices<ILoggerProvider>())));

        builder.Services.AddSingleton<BodianTransportOptions>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IDeviceIdentity, FileDeviceIdentity>();
        builder.Services.AddSingleton<ICredentialStore, DpapiCredentialStore>();

        // 播放历史是**明文 JSON**（不是凭据，不含账号标识）。
        // 显式写工厂：构造参数里有个可选的路径，交给容器按默认值挑容易出意外。
        builder.Services.AddSingleton<IPlayHistoryStore>(sp => new JsonPlayHistoryStore(
            clock: sp.GetRequiredService<TimeProvider>(),
            logger: sp.GetRequiredService<ILogger<JsonPlayHistoryStore>>()));

        // 外观设置同样是明文 JSON。构造参数里有个可选的路径，同样用显式工厂。
        // 它在主窗口构造时被同步读一次 —— 主题必须在第一帧之前定下来，否则会闪一下系统主题。
        builder.Services.AddSingleton<IThemeSettingsStore>(sp => new JsonThemeSettingsStore(
            logger: sp.GetRequiredService<ILogger<JsonThemeSettingsStore>>()));

        // 窗口位置记忆。单独一个文件（window.json）—— 几何是「这台机器」的事，
        // 而外观偏好是「这个人」的事，不混在一起。
        builder.Services.AddSingleton<IWindowPlacementStore>(sp => new JsonWindowPlacementStore(
            logger: sp.GetRequiredService<ILogger<JsonWindowPlacementStore>>()));
        builder.Services.AddSingleton<BodianSession>();
        // ★ 必须显式声明成 HttpMessageHandler。
        //   CreateHandler 返回的是 SocketsHttpHandler 这个**具体类型**，不写泛型参数就会按它注册，
        //   而 BodianHttpTransport 的构造参数类型是 HttpMessageHandler —— 按接口/基类解析不到，
        //   报 "Unable to resolve service for type 'HttpMessageHandler'"。
        //   P1 的组合根从不解析 IBodianTransport（窗口只注入 devid 与 session），所以这个错
        //   一直潜伏到 P2 第一次真正用到传输层才暴露。
        builder.Services.AddSingleton<HttpMessageHandler>(sp => BodianHttpTransport.CreateHandler(
            sp.GetRequiredService<BodianTransportOptions>()));
        builder.Services.AddSingleton<IBodianTransport, BodianHttpTransport>();

        // 业务门面与扫码登录
        builder.Services.AddSingleton<LoginOptions>();
        builder.Services.AddSingleton<IBodianApi, BodianApi>();
        builder.Services.AddSingleton<IBodianLogin, BodianLogin>();

        // 播放。引擎是单例，退出时显式释放（见 OnLaunched 的 Closed 处理）。
        builder.Services.AddSingleton<IPlaybackService>(sp => new LibMpvPlaybackService(
            sp.GetRequiredService<ILogger<LibMpvPlaybackService>>()));
        builder.Services.AddSingleton<IAudioQualitySettingsStore>(sp => new JsonAudioQualitySettingsStore(
            logger: sp.GetRequiredService<ILogger<JsonAudioQualitySettingsStore>>()));
        builder.Services.AddSingleton<IPlaybackSettingsStore>(sp => new JsonPlaybackSettingsStore(
            logger: sp.GetRequiredService<ILogger<JsonPlaybackSettingsStore>>()));
        builder.Services.AddSingleton<PlaybackCoordinator>();

        // 系统媒体控件。构造时只订阅事件，会话在首次播放时才建 —— 所以必须在这里解析一次，
        // 否则这个单例永远不会被实例化，事件订阅也就不存在。
        builder.Services.AddSingleton<SmtcManager>();

        // 歌词。仓库带缓存（同一首歌不重复取词），面板与播放条共享同一个实例。
        builder.Services.AddSingleton(sp => new LyricRepository(sp.GetRequiredService<IBodianApi>()));

        // UI 基础设施
        builder.Services.AddSingleton<INavigationService, NavigationService>();
        builder.Services.AddSingleton<IQrImageFactory, QrImageFactory>();
        builder.Services.AddSingleton<IStartMenuShortcutInstaller, StartMenuShortcutInstaller>();
        builder.Services.AddSingleton<IClipboardService, ClipboardService>();
        // 喜欢状态是会话级缓存，必须单例：播放条与歌词页共享同一份。
        builder.Services.AddSingleton<ILikedSongsService, LikedSongsService>();
        // 关注歌手状态同理：歌手详情各处共享同一份本地列表（详情接口没有 follow 字段）。
        builder.Services.AddSingleton<IFollowedArtistsService, FollowedArtistsService>();

        // 曲目行「更多」菜单的装配点。两个接口指向同一个实例：
        // 行内控件从 App 资源拿它，而动作 ViewModel 只认那两个接口（这样才能进离线测试）。
        builder.Services.AddSingleton<TrackActionsService>();
        builder.Services.AddSingleton<ITrackNavigator>(sp => sp.GetRequiredService<TrackActionsService>());
        builder.Services.AddSingleton<INoticeSink>(sp => sp.GetRequiredService<TrackActionsService>());

        builder.Services.AddSingleton<MainWindow>();

        // 外壳自己实现它：删歌单之后要把侧栏那一行摘掉，并决定当前页去哪 ——
        // 两件事都只有外壳知道。与 INoticeSink 指向 TrackActionsService 是同一种接线。
        builder.Services.AddSingleton<IPlaylistLibrarySink>(sp => sp.GetRequiredService<MainWindow>());

        // 选文件（封面）要在显示前绑定宿主窗口，句柄只有 MainWindow 拿得到。
        builder.Services.AddSingleton<IWindowHandleProvider>(sp => sp.GetRequiredService<MainWindow>());

        // 播放条与侧栏常驻，所以这几个 ViewModel 是单例；页面则每次导航新建。
        builder.Services.AddSingleton<TrackStatisticsViewModel>();
        builder.Services.AddSingleton<PlayerViewModel>();
        builder.Services.AddSingleton<LyricsViewModel>();
        builder.Services.AddSingleton<PlayQueueViewModel>();
        builder.Services.AddSingleton<AccountViewModel>();
        builder.Services.AddSingleton<SidebarViewModel>();
        builder.Services.AddSingleton<ISearchHistoryStore, JsonSearchHistoryStore>();
        builder.Services.AddSingleton<SearchViewModel>();
        builder.Services.AddTransient<Func<Artist, ArtistDetailPage>>(sp => artist =>
            new ArtistDetailPage(new ArtistDetailViewModel(
                sp.GetRequiredService<IBodianApi>(), sp.GetRequiredService<PlaybackCoordinator>(),
                sp.GetRequiredService<BodianSession>(), sp.GetRequiredService<IClipboardService>(), artist,
                sp.GetRequiredService<INoticeSink>(), sp.GetRequiredService<IFollowedArtistsService>(),
                sp.GetRequiredService<ILogger<ArtistDetailViewModel>>()),
                sp.GetRequiredService<INavigationService>(), sp.GetRequiredService<Func<Album, AlbumDetailPage>>()));
        builder.Services.AddSingleton<ThemeViewModel>();
        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<SearchPage>();
        builder.Services.AddTransient<SongCommentsViewModel>();
        builder.Services.AddTransient<LyricsPage>();
        builder.Services.AddTransient<FavoritesViewModel>();
        builder.Services.AddTransient<FavoritesPage>();
        builder.Services.AddTransient<RecentViewModel>();
        builder.Services.AddTransient<RecentPage>();
        builder.Services.AddTransient<CollectedAlbumsViewModel>();
        builder.Services.AddTransient<CollectedAlbumsPage>();
        builder.Services.AddTransient<CollectedPlaylistsViewModel>();
        builder.Services.AddTransient<CollectedPlaylistsPage>();

        builder.Services.AddTransient<DiscoverViewModel>();
        builder.Services.AddTransient<DiscoverPage>();
        builder.Services.AddTransient<BangListViewModel>();
        builder.Services.AddTransient<BangListPage>();
        builder.Services.AddTransient<LibraryViewModel>();
        builder.Services.AddTransient<LibraryPage>();

        // 乐库分类页要带「哪个大类」构造，DI 解析不出来 —— 用工厂。
        builder.Services.AddTransient<Func<MusicCategoryGroup, LibraryCategoryPage>>(sp =>
            group => new LibraryCategoryPage(
                new LibraryCategoryViewModel(
                    sp.GetRequiredService<IBodianApi>(),
                    group,
                    sp.GetRequiredService<ILogger<LibraryCategoryViewModel>>()),
                sp.GetRequiredService<INavigationService>(),
                sp.GetRequiredService<Func<Album, AlbumDetailPage>>()));

        // 专辑详情页要带「哪张专辑」构造，DI 解析不出来 —— 用工厂。
        builder.Services.AddTransient<Func<Album, AlbumDetailPage>>(sp => album =>
            new AlbumDetailPage(
                new AlbumDetailViewModel(
                    sp.GetRequiredService<IBodianApi>(),
                    sp.GetRequiredService<PlaybackCoordinator>(),
                    sp.GetRequiredService<BodianSession>(),
                    sp.GetRequiredService<IClipboardService>(),
                    sp.GetRequiredService<INoticeSink>(),
                    album,
                    sp.GetRequiredService<ILogger<AlbumDetailViewModel>>()),
                sp.GetRequiredService<INavigationService>(),
                sp.GetRequiredService<Func<Artist, ArtistDetailPage>>()));

        // AI 歌单页要带「哪个序号 + 什么标题」构造，DI 解析不出来 —— 用工厂。
        // 标题一并传进去：模块里那一组的标题与详情响应的 title 实测逐字相同，
        // 先填上可以让页面在请求回来之前就有标题，不至于空着。
        builder.Services.AddTransient<Func<AiPlaylistRef, string, AiPlaylistPage>>(sp => (target, title) =>
            new AiPlaylistPage(
                new AiPlaylistViewModel(
                    sp.GetRequiredService<IBodianApi>(),
                    sp.GetRequiredService<PlaybackCoordinator>(),
                    target,
                    title,
                    sp.GetRequiredService<ILogger<AiPlaylistViewModel>>())));

        // 榜详情要带「哪个榜」构造，DI 解析不出来 —— 用工厂。
        builder.Services.AddTransient<Func<Bang, BangDetailPage>>(sp => bang =>
            new BangDetailPage(new BangDetailViewModel(
                sp.GetRequiredService<IBodianApi>(),
                sp.GetRequiredService<PlaybackCoordinator>(),
                bang,
                sp.GetRequiredService<ILogger<BangDetailViewModel>>())));

        // 歌单详情要带「哪个歌单 + 哪个 source」构造，DI 解析不出来 —— 用工厂交给调用方，
        // 侧栏（自建歌单，source=5）与发现页（公开歌单，source=4）各传各的。
        builder.Services.AddTransient<Func<Playlist, int, PlaylistDetailPage>>(sp => (playlist, source) =>
            new PlaylistDetailPage(
                new PlaylistDetailViewModel(
                    sp.GetRequiredService<IBodianApi>(),
                    sp.GetRequiredService<PlaybackCoordinator>(),
                    sp.GetRequiredService<BodianSession>(),
                    sp.GetRequiredService<IClipboardService>(),
                    playlist,
                    source,
                    sp.GetRequiredService<INoticeSink>(),
                    sp.GetRequiredService<IPlaylistLibrarySink>(),
                    sp.GetRequiredService<ILogger<PlaylistDetailViewModel>>()),
                sp.GetRequiredService<IWindowHandleProvider>()));

        // Win2D 歌词控件由歌词页构造注入（XAML 实例化要求无参构造，所以不能直接写在 XAML 里）
        builder.Services.AddTransient<LyricsCanvasView>();
        builder.Services.AddTransient<AudioSpectrumSource>();
        builder.Services.AddTransient<AudioSpectrumView>();

        _host = builder.Build();
        _host.Start();

        // 未处理异常必须进日志。
        // unpackaged + WinUI 下崩溃只留一句 STATUS_STOWED_EXCEPTION（0xC000027B），
        // 事件日志里也只有「模块 combase.dll、异常码 E_INVALIDARG」这种够不着原因的信息。
        // 这三条是唯一能把堆栈留下来的地方。
        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // 设备标识**不是凭据**（它是假名化的设备串，没有它就没有账号访问能力），
        // 记下来是为了让「客户端与 P0 探针用的是同一个 devid」这条验收可以核对。
        // 换设备标识在账号风控看来是异常信号，值得留痕。
        _host.Services.GetRequiredService<ILogger<App>>().LogInformation(
            "启动完成：设备标识 {DeviceId}，日志目录 {LogDirectory}",
            _host.Services.GetRequiredService<IDeviceIdentity>().Value,
            AppPaths.LogDirectory);

#if DEBUG
        // 脱敏自检：故意发一条含假凭据的日志。
        // 它证明的是「封装是活的」而不是「调用点记得脱敏」——后者不是这个项目的做法。
        // 只出现在 Debug 构建，不污染发布版日志。
        _host.Services.GetRequiredService<ILogger<App>>()
            .LogInformation("脱敏自检：token=fake-token-for-redaction-check&freeSign=fake-signs&uid=99999999");
#endif
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow window;

        // 日志工厂也放进 App 资源：XAML 实例化的控件（构造函数必须无参）拿不到 DI 容器，
        // 这是它们唯一能拿到 logger 的通道。目前用它的有氛围背景层。
        // 曲目列表用的是同一个通道拿 PlayerViewModel，见 MainWindow 里的说明。
        Resources["BodianLoggerFactory"] = _host.Services.GetRequiredService<ILoggerFactory>();

        try
        {
            window = _host.Services.GetRequiredService<MainWindow>();
        }
        catch (Exception ex)
        {
            // unpackaged 下启动期的异常不会自己进日志，进程只给一句
            // STATUS_STOWED_EXCEPTION（0xC000027B），看不出原因。显式记下来。
            _host.Services.GetRequiredService<ILogger<App>>().LogCritical(ex, "创建主窗口失败");
            throw;
        }

        window.Closed += (_, _) =>
        {
            // 先撤 SMTC 会话（它读引擎状态，得在引擎之前放）。
            _host.Services.GetRequiredService<SmtcManager>().Dispose();

            // 再放掉音频引擎（它会停 libmpv 的事件循环，卡住会拖住进程退出）。
            // 同步等待：窗口已经关了，这里阻塞不影响交互。
            _host.Services.GetRequiredService<IPlaybackService>()
                .DisposeAsync()
                .AsTask()
                .GetAwaiter()
                .GetResult();

            // Dispose 会顺带 flush 掉 Serilog（注册时传了 dispose: true）
            _host.Dispose();
        };

        window.Activate();

        // 解析一次，让 SmtcManager 的订阅生效（它自己是懒初始化的，这里不会建会话）。
        _host.Services.GetRequiredService<SmtcManager>();

        // 开始菜单快捷方式要在窗口起来之后再补 —— 系统媒体面板的应用名与图标取自它。
        // 同步做（不挪后台线程）：COM 的 ShellLink 是 STA 对象，UI 线程是 STA，
        // 挪到线程池（MTA）要靠宿主单线程套间代理，没必要引入那层不确定性。
        // 代价只是一次很小的文件读写，失败了也只记日志。
        _host.Services.GetRequiredService<IStartMenuShortcutInstaller>().EnsureInstalled();
        _ = PerformanceScenarios.RunAsync(_host.Services);
    }

    /// <summary>XAML 线程上的未处理异常。</summary>
    /// <remarks>
    /// <b>不设 <c>e.Handled = true</c></b>：把这个异常吞掉会让程序带着坏了的状态继续跑，
    /// 那比崩掉更难查。这里只负责留下堆栈。
    /// </remarks>
    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        => LogFatal(e.Exception, "XAML 未处理异常");

    private void OnDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
        => LogFatal(e.ExceptionObject as Exception, "AppDomain 未处理异常");

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        => LogFatal(e.Exception, "未观察到的任务异常");

    /// <summary>把致命异常写进日志。找不到 logger 时退回标准错误。</summary>
    private void LogFatal(Exception? exception, string title)
    {
        try
        {
            _host.Services.GetRequiredService<ILogger<App>>().LogCritical(exception, "{Title}", title);
        }
        catch (Exception)
        {
            Console.Error.WriteLine($"{title}: {exception}");
        }
    }

    private static SerilogLoggerProvider CreateSerilogProvider()
    {
        var logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.WithProperty("ProcessId", Environment.ProcessId)
            .WriteTo.File(
                Path.Combine(AppPaths.LogDirectory, "bodian-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [PID {ProcessId}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        return new SerilogLoggerProvider(logger, dispose: true);
    }
}
