namespace Bodian.Core.Models;

/// <summary>一次「检查更新」的结果状态。</summary>
public enum AppUpdateStatus
{
    /// <summary>还没配更新源。当前就是这个状态，见 <see cref="AppUpdateOptions"/>。</summary>
    NotConfigured,

    /// <summary>已经是最新的。</summary>
    UpToDate,

    /// <summary>有新版本。</summary>
    UpdateAvailable,

    /// <summary>检查失败（断网、接口变了）。</summary>
    Failed,
}

/// <summary>检查更新的结果。</summary>
/// <param name="Status">状态。</param>
/// <param name="LatestVersion">查到的最新版本。没查到就是 <c>null</c>。</param>
/// <param name="DownloadUri">去哪里下载。没查到就是 <c>null</c>。</param>
/// <param name="Message">给用户看的一句话。<b>永远是人话</b>，不是异常串。</param>
public sealed record AppUpdateCheckResult(
    AppUpdateStatus Status,
    Version? LatestVersion = null,
    Uri? DownloadUri = null,
    string Message = "")
{
    public static AppUpdateCheckResult NotConfigured(string message) =>
        new(AppUpdateStatus.NotConfigured, Message: message);

    public static AppUpdateCheckResult UpToDate(Version current) =>
        new(AppUpdateStatus.UpToDate, current, Message: $"已是最新版本（{Format(current)}）");

    public static AppUpdateCheckResult Available(Version latest, Uri download, string message) =>
        new(AppUpdateStatus.UpdateAvailable, latest, download, message);

    public static AppUpdateCheckResult Failed(string message) =>
        new(AppUpdateStatus.Failed, Message: message);

    /// <summary>版本号的显示形式：只到 <c>Major.Minor.Build</c>，不显示第四段。</summary>
    public static string Format(Version version) =>
        $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
}
