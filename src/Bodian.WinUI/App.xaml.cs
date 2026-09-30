using Bodian.Core.Api;
using Bodian.Core.Diagnostics;
using Bodian.Core.Models.Login;
using Bodian.Core.Services;
using Bodian.Core.Services.Abstractions;
using Bodian.Core.Services.Implementations;
using Bodian.WinUI.Playback;
using Bodian.WinUI.Services;
using Bodian.WinUI.ViewModels;
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
        // ★ 必须在创建任何窗口之前
        NativeMethods.SetCurrentProcessExplicitAppUserModelID("Bodian.WinUI");

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
        builder.Services.AddSingleton<PlaybackCoordinator>();

        // UI 基础设施
        builder.Services.AddSingleton<INavigationService, NavigationService>();
        builder.Services.AddSingleton<IQrImageFactory, QrImageFactory>();

        builder.Services.AddSingleton<MainWindow>();

        // 播放条常驻，所以 ViewModel 是单例；页面则每次导航新建。
        builder.Services.AddSingleton<PlayerViewModel>();
        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<SearchViewModel>();
        builder.Services.AddTransient<SearchPage>();

        _host = builder.Build();
        _host.Start();

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
            // 先放掉音频引擎（它会停 libmpv 的事件循环，卡住会拖住进程退出）。
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
    }

    private static SerilogLoggerProvider CreateSerilogProvider()
    {
        var logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(AppPaths.LogDirectory, "bodian-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();

        return new SerilogLoggerProvider(logger, dispose: true);
    }
}
