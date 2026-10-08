using Bodian.Core.Api;
using Bodian.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace Bodian.Core.Services.Implementations;

/// <summary>
/// 把多账号改造之前平铺在数据根目录下的三份用户数据，搬进账号作用域目录。
/// </summary>
/// <remarks>
/// <para>
/// <b>代码长期保留，不当天删。</b> 上一次改数据目录（<c>Bodian</c> → <c>Bowen</c>）带过一次迁移，
/// 在本机验证后就把代码删了，代价是从旧版本升级上来的用户会拿到新的设备标识并丢掉会话与队列。
/// 这次搬的是用户可见的数据，删之前要先确认没有别的机器、别的副本还在跑旧布局。
/// </para>
/// <para>
/// <b>归属由 <c>session.dat</c> 里的 uid 决定</b>，而不是等会话恢复完成。会话恢复发生在主窗口
/// 首次布局之后，比这里晚；而队列在更早的时候就已经被读进内存了。直接读凭据可以在
/// 「谁都没开始读数据」之前跑完，不依赖那一步的时序。没有凭据就归到匿名桶。
/// </para>
/// <para>
/// <b>幂等，且不用标记文件</b>：搬完源文件就没了，下一次自然什么都不做。
/// 目标已存在时<b>保留</b>两边、只记日志 —— 那说明这个账号已经有新数据了，宁可有重复文件也不删用户数据。
/// </para>
/// </remarks>
public static class AccountDataMigration
{
    /// <summary>要迁的三份数据。它们的名字仍从 <see cref="AppPaths"/> 里取，不在这里各写一遍。</summary>
    private static readonly string[] MigratedFiles =
    [
        AppPaths.PlayQueueFileName,
        AppPaths.PlayHistoryFileName,
        AppPaths.SearchHistoryFileName,
    ];

    /// <param name="credentials">用来定归属的会话凭据。</param>
    /// <param name="logger">日志。</param>
    /// <param name="rootDirectory">
    /// 数据根目录。默认 <see cref="AppPaths.LocalAppData"/>；<b>测试指向临时目录</b>，
    /// 否则跑测试会把本机真实数据搬走。
    /// </param>
    public static void Run(ICredentialStore credentials, ILogger logger, string? rootDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(logger);

        var root = rootDirectory ?? AppPaths.LocalAppData;
        var scope = ResolveScope(credentials, logger);

        foreach (var fileName in MigratedFiles)
        {
            Migrate(fileName, scope, root, logger);
        }
    }

    private static string ResolveScope(ICredentialStore credentials, ILogger logger)
    {
        try
        {
            var stored = credentials.Load();

            if (stored?.Uid is { Length: > 0 } uid && uid != BodianSession.AnonymousUid)
            {
                return uid;
            }
        }
        catch (Exception ex)
        {
            // 凭据读不出来不是迁移该拦的事：归到匿名桶，用户下次登录后看到的是空历史，
            // 比整个应用起不来好。凭据本身的问题会在别处报出来。
            logger.LogWarning(ex, "读取会话失败，旧数据按匿名归置");
        }

        return AppPaths.AnonymousScope;
    }

    private static void Migrate(string fileName, string scope, string root, ILogger logger)
    {
        var source = Path.Combine(root, fileName);

        if (!File.Exists(source))
        {
            return;
        }

        var target = AppPaths.AccountFilePath(scope, fileName, root);

        if (File.Exists(target))
        {
            logger.LogWarning("旧数据未迁移（目标已存在，两边都保留）：{Source} → {Target}", source, target);
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.Move(source, target);
            logger.LogInformation("已把旧数据迁入账号作用域：{FileName} → {Target}", fileName, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "迁移旧数据失败，保留原文件：{Source}", source);
        }
    }
}
