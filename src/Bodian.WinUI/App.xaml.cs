using Bodian.Core.Api;
using Bodian.Core.Diagnostics;
using Bodian.Core.Services;
using Bodian.Core.Services.Abstractions;
using Bodian.Core.Services.Implementations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
        builder.Services.AddSingleton(sp => BodianHttpTransport.CreateHandler(
            sp.GetRequiredService<BodianTransportOptions>()));
        builder.Services.AddSingleton<IBodianTransport, BodianHttpTransport>();
        builder.Services.AddSingleton<MainWindow>();

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
        var window = _host.Services.GetRequiredService<MainWindow>();

        // Dispose 会顺带 flush 掉 Serilog（注册时传了 dispose: true）
        window.Closed += (_, _) => _host.Dispose();
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
