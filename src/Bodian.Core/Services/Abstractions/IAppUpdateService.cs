using Bodian.Core.Models;

namespace Bodian.Core.Services.Abstractions;

/// <summary>
/// 检查更新。
/// </summary>
/// <remarks>
/// <b>只由用户点按触发，不做自动检查</b> —— 不启动时静默联网、不后台轮询，
/// 与本项目「不含遥测与日志上报」的立场一致。
/// </remarks>
public interface IAppUpdateService
{
    /// <summary>当前配置。界面据此决定「项目主页」按钮是否可用。</summary>
    AppUpdateOptions Options { get; }

    /// <summary>
    /// 查一次最新版本。
    /// </summary>
    /// <remarks>
    /// <b>永不抛。</b> 失败一律返回 <see cref="AppUpdateStatus.Failed"/> 且带一句人话，
    /// 界面直接显示，不需要再包一层 try。
    /// </remarks>
    Task<AppUpdateCheckResult> CheckAsync(Version currentVersion, CancellationToken cancellationToken = default);
}
