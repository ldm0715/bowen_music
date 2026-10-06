using System.Diagnostics;
using Bodian.Core.Models;
using Bodian.Core.Services.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.WinUI.ViewModels;

/// <summary>
/// 设置页「关于」分区。
/// </summary>
/// <remarks>
/// <b>版本号从程序集读，不用 <c>Package.Current</c></b>：本项目是 unpackaged 运行，
/// 没有 MSIX 身份，<c>Package.Current</c> 会直接抛。版本号的唯一来源是
/// <c>src/Directory.Build.props</c> 里的 <c>&lt;Version&gt;</c>。
/// </remarks>
public sealed partial class AboutViewModel : ObservableObject
{
    /// <summary>官方声明。README 里那份的同一段话，关于页必须写出来。</summary>
    public const string Disclaimer =
        "本项目为个人学习用途的非官方第三方客户端，与波点音乐官方无任何关联，未获其授权或认可。";

    private readonly IAppUpdateService _updates;
    private readonly ILogger _logger;

    public AboutViewModel(IAppUpdateService updates, ILogger<AboutViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(updates);

        _updates = updates;
        _logger = logger ?? NullLogger<AboutViewModel>.Instance;

        // 拿不到就显示 0.0.0 而不是崩 —— 关于页不值得为一个显示值把应用弄挂。
        var version = typeof(AboutViewModel).Assembly.GetName().Version ?? new Version(0, 0, 0);
        VersionText = AppUpdateCheckResult.Format(version);
        CurrentVersion = version;

        if (!_updates.Options.IsConfigured)
        {
            UpdateStatusText = "尚未配置更新源";
        }
    }

    /// <summary>当前版本，如 <c>0.1.0</c>。</summary>
    public string VersionText { get; }

    /// <summary>带前缀的版本号，如 <c>v0.1.0</c>。关于页那一行显示的是它。</summary>
    public string VersionLabel => $"v{VersionText}";

    /// <summary>底部的版权与许可行。</summary>
    public string CopyrightText =>
        $"Copyright © {DateTime.Now.Year} gcnanmu · 基于 GPL-3.0 发布";

    public Version CurrentVersion { get; }

    /// <summary>项目主页能不能点。仓库还没建，现在是 false。</summary>
    public bool CanOpenProject => _updates.Options.ProjectUri is not null;

    /// <summary>仓库地址能不能点。同上。</summary>
    public bool CanOpenRepository => _updates.Options.RepositoryUri is not null;

    /// <summary>正在检查。检查期间按钮禁用。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCheck))]
    public partial bool IsChecking { get; set; }

    /// <summary>检查结果或「尚未配置」的说明。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string UpdateStatusText { get; set; } = "";

    /// <summary>查到新版本时才有得下载。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDownload))]
    public partial Uri? DownloadUri { get; set; }

    /// <summary>「前往下载」按钮的显隐。给绑定用 —— x:Bind 里写不了判空。</summary>
    public bool HasDownload => DownloadUri is not null;

    /// <summary>非官方声明。与 README 里那段是同一句话。</summary>
    public string DisclaimerText => Disclaimer;

    public bool CanCheck => !IsChecking;

    public bool HasStatus => UpdateStatusText.Length > 0;

    [RelayCommand]
    private async Task CheckUpdateAsync()
    {
        IsChecking = true;
        DownloadUri = null;

        try
        {
            var result = await _updates.CheckAsync(CurrentVersion).ConfigureAwait(true);

            UpdateStatusText = result.Message;
            DownloadUri = result.DownloadUri;
        }
        finally
        {
            IsChecking = false;
        }
    }

    [RelayCommand]
    private void OpenProject() => Open(_updates.Options.ProjectUri);

    [RelayCommand]
    private void OpenRepository() => Open(_updates.Options.RepositoryUri);

    [RelayCommand]
    private void OpenDownload() => Open(DownloadUri);

    /// <remarks>
    /// 用 <c>Process.Start</c> + <c>UseShellExecute</c> 而不是 <c>Launcher.LaunchUriAsync</c>：
    /// 未打包应用没有 MSIX 身份，后者在部分环境里会静默失败。前者对
    /// <c>https://</c> 与 <c>file:///</c> 两种地址都能交给系统处理。
    /// </remarks>
    private void Open(Uri? target)
    {
        if (target is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(target.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "打开链接失败：{Uri}", target);
        }
    }
}
