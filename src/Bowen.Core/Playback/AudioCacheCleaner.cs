using Bowen.Core.Services;
using Microsoft.Extensions.Logging;

namespace Bowen.Core.Playback;

/// <summary>
/// 清理 mpv 磁盘缓存目录里的崩溃残留。
/// </summary>
/// <remarks>
/// <para>
/// mpv 正常关闭媒体时会自己删掉 <c>mpv-cache-*.dat</c>；只有崩溃、强杀，或者
/// <c>mpv.Dispose()</c> 没来得及跑完才会留下。应用是单实例，启动这一刻目录里不可能
/// 有别的进程正在用这些文件，所以可以整个目录清。
/// </para>
/// <para>
/// <b>启动时清一次，引擎初始化时再清一次。</b> 前者保证「启动后一直没播放」也不会留下垃圾；
/// 后者覆盖运行期间才出现的残留 —— 正常路径不该有，作为兜底。
/// </para>
/// </remarks>
public static class AudioCacheCleaner
{
    /// <summary>mpv 的缓存文件模板：<c>mpv-cache-XXXXXX.dat</c>。</summary>
    public const string FilePattern = "mpv-cache-*.dat";

    /// <summary>
    /// 删除缓存目录里的所有 mpv 缓存文件，返回删除数量。
    /// </summary>
    /// <param name="logger">可选日志。删除失败只记 Debug，不向上抛。</param>
    /// <param name="directory">
    /// 默认 <see cref="AppPaths.AudioCacheDirectory"/>；测试可指向临时目录。
    /// </param>
    public static int ClearStale(ILogger? logger = null, string? directory = null)
    {
        directory ??= AppPaths.AudioCacheDirectory;

        try
        {
            Directory.CreateDirectory(directory);

            var removed = 0;
            foreach (var file in Directory.EnumerateFiles(directory, FilePattern))
            {
                try
                {
                    File.Delete(file);
                    removed++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger?.LogDebug(ex, "清理磁盘缓存残留失败：{File}", file);
                }
            }

            if (removed > 0)
            {
                logger?.LogInformation("清理磁盘缓存残留：{Count} 个文件", removed);
            }

            return removed;
        }
        catch (Exception ex)
        {
            // 清不掉只是多占一点磁盘，不能让启动失败。
            logger?.LogWarning(ex, "准备磁盘缓存目录失败");
            return 0;
        }
    }
}
